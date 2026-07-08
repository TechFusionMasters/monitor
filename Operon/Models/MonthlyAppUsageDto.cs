using System;

namespace SystemActivityTracker.Models
{
    public class MonthlyAppUsageDto
    {
        public string ProcessName { get; set; } = string.Empty;

        public TimeSpan TotalActive { get; set; }
        public TimeSpan TotalIdle { get; set; }
        public TimeSpan TotalLocked { get; set; }

        // NOT TimeSpan.ToString(@"hh\:mm"): custom-format "hh" is the hour-of-day component
        // (0-23, wraps every 24h), not a total-hours count. These are monthly sums that
        // routinely exceed 24h (e.g. accumulated Locked time across a whole month), so that
        // format silently showed the wrong, modulo-24 number once a total crossed a day
        // boundary. FormatTotalHoursMinutes uses TotalHours instead, keeping the same
        // zero-padded "HH:MM" look for values under 24h and extending correctly beyond it.
        public string TotalActiveText => FormatTotalHoursMinutes(TotalActive);
        public string TotalIdleText => FormatTotalHoursMinutes(TotalIdle);
        public string TotalLockedText => FormatTotalHoursMinutes(TotalLocked);

        // TEMP DIAGNOSTIC: seconds appended (":SS") to help pinpoint a reported minute-level
        // mismatch between Day/Week/Month/Week Report totals. Revert to the 2-arg "{0:00}:
        // {1:00}" format once confirmed.
        private static string FormatTotalHoursMinutes(TimeSpan value)
        {
            int totalHours = (int)value.TotalHours;
            int minutes = value.Minutes;
            int seconds = value.Seconds;
            return $"{totalHours:00}:{minutes:00}:{seconds:00}";
        }
    }
}
