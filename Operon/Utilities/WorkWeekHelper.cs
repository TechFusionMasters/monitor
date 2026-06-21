using System;
using System.Collections.Generic;
using System.Globalization;

namespace SystemActivityTracker.Utilities
{
    /// <summary>
    /// Monday–Sunday work-week boundaries used consistently across day, week, and month views.
    /// </summary>
    public static class WorkWeekHelper
    {
        public static DateTime GetWeekStartMonday(DateTime date)
        {
            var normalized = date.Date;
            int diff = (7 + (normalized.DayOfWeek - DayOfWeek.Monday)) % 7;
            return normalized.AddDays(-diff);
        }

        public static DateTime GetWeekEndSunday(DateTime date) => GetWeekStartMonday(date).AddDays(6);

        public static bool IsDateInWeek(DateTime date, DateTime weekStartMonday)
        {
            var day = date.Date;
            var start = weekStartMonday.Date;
            return day >= start && day <= start.AddDays(6);
        }

        public static IEnumerable<DateTime> EnumerateWeekDays(DateTime weekStartMonday)
        {
            var start = weekStartMonday.Date;
            for (int i = 0; i < 7; i++)
            {
                yield return start.AddDays(i);
            }
        }

        /// <summary>
        /// ISO week number for the Monday-based work week containing <paramref name="date"/>.
        /// </summary>
        public static int GetIsoWeekNumber(DateTime date) =>
            ISOWeek.GetWeekOfYear(GetWeekStartMonday(date));

        /// <summary>
        /// Inclusive Monday–Sunday grid range that covers every day in the calendar month.
        /// </summary>
        public static (DateTime GridStart, DateTime GridEnd) GetMonthCalendarGridRange(int year, int month)
        {
            var monthStart = new DateTime(year, month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            var gridStart = GetWeekStartMonday(monthStart);
            var gridEnd = GetWeekEndSunday(monthEnd);
            return (gridStart, gridEnd);
        }

        public static string FormatWeekRange(DateTime weekStartMonday, string format = "MMM d")
        {
            var start = weekStartMonday.Date;
            var end = start.AddDays(6);
            return $"{start.ToString(format, CultureInfo.CurrentCulture)} – {end.ToString(format, CultureInfo.CurrentCulture)}";
        }
    }
}
