using System;
using System.Collections.Generic;
using System.Linq;
using SystemActivityTracker.Models;

namespace SystemActivityTracker.Utilities
{
    public static class ExpectedHoursCalculator
    {
        public const double StandardDayHours = 8;
        public const double StandardWeekHours = 40; // 5 × 8h, Mon–Fri

        public static bool IsWorkingDay(DateTime date) =>
            date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday;

        // Expected hours for a day — 8h on Mon–Fri, 0 on Sat/Sun. Leave does NOT reduce this.
        public static TimeSpan GetDayExpectedHours(DateTime date) =>
            IsWorkingDay(date) ? TimeSpan.FromHours(StandardDayHours) : TimeSpan.Zero;

        // Leave credit for a specific date (0 on weekends regardless of leave entry).
        public static TimeSpan GetDayLeaveCredit(DateTime date, LeaveDuration? duration)
        {
            if (duration == null || !IsWorkingDay(date)) return TimeSpan.Zero;
            return duration switch
            {
                LeaveDuration.FullDay => TimeSpan.FromHours(StandardDayHours),
                LeaveDuration.MorningHalf or LeaveDuration.AfternoonHalf => TimeSpan.FromHours(StandardDayHours / 2),
                _ => TimeSpan.Zero
            };
        }

        // Week expected is always 40h (Mon–Fri × 8h). Leave does NOT reduce it.
        public static TimeSpan GetWeekExpectedHours() => TimeSpan.FromHours(StandardWeekHours);

        // Month expected = count of Mon–Fri days in the month × 8h (full month, used for past months and tests).
        public static TimeSpan GetMonthExpectedHours(int year, int month)
        {
            var start = new DateTime(year, month, 1);
            var end = start.AddMonths(1).AddDays(-1);
            int workdays = 0;
            for (var d = start; d <= end; d = d.AddDays(1))
            {
                if (IsWorkingDay(d)) workdays++;
            }
            return TimeSpan.FromHours(workdays * StandardDayHours);
        }

        // Month expected — month-to-date aware:
        //   current month → count Mon–Fri from month start up to and including today
        //   past months   → full month count
        //   future months → zero (nothing has been worked yet)
        public static TimeSpan GetMonthExpectedHours(int year, int month, DateTime today)
        {
            var monthStart = new DateTime(year, month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);

            // Future month: no hours expected yet.
            if (monthStart > today.Date) return TimeSpan.Zero;

            // Current month: count only up to today.
            // Past month: count the full month.
            var cutoff = (year == today.Year && month == today.Month) ? today.Date : monthEnd;

            int workdays = 0;
            for (var d = monthStart; d <= cutoff; d = d.AddDays(1))
            {
                if (IsWorkingDay(d)) workdays++;
            }
            return TimeSpan.FromHours(workdays * StandardDayHours);
        }

        // Total leave credit for a full week (Mon–Fri only; weekend leaves are ignored).
        public static TimeSpan GetWeekLeaveCredit(DateTime weekStartMonday, Func<DateTime, LeaveDuration?> leaveLookup)
        {
            var total = TimeSpan.Zero;
            foreach (var date in WorkWeekHelper.EnumerateWeekDays(weekStartMonday))
            {
                total += GetDayLeaveCredit(date, leaveLookup(date));
            }
            return total;
        }

        // Total leave credit for a month (Mon–Fri only inside the selected month).
        public static TimeSpan GetMonthLeaveCredit(int year, int month, Func<DateTime, LeaveDuration?> leaveLookup)
        {
            var start = new DateTime(year, month, 1);
            var end = start.AddMonths(1).AddDays(-1);
            var total = TimeSpan.Zero;
            for (var d = start; d <= end; d = d.AddDays(1))
            {
                total += GetDayLeaveCredit(d, leaveLookup(d));
            }
            return total;
        }

        // ── Backward-compat overloads kept for chart reference usage ──────────────────

        // Leave does not reduce expected; overload kept so chart call-sites still compile.
        public static TimeSpan GetDayExpectedHours(LeaveDuration? _) =>
            TimeSpan.FromHours(StandardDayHours);

        // Week expected is always 40h regardless of leave enumeration.
        public static TimeSpan GetWeekExpectedHours(IEnumerable<LeaveDuration?> _) =>
            GetWeekExpectedHours();

        public static TimeSpan GetWeekExpectedHours(DateTime weekStartMonday, Func<DateTime, LeaveDuration?> _) =>
            GetWeekExpectedHours();

        // Kept for leave-summary label helpers that still need the raw deduction amount.
        public static double GetLeaveDeductionHours(LeaveDuration? duration) => duration switch
        {
            LeaveDuration.FullDay => StandardDayHours,
            LeaveDuration.MorningHalf => StandardDayHours / 2,
            LeaveDuration.AfternoonHalf => StandardDayHours / 2,
            _ => 0
        };

        public static int SumLeaveDeductionHours(IEnumerable<LeaveDuration?> leaveDurations) =>
            leaveDurations.Sum(d => (int)GetLeaveDeductionHours(d));
    }
}
