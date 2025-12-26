using System;

namespace SystemActivityTracker.Models
{
    public class MonthlyAppUsageDto
    {
        public string ProcessName { get; set; } = string.Empty;

        public TimeSpan TotalActive { get; set; }
        public TimeSpan TotalIdle { get; set; }
        public TimeSpan TotalLocked { get; set; }

        public string TotalActiveText => TotalActive.ToString(@"hh\:mm");
        public string TotalIdleText => TotalIdle.ToString(@"hh\:mm");
        public string TotalLockedText => TotalLocked.ToString(@"hh\:mm");
    }
}
