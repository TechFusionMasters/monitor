using System;

namespace SystemActivityTracker.Models
{
    public class ActivityRecord
    {
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }

        public string ProcessName { get; set; } = string.Empty;
        public string WindowTitle { get; set; } = string.Empty;

        public bool IsLocked { get; set; }
        public bool IsIdle { get; set; }
    }
}
