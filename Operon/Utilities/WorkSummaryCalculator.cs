using System;
using System.Collections.Generic;
using SystemActivityTracker.Models;

namespace SystemActivityTracker.Utilities
{
    public enum WorkSummaryStatus
    {
        RemainingWork,
        TargetAchieved,
        ExtraWorked
    }

    // Per-day raw inputs the caller looks up (activity log, offline/manual entries,
    // leave, holiday) — kept provider-agnostic so any period (day/week/month/custom)
    // can be summed the same way.
    public readonly struct WorkDayInputs
    {
        public TimeSpan TrackedActive { get; init; }
        public TimeSpan OfflineWork { get; init; }
        public LeaveDuration? Leave { get; init; }
        public bool IsHoliday { get; init; }
    }

    public readonly struct WorkSummaryResult
    {
        public TimeSpan ExpectedHours { get; init; }
        public TimeSpan ActiveHours { get; init; }
        public TimeSpan OfflineWork { get; init; }
        public TimeSpan TotalActiveHours => ActiveHours + OfflineWork;
        public TimeSpan LeaveTaken { get; init; }
        public TimeSpan PublicHolidayHours { get; init; }
        public TimeSpan WeekendWork { get; init; }

        public TimeSpan ProgressHours => TotalActiveHours + LeaveTaken;

        public WorkSummaryStatus Status
        {
            get
            {
                if (ProgressHours < ExpectedHours) return WorkSummaryStatus.RemainingWork;
                if (ProgressHours > ExpectedHours) return WorkSummaryStatus.ExtraWorked;
                return WorkSummaryStatus.TargetAchieved;
            }
        }

        // 0..1, clamped — for driving a progress bar. Never exceeds 1 even when "Extra Worked".
        public double ProgressFraction
        {
            get
            {
                if (ExpectedHours <= TimeSpan.Zero)
                {
                    return ProgressHours > TimeSpan.Zero ? 1.0 : 0.0;
                }

                double fraction = ProgressHours.TotalSeconds / ExpectedHours.TotalSeconds;
                return Math.Clamp(fraction, 0.0, 1.0);
            }
        }
    }

    // Reuses ExpectedHoursCalculator's day-level primitives (weekday/weekend, leave credit)
    // plus holiday awareness to build Expected/Active/Leave/Holiday/Weekend totals for any
    // period. Existing ExpectedHoursCalculator methods are untouched — this only adds new,
    // purely additive day-level logic on top of them.
    public static class WorkSummaryCalculator
    {
        // Holiday credit for a date — 8h on a weekday holiday, 0 on a weekend holiday
        // (Sat/Sun are already 0 expected, so a holiday there must not double-subtract).
        public static TimeSpan GetDayHolidayCredit(DateTime date, bool isHoliday)
        {
            if (!isHoliday || !ExpectedHoursCalculator.IsWorkingDay(date))
            {
                return TimeSpan.Zero;
            }

            return TimeSpan.FromHours(ExpectedHoursCalculator.StandardDayHours);
        }

        // Expected hours for a single date after holiday and leave deductions (never negative).
        public static TimeSpan GetDayExpectedHours(DateTime date, bool isHoliday, LeaveDuration? leave)
        {
            var baseExpected = ExpectedHoursCalculator.GetDayExpectedHours(date);
            var holidayCredit = GetDayHolidayCredit(date, isHoliday);
            var leaveCredit = ExpectedHoursCalculator.GetDayLeaveCredit(date, leave);

            var expected = baseExpected - holidayCredit - leaveCredit;
            return expected < TimeSpan.Zero ? TimeSpan.Zero : expected;
        }

        public static WorkSummaryResult Calculate(IEnumerable<DateTime> days, Func<DateTime, WorkDayInputs> lookup)
        {
            var expected = TimeSpan.Zero;
            var active = TimeSpan.Zero;
            var offline = TimeSpan.Zero;
            var leaveTaken = TimeSpan.Zero;
            var holidayHours = TimeSpan.Zero;
            var weekendWork = TimeSpan.Zero;

            foreach (var date in days)
            {
                var inputs = lookup(date);
                var holidayCredit = GetDayHolidayCredit(date, inputs.IsHoliday);
                var leaveCredit = ExpectedHoursCalculator.GetDayLeaveCredit(date, inputs.Leave);

                var dayExpected = ExpectedHoursCalculator.GetDayExpectedHours(date) - holidayCredit - leaveCredit;
                expected += dayExpected < TimeSpan.Zero ? TimeSpan.Zero : dayExpected;

                active += inputs.TrackedActive;
                offline += inputs.OfflineWork;
                leaveTaken += leaveCredit;
                holidayHours += holidayCredit;

                if (!ExpectedHoursCalculator.IsWorkingDay(date))
                {
                    weekendWork += inputs.TrackedActive + inputs.OfflineWork;
                }
            }

            return new WorkSummaryResult
            {
                ExpectedHours = expected,
                ActiveHours = active,
                OfflineWork = offline,
                LeaveTaken = leaveTaken,
                PublicHolidayHours = holidayHours,
                WeekendWork = weekendWork
            };
        }

        // ── Day enumeration per view, all future-date-safe ────────────────────────────

        public static IEnumerable<DateTime> EnumerateTodayView(DateTime today) => new[] { today.Date };

        public static IEnumerable<DateTime> EnumerateWeekView(DateTime weekStartMonday) =>
            WorkWeekHelper.EnumerateWeekDays(weekStartMonday);

        // Month-to-date only: never yields days beyond today, and yields nothing for a
        // future month.
        public static IEnumerable<DateTime> EnumerateMonthView(int year, int month, DateTime today)
        {
            var start = new DateTime(year, month, 1);
            if (start > today.Date)
            {
                yield break;
            }

            var monthEnd = start.AddMonths(1).AddDays(-1);
            var cutoff = (year == today.Year && month == today.Month) ? today.Date : monthEnd;
            if (cutoff > monthEnd)
            {
                cutoff = monthEnd;
            }

            for (var d = start; d <= cutoff; d = d.AddDays(1))
            {
                yield return d;
            }
        }

        // Custom range, clamped so it never reaches into the future.
        public static IEnumerable<DateTime> EnumerateCustomView(DateTime start, DateTime end, DateTime today)
        {
            var rangeStart = start.Date;
            var rangeEnd = end.Date > today.Date ? today.Date : end.Date;

            for (var d = rangeStart; d <= rangeEnd; d = d.AddDays(1))
            {
                yield return d;
            }
        }
    }
}
