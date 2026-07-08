using System;
using System.Collections.Generic;
using System.Linq;
using SystemActivityTracker.Models;
using SystemActivityTracker.Services.Abstractions;

namespace SystemActivityTracker.Utilities
{
    public static class HoursCalculationHelper
    {
        // ── Core result types ─────────────────────────────────────────────────────────

        public readonly record struct ActivityTotals(TimeSpan Active, TimeSpan Idle, TimeSpan Locked)
        {
            public static ActivityTotals Zero => new(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);
        }

        /// <summary>
        /// Unified period summary used for day, week, and month views.
        /// Active   = tracked computer time + manual task time (no leave).
        /// Leave    = approved leave credit (Mon–Fri only; full-day=8h, half-day=4h).
        /// TotalActive = Active + Leave.
        /// Expected = Mon–Fri working days × 8h; leave does NOT reduce it.
        /// </summary>
        public readonly struct PeriodHoursSummary
        {
            public TimeSpan Expected { get; init; }
            public TimeSpan Active { get; init; }
            public TimeSpan Leave { get; init; }
            public TimeSpan TotalActive => Active + Leave;

            public string StatusText
            {
                get
                {
                    if (Expected == TimeSpan.Zero) return string.Empty;
                    var diff = TotalActive - Expected;
                    if (diff == TimeSpan.Zero) return "Completed";
                    var abs = diff < TimeSpan.Zero ? -diff : diff;
                    var label = FormatDiff(abs);
                    return diff > TimeSpan.Zero ? $"{label} above expected" : $"{label} remaining";
                }
            }

            public string ExpectedText => FormatHm(Expected);
            public string ActiveText => FormatHm(Active);
            public string LeaveText => FormatHm(Leave);
            public string TotalActiveText => FormatHm(TotalActive);

            private static string FormatDiff(TimeSpan span)
            {
                int h = (int)span.TotalHours;
                int m = span.Minutes;
                return m == 0 ? $"{h}h" : $"{h}h {m}m";
            }

            private static string FormatHm(TimeSpan span)
            {
                int h = (int)span.TotalHours;
                int m = span.Minutes;
                string hu = h == 1 ? "hour" : "hours";
                string mu = m == 1 ? "minute" : "minutes";
                return $"{h} {hu} {m} {mu}";
            }
        }

        // ── Activity entry summation ──────────────────────────────────────────────────

        public static ActivityTotals SumActivityEntries(IEnumerable<ActivityLogEntry> entries)
        {
            TimeSpan active = TimeSpan.Zero;
            TimeSpan idle = TimeSpan.Zero;
            TimeSpan locked = TimeSpan.Zero;

            foreach (var entry in entries)
            {
                var duration = entry.EndTime - entry.StartTime;
                if (entry.IsLocked)
                    locked += duration;
                else if (entry.IsIdle)
                    idle += duration;
                else
                    active += duration;
            }

            return new ActivityTotals(active, idle, locked);
        }

        public static TimeSpan SumActiveOnly(IEnumerable<ActivityLogEntry> entries)
        {
            TimeSpan active = TimeSpan.Zero;
            foreach (var entry in entries)
            {
                if (!entry.IsLocked && !entry.IsIdle)
                    active += entry.EndTime - entry.StartTime;
            }
            return active;
        }

        public static TimeSpan SumTotalActive(TimeSpan trackedActive, TimeSpan manual) =>
            trackedActive + manual;

        // ── Period summary factories ──────────────────────────────────────────────────

        /// <summary>Builds a PeriodHoursSummary for a single calendar day.</summary>
        public static PeriodHoursSummary CalculateDaySummary(
            DateTime date,
            TimeSpan trackedActive,
            TimeSpan manual,
            LeaveDuration? leaveDuration)
        {
            return new PeriodHoursSummary
            {
                Expected = ExpectedHoursCalculator.GetDayExpectedHours(date),
                Active = trackedActive + manual,
                Leave = ExpectedHoursCalculator.GetDayLeaveCredit(date, leaveDuration)
            };
        }

        /// <summary>
        /// Builds a PeriodHoursSummary for the Mon–Sun week starting at <paramref name="weekStartMonday"/>.
        /// Only Mon–Fri days count toward Expected and Leave.
        /// Active includes all 7 days (weekend work is included).
        /// </summary>
        public static PeriodHoursSummary CalculateWeekSummary(
            DateTime weekStartMonday,
            Func<DateTime, (TimeSpan TrackedActive, TimeSpan Manual)> getActivity,
            Func<DateTime, LeaveDuration?> getLeaveDuration)
        {
            var totalActive = TimeSpan.Zero;
            var totalLeave = TimeSpan.Zero;

            foreach (var date in WorkWeekHelper.EnumerateWeekDays(weekStartMonday))
            {
                var (tracked, manual) = getActivity(date);
                totalActive += tracked + manual;
                totalLeave += ExpectedHoursCalculator.GetDayLeaveCredit(date, getLeaveDuration(date));
            }

            return new PeriodHoursSummary
            {
                Expected = ExpectedHoursCalculator.GetWeekExpectedHours(),
                Active = totalActive,
                Leave = totalLeave
            };
        }

        /// <summary>
        /// Builds a PeriodHoursSummary for a calendar month.
        /// Only days inside the selected month are counted (no cross-month week bleed-in).
        /// </summary>
        public static PeriodHoursSummary CalculateMonthSummary(
            int year,
            int month,
            Func<DateTime, (TimeSpan TrackedActive, TimeSpan Manual)> getActivity,
            Func<DateTime, LeaveDuration?> getLeaveDuration)
        {
            var start = new DateTime(year, month, 1);
            var end = start.AddMonths(1).AddDays(-1);

            var totalActive = TimeSpan.Zero;
            var totalLeave = TimeSpan.Zero;

            for (var d = start; d <= end; d = d.AddDays(1))
            {
                var (tracked, manual) = getActivity(d);
                totalActive += tracked + manual;
                totalLeave += ExpectedHoursCalculator.GetDayLeaveCredit(d, getLeaveDuration(d));
            }

            return new PeriodHoursSummary
            {
                Expected = ExpectedHoursCalculator.GetMonthExpectedHours(year, month),
                Active = totalActive,
                Leave = totalLeave
            };
        }

        // ── Monthly calendar helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Sums TotalActive (tracked + manual + leave) for in-month days in the given ISO week.
        /// Correctly excludes days from adjacent months when a week crosses a month boundary.
        /// </summary>
        // Burnt Hours for the Month View calendar's per-week Summary column: actual worked
        // time (tracked + manual) for that week's in-month days — regardless of leave/holiday
        // flags, since work done on a leave or holiday day still counts as burnt. Deliberately
        // excludes LeaveCredit (unlike the old SumInMonthWeekTotalActive this replaces), since
        // leave credit is time off, not time worked. Future days naturally contribute zero
        // (no tracked data exists yet), so no separate month-to-date cutoff is needed here.
        public static TimeSpan SumBurntHoursForWeek(
            IEnumerable<MonthlyDayItemLike> days,
            int isoWeekNumber,
            int year,
            int month)
        {
            return TimeSpan.FromSeconds(days
                .Where(d => d.IsCurrentMonth
                    && d.Date.Year == year
                    && d.Date.Month == month
                    && WorkWeekHelper.GetIsoWeekNumber(d.Date) == isoWeekNumber)
                .Sum(d => Math.Max(0, (d.TrackedActive + d.Manual).TotalSeconds)));
        }

        // Expected Hours for the Month View calendar's per-week Summary column — same rule as
        // the Monthly Usage summary's GetMonthlyExpectedHoursAdjusted(): 8h per Mon-Fri
        // in-month day, minus that day's holiday credit (holiday hours excluded). Leave does
        // NOT reduce this — a leave day still counts as its full 8h expected, i.e. leave hours
        // are "included" the same way the monthly figure treats them. Also month-to-date aware
        // like the monthly figure: days after `today` don't count yet, so a week that's only
        // partially elapsed doesn't show its full 40h before that time has passed.
        public static TimeSpan GetExpectedHoursForWeek(
            IEnumerable<MonthlyDayItemLike> days,
            int isoWeekNumber,
            int year,
            int month,
            DateTime today)
        {
            var weekDays = days.Where(d => d.IsCurrentMonth
                && d.Date.Year == year
                && d.Date.Month == month
                && WorkWeekHelper.GetIsoWeekNumber(d.Date) == isoWeekNumber
                && d.Date.Date <= today.Date);

            var baseExpected = TimeSpan.Zero;
            var holidayCredit = TimeSpan.Zero;
            foreach (var d in weekDays)
            {
                baseExpected += ExpectedHoursCalculator.GetDayExpectedHours(d.Date);
                holidayCredit += WorkSummaryCalculator.GetDayHolidayCredit(d.Date, d.IsHoliday);
            }

            var expected = baseExpected - holidayCredit;
            return expected < TimeSpan.Zero ? TimeSpan.Zero : expected;
        }

        public static int SumManualSecondsForMonth(int year, int month, Func<DateTime, int> getManualSecondsForDate)
        {
            var monthStart = new DateTime(year, month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            int total = 0;
            for (var date = monthStart; date <= monthEnd; date = date.AddDays(1))
            {
                total += getManualSecondsForDate(date);
            }
            return total;
        }

        // ── Interface for monthly calendar items ──────────────────────────────────────

        public interface MonthlyDayItemLike
        {
            DateTime Date { get; }
            bool IsCurrentMonth { get; }
            TimeSpan TrackedActive { get; }
            TimeSpan Manual { get; }
            TimeSpan LeaveCredit { get; }
            bool IsHoliday { get; }
        }
    }
}
