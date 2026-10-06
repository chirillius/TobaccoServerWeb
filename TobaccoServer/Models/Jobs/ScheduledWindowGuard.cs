namespace TobacoServer.Models.Jobs
{
    public static class ScheduledWindowGuard
    {
        private static readonly object _finalizedWindowsLock = new object();
        private static readonly HashSet<string> _finalizedWindows = new HashSet<string>();

        public static bool IsFinalFireInConfiguredWindow(DateTime now, TimeSpan intervalEndTime, TimeSpan period)
        {
            if (period <= TimeSpan.Zero)
                period = TimeSpan.FromSeconds(1);

            var normalizedEndTime = intervalEndTime == TimeSpan.Zero
                ? TimeSpan.FromDays(1)
                : intervalEndTime;

            return now.TimeOfDay < normalizedEndTime &&
                   now.TimeOfDay + period >= normalizedEndTime;
        }

        public static bool TryClaimFinalFire(
            string intervalKey,
            DateTime scheduledFireTime,
            TimeSpan intervalEndTime,
            TimeSpan period)
        {
            if (!IsFinalFireInConfiguredWindow(scheduledFireTime, intervalEndTime, period))
                return false;

            var finalizationKey = $"{intervalKey}:{scheduledFireTime:yyyy-MM-dd}";

            lock (_finalizedWindowsLock)
            {
                if (_finalizedWindows.Contains(finalizationKey))
                    return false;

                _finalizedWindows.Add(finalizationKey);
                return true;
            }
        }
    }
}
