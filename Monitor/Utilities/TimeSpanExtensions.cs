using System;

namespace SystemActivityTracker.Utilities
{
    public static class TimeSpanExtensions
    {
        public static string ToHoursMinutes(this TimeSpan value)
        {
            int hours = (int)value.TotalHours;
            int minutes = value.Minutes;

            string hourUnit = hours == 1 ? "hour" : "hours";
            string minuteUnit = minutes == 1 ? "minute" : "minutes";

            return $"{hours} {hourUnit} {minutes} {minuteUnit}";
        }

        public static string ToHoursMinutes(this TimeSpan? value)
        {
            return value?.ToHoursMinutes() ?? "0 hours 0 minutes";
        }
    }
}
