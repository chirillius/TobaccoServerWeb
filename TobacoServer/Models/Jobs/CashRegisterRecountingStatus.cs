namespace TobacoServer.Models.Jobs
{
    public static class CashRegisterRecountingStatus
    {
        private static readonly object _lock = new object();
        private static bool _isRecountingWindowActive = false;
        private static bool _isRecountingCompleted = false;
        private static string _activeIntervalKey = string.Empty;

        public static bool IsRecountingWindowActive
        {
            get
            {
                lock (_lock)
                {
                    return _isRecountingWindowActive;
                }
            }
        }

        public static bool IsRecountingCompleted
        {
            get
            {
                lock (_lock)
                {
                    return _isRecountingCompleted;
                }
            }
        }

        public static void EnterWindow(string intervalKey)
        {
            lock (_lock)
            {
                if (_activeIntervalKey == intervalKey && _isRecountingWindowActive)
                    return;

                _activeIntervalKey = intervalKey;
                _isRecountingWindowActive = true;
                _isRecountingCompleted = false;
            }
        }

        public static void MarkCompleted()
        {
            lock (_lock)
            {
                if (_isRecountingWindowActive)
                {
                    _isRecountingCompleted = true;
                }
            }
        }

        public static void ExitWindow(string intervalKey)
        {
            lock (_lock)
            {
                if (_activeIntervalKey != intervalKey)
                    return;

                _activeIntervalKey = string.Empty;
                _isRecountingWindowActive = false;
                _isRecountingCompleted = false;
            }
        }
    }
}
