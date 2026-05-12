namespace TobacoServer.Models.Jobs
{
    public static class ScheduledWindowGuard
    {
        public static bool IsFinalFireInConfiguredWindow(DateTime now, TimeSpan intervalEndTime, TimeSpan period)
        {
            if (period <= TimeSpan.Zero)
                period = TimeSpan.FromSeconds(1);

            var normalizedEndTime = intervalEndTime == TimeSpan.Zero
                ? TimeSpan.FromDays(1)
                : intervalEndTime;

            return now.TimeOfDay + period >= normalizedEndTime;
        }
    }
}
