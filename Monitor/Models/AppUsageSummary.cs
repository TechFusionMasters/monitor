using System;

namespace SystemActivityTracker.Models
{
    public class AppUsageSummary
    {
        public string ProcessName { get; set; } = string.Empty;
        public TimeSpan ActiveDuration { get; set; }
        public string ActiveDurationText => ActiveDuration.ToString(@"hh\:mm");
    }
}
