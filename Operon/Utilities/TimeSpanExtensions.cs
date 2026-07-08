using System;

namespace SystemActivityTracker.Utilities
{
    public static class TimeSpanExtensions
    {
        // TEMP DIAGNOSTIC: seconds appended to every duration string app-wide to help
        // pinpoint a reported minute-level mismatch between Day/Week/Month/Week Report
        // totals. Revert by removing " {seconds} {secondUnit}" below once confirmed.
        public static string ToHoursMinutes(this TimeSpan value)
        {
            int hours = (int)value.TotalHours;
            int minutes = value.Minutes;
            int seconds = value.Seconds;

            string hourUnit = hours == 1 ? "hour" : "hours";
            string minuteUnit = minutes == 1 ? "minute" : "minutes";
            string secondUnit = seconds == 1 ? "second" : "seconds";

            return $"{hours} {hourUnit} {minutes} {minuteUnit} {seconds} {secondUnit}";
        }

        public static string ToHoursMinutes(this TimeSpan? value)
        {
            return value?.ToHoursMinutes() ?? "0 hours 0 minutes 0 seconds";
        }

        // Standard format for every "Expected" hours display app-wide: "Xh Ym", or just "Xh"
        // when there are no leftover minutes (e.g. "8h" rather than "8h 0m").
        public static string ToExpectedHoursText(this TimeSpan value)
        {
            int hours = (int)value.TotalHours;
            int minutes = value.Minutes;
            return minutes == 0 ? $"{hours}h" : $"{hours}h {minutes}m";
        }
    }
}
