using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VsixTreeViewer
{
    public static class Debouncer
    {
        private static readonly Dictionary<string, CancellationTokenSource> _tokens = new();
        private static readonly object _syncRoot = new();

        public static void Debounce(string uniqueKey, Action action, int milliseconds)
        {
            var tokenSource = new CancellationTokenSource();
            CancellationToken token = tokenSource.Token;

            lock (_syncRoot)
            {
                if (_tokens.TryGetValue(uniqueKey, out CancellationTokenSource existingToken))
                {
                    existingToken.Cancel();
                }

                _tokens[uniqueKey] = tokenSource;
            }

            _ = Task.Delay(milliseconds, token).ContinueWith(task =>
            {
                if (task.IsCanceled)
                {
                    CleanupToken(uniqueKey, tokenSource);
                    return;
                }

                try
                {
                    action();
                }
                finally
                {
                    CleanupToken(uniqueKey, tokenSource);
                }
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }

        public static void Cancel(string uniqueKey)
        {
            lock (_syncRoot)
            {
                if (_tokens.TryGetValue(uniqueKey, out CancellationTokenSource token))
                {
                    _tokens.Remove(uniqueKey);
                    token.Cancel();
                }
            }
        }

        private static void CleanupToken(string uniqueKey, CancellationTokenSource token)
        {
            lock (_syncRoot)
            {
                if (_tokens.TryGetValue(uniqueKey, out CancellationTokenSource currentToken) && ReferenceEquals(currentToken, token))
                {
                    _tokens.Remove(uniqueKey);
                }
            }

            token.Dispose();
        }
    }
}