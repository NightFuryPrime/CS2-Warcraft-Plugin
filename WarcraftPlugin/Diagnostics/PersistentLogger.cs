using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace WarcraftPlugin.Diagnostics
{
    internal static class PersistentLogger
    {
        private readonly record struct LogWriteRequest(string Path, string Line, bool Flush);

        private static readonly object Sync = new();
        private static readonly Dictionary<string, DateTime> LastBreadcrumbByKey = new(StringComparer.OrdinalIgnoreCase);

        private static string _logDirectory = string.Empty;
        private static string _logPath = string.Empty;
        private static string _breadcrumbPath = string.Empty;
        private static string _lastBreadcrumbPath = string.Empty;
        private static Channel<LogWriteRequest> _writeQueue;
        private static Task _writerTask;
        private static bool _initialized;

        internal static void Initialize(string moduleDirectory)
        {
            lock (Sync)
            {
                if (_initialized)
                    return;

                _logDirectory = Path.Combine(moduleDirectory, "logs");
                Directory.CreateDirectory(_logDirectory);

                var dateStamp = DateTime.UtcNow.ToString("yyyyMMdd");
                _logPath = Path.Combine(_logDirectory, $"warcraft-{dateStamp}.log");
                _breadcrumbPath = Path.Combine(_logDirectory, "warcraft-breadcrumbs.log");
                _lastBreadcrumbPath = Path.Combine(_logDirectory, "warcraft-last-breadcrumb.log");
                var writeQueue = Channel.CreateUnbounded<LogWriteRequest>(new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false
                });
                _writeQueue = writeQueue;
                _writerTask = Task.Factory.StartNew(
                    () => RunWriterAsync(writeQueue.Reader),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default).Unwrap();

                AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
                TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
                _initialized = true;
            }

            Info(nameof(PersistentLogger), $"Initialized persistent logging in '{_logDirectory}'.", mirrorConsole: true);
        }

        internal static void Shutdown()
        {
            if (_initialized)
            {
                Info(nameof(PersistentLogger), "Persistent logger shutdown.", mirrorConsole: true);
            }

            Channel<LogWriteRequest> writeQueue;
            Task writerTask;

            lock (Sync)
            {
                if (!_initialized)
                    return;

                AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
                TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

                writeQueue = _writeQueue;
                writerTask = _writerTask;
                _writeQueue = null;
                _writerTask = null;
                _initialized = false;
            }

            writeQueue?.Writer.TryComplete();

            try
            {
                writerTask?.Wait(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // Best-effort shutdown only.
            }
        }

        internal static void Info(string source, string message, bool mirrorConsole = false)
        {
            WriteLine("INFO", source, message, mirrorConsole);
        }

        internal static void Warn(string source, string message, bool mirrorConsole = true)
        {
            WriteLine("WARN", source, message, mirrorConsole);
        }

        internal static void Error(string source, string message, Exception? ex = null, bool mirrorConsole = true)
        {
            var fullMessage = ex == null ? message : $"{message}{Environment.NewLine}{ex}";
            WriteLine("ERROR", source, fullMessage, mirrorConsole);
        }

        internal static void Breadcrumb(string source, string detail, int throttleMs = 250)
        {
            if (!_initialized)
                return;

            var now = DateTime.UtcNow;
            var key = $"{source}:{detail}";
            var line = $"{now:O} [{source}] {detail}";

            lock (Sync)
            {
                if (throttleMs > 0 &&
                    LastBreadcrumbByKey.TryGetValue(key, out var lastSeen) &&
                    (now - lastSeen).TotalMilliseconds < throttleMs)
                {
                    return;
                }

                LastBreadcrumbByKey[key] = now;
                TryEnqueue(_breadcrumbPath, line, flush: true);

                try
                {
                    File.WriteAllText(_lastBreadcrumbPath, line + Environment.NewLine);
                }
                catch
                {
                    // Best-effort only. We do not want logging to break gameplay.
                }
            }
        }

        private static void OnUnhandledException(object? sender, UnhandledExceptionEventArgs args)
        {
            var exception = args.ExceptionObject as Exception;
            Error("AppDomain.UnhandledException", $"IsTerminating={args.IsTerminating}", exception, mirrorConsole: true);
        }

        private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
        {
            Error("TaskScheduler.UnobservedTaskException", "An unobserved task exception reached the scheduler.", args.Exception, mirrorConsole: true);
        }

        private static void WriteLine(string level, string source, string message, bool mirrorConsole)
        {
            if (!_initialized)
                return;

            var line = $"{DateTime.UtcNow:O} [{level}] [{source}] {message}";

            TryEnqueue(_logPath, line, flush: false);

            if (mirrorConsole)
            {
                Console.WriteLine(line);
            }
        }

        private static void TryEnqueue(string path, string line, bool flush)
        {
            var queue = _writeQueue;
            if (queue == null)
                return;

            queue.Writer.TryWrite(new LogWriteRequest(path, line, flush));
        }

        private static async Task RunWriterAsync(ChannelReader<LogWriteRequest> reader)
        {
            var writers = new Dictionary<string, StreamWriter>(StringComparer.OrdinalIgnoreCase);

            try
            {
                while (await reader.WaitToReadAsync())
                {
                    while (reader.TryRead(out var entry))
                    {
                        try
                        {
                            if (!writers.TryGetValue(entry.Path, out var writer))
                            {
                                var stream = new FileStream(entry.Path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096);
                                writer = new StreamWriter(stream);
                                writers[entry.Path] = writer;
                            }

                            writer.WriteLine(entry.Line);
                            if (entry.Flush)
                            {
                                writer.Flush();
                            }
                        }
                        catch
                        {
                            // Avoid cascading failures inside gameplay code.
                        }
                    }
                }
            }
            catch
            {
                // Best-effort logging worker only.
            }
            finally
            {
                foreach (var writer in writers.Values)
                {
                    try
                    {
                        writer.Flush();
                        writer.Dispose();
                    }
                    catch
                    {
                        // Best-effort shutdown only.
                    }
                }
            }
        }
    }
}
