using Microsoft.Data.Sqlite;
using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace WarcraftPlugin.Core
{
    internal sealed class SqliteDispatcher : IDisposable
    {
        private readonly Channel<ISqliteWorkItem> _queue;
        private readonly CancellationTokenSource _shutdownCts = new();
        private readonly string _connectionString;
        private readonly Task _workerTask;

        private Exception _fatalException;
        private bool _disposed;
        private int _pendingCount;

        internal int PendingCount => Math.Max(0, Volatile.Read(ref _pendingCount));

        internal SqliteDispatcher(string connectionString)
        {
            _connectionString = connectionString;
            _queue = Channel.CreateUnbounded<ISqliteWorkItem>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

            _workerTask = Task.Factory.StartNew(
                RunWorkerAsync,
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).Unwrap();
        }

        internal Task ExecuteAsync(string description, Action<SqliteConnection> work)
        {
            return ExecuteAsync<object>(description, connection =>
            {
                work(connection);
                return null;
            });
        }

        internal Task<TResult> ExecuteAsync<TResult>(string description, Func<SqliteConnection, TResult> work)
        {
            if (_disposed)
                return Task.FromException<TResult>(new ObjectDisposedException(nameof(SqliteDispatcher)));

            if (_fatalException != null)
                return Task.FromException<TResult>(_fatalException);

            var workItem = new SqliteWorkItem<TResult>(description, work);
            Interlocked.Increment(ref _pendingCount);

            if (!_queue.Writer.TryWrite(workItem))
            {
                Interlocked.Decrement(ref _pendingCount);
                return Task.FromException<TResult>(new InvalidOperationException("SQLite dispatcher is not accepting work."));
            }

            return workItem.Task;
        }

        internal Task DrainAsync(string reason, TimeSpan timeout)
        {
            return ExecuteAsync($"drain:{reason}", _ => { }).WaitAsync(timeout);
        }

        internal async Task ShutdownAsync(string reason, TimeSpan timeout)
        {
            if (_disposed)
                return;

            try
            {
                await DrainAsync(reason, timeout);
            }
            finally
            {
                _queue.Writer.TryComplete();
                _shutdownCts.Cancel();

                try
                {
                    await _workerTask.WaitAsync(timeout);
                }
                catch
                {
                    // Best-effort shutdown. The caller already handled logging.
                }

                _disposed = true;
            }
        }

        private async Task RunWorkerAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                while (await _queue.Reader.WaitToReadAsync(_shutdownCts.Token))
                {
                    while (_queue.Reader.TryRead(out var workItem))
                    {
                        try
                        {
                            workItem.Execute(connection);
                        }
                        finally
                        {
                            Interlocked.Decrement(ref _pendingCount);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal during shutdown.
            }
            catch (Exception ex)
            {
                _fatalException = ex;
                while (_queue.Reader.TryRead(out var workItem))
                {
                    try
                    {
                        workItem.Fail(ex);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _pendingCount);
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            ShutdownAsync("dispose", TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }

        private interface ISqliteWorkItem
        {
            void Execute(SqliteConnection connection);
            void Fail(Exception ex);
        }

        private sealed class SqliteWorkItem<TResult> : ISqliteWorkItem
        {
            private readonly Func<SqliteConnection, TResult> _work;
            private readonly TaskCompletionSource<TResult> _tcs =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            internal Task<TResult> Task => _tcs.Task;

            internal SqliteWorkItem(string description, Func<SqliteConnection, TResult> work)
            {
                _work = work ?? throw new ArgumentNullException(nameof(work));
            }

            public void Execute(SqliteConnection connection)
            {
                try
                {
                    _tcs.TrySetResult(_work(connection));
                }
                catch (Exception ex)
                {
                    _tcs.TrySetException(ex);
                }
            }

            public void Fail(Exception ex)
            {
                _tcs.TrySetException(ex);
            }
        }
    }
}
