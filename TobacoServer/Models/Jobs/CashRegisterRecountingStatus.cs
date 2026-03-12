namespace TobacoServer.Models.Jobs
{
    public static class CashRegisterRecountingStatus
    {
        private static readonly object _lock = new object();
        private static bool _isRecountingInProgress = false;

        public static bool IsRecountingInProgress
        {
            get
            {
                lock (_lock)
                {
                    return _isRecountingInProgress;
                }
            }
            set
            {
                lock (_lock)
                {
                    _isRecountingInProgress = value;
                }
            }
        }
    }
}
