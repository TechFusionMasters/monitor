using System;

namespace SystemActivityTracker.Utilities
{
    public static class DurationFormatter
    {
        [Obsolete("Use TimeSpan.ToHoursMinutes() extension method instead")]
        public static string ToHoursMinutes(TimeSpan value) => value.ToHoursMinutes();
    }
}
