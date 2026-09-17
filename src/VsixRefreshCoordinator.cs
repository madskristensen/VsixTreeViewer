namespace VsixTreeViewer
{
    internal interface IDebounceDispatcher
    {
        void Debounce(string key, Action action, int milliseconds);
        void Cancel(string key);
    }

    internal sealed class DebounceDispatcher : IDebounceDispatcher
    {
        public static readonly DebounceDispatcher Instance = new();

        private DebounceDispatcher()
        {
        }

        public void Debounce(string key, Action action, int milliseconds)
        {
            Debouncer.Debounce(key, action, milliseconds);
        }

        public void Cancel(string key)
        {
            Debouncer.Cancel(key);
        }
    }

    internal sealed class VsixRefreshCoordinator : IDisposable
    {
        private const int RefreshDelayMilliseconds = 500;
        private readonly object _syncRoot = new();
        private readonly string _key;
        private readonly IDebounceDispatcher _dispatcher;
        private readonly Action<bool> _rebuild;
        private bool _isBuilding;
        private bool _isDisposed;

        public VsixRefreshCoordinator(
            string key,
            Action<bool> rebuild,
            IDebounceDispatcher dispatcher = null)
        {
            _key = key ?? throw new ArgumentNullException(nameof(key));
            _rebuild = rebuild ?? throw new ArgumentNullException(nameof(rebuild));
            _dispatcher = dispatcher ?? DebounceDispatcher.Instance;
        }

        public void BuildStarted()
        {
            lock (_syncRoot)
            {
                if (_isDisposed)
                {
                    return;
                }

                _isBuilding = true;
            }

            _dispatcher.Cancel(_key);
        }

        public void BuildCompleted(bool success)
        {
            lock (_syncRoot)
            {
                if (_isDisposed)
                {
                    return;
                }

                _isBuilding = false;
            }

            if (success)
            {
                Schedule(force: true);
            }
        }

        public void Schedule(bool force)
        {
            lock (_syncRoot)
            {
                if (_isDisposed || _isBuilding)
                {
                    return;
                }
            }

            _dispatcher.Debounce(_key, () =>
            {
                lock (_syncRoot)
                {
                    if (_isDisposed || _isBuilding)
                    {
                        return;
                    }
                }

                _rebuild(force);
            }, RefreshDelayMilliseconds);
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                if (_isDisposed)
                {
                    return;
                }

                _isDisposed = true;
            }

            _dispatcher.Cancel(_key);
        }
    }
}
