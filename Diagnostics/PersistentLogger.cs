using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace WarcraftPlugin.Diagnostics
{
    internal static class PersistentLogger
    {
        private static readonly object Sync = new();
        private static readonly Dictionary<string, DateTime> LastBreadcrumbByKey = new(StringComparer.OrdinalIgnoreCase);

        private static string _logDirectory = string.Empty;
        private static string _logPath = string.Empty;
        private static string _breadcrumbPath = string.Empty;
        private static string _lastBreadcrumbPath = string.Empty;
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

                AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
                TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
                _initialized = true;
            }

            Info(nameof(PersistentLogger), $"Initialized persistent logging in '{_logDirectory}'.", mirrorConsole: true);
        }

        internal static void Shutdown()
        {
            lock (Sync)
            {
                if (!_initialized)
                    return;

                AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
                TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
                _initialized = false;
            }

            Info(nameof(PersistentLogger), "Persistent logger shutdown.", mirrorConsole: true);
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
                AppendLine(_breadcrumbPath, line);

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

            lock (Sync)
            {
                AppendLine(_logPath, line);
            }

            if (mirrorConsole)
            {
                Console.WriteLine(line);
            }
        }

        private static void AppendLine(string path, string line)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096, FileOptions.WriteThrough);
                using var writer = new StreamWriter(stream);
                writer.WriteLine(line);
                writer.Flush();
                stream.Flush(true);
            }
            catch
            {
                // If file logging fails, avoid cascading failures inside gameplay code.
            }
        }
    }
}
