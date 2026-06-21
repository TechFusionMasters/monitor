using System;
using System.Collections.Generic;
using System.Linq;
using SystemActivityTracker.Models;

namespace SystemActivityTracker.Utilities
{
    public static class ExpectedHoursCalculator
    {
        public const double StandardDayHours = 8;
        public const double StandardWeekHours = 40;

        public static double GetLeaveDeductionHours(LeaveDuration? duration) => duration switch
        {
            LeaveDuration.FullDay => StandardDayHours,
            LeaveDuration.MorningHalf => StandardDayHours / 2,
            LeaveDuration.AfternoonHalf => StandardDayHours / 2,
            _ => 0
        };

        public static TimeSpan GetDayExpectedHours(LeaveDuration? duration)
        {
            var expectedHours = StandardDayHours - GetLeaveDeductionHours(duration);
            return TimeSpan.FromHours(Math.Max(0, expectedHours));
        }

        public static TimeSpan GetWeekExpectedHours(IEnumerable<LeaveDuration?> leaveDurations)
        {
            var totalDeduction = leaveDurations.Sum(GetLeaveDeductionHours);
            return TimeSpan.FromHours(Math.Max(0, StandardWeekHours - totalDeduction));
        }

        public static TimeSpan GetWeekExpectedHours(DateTime weekStartMonday, Func<DateTime, LeaveDuration?> leaveLookup)
        {
            var leaveDurations = WorkWeekHelper.EnumerateWeekDays(weekStartMonday)
                .Select(date => leaveLookup(date));
            return GetWeekExpectedHours(leaveDurations);
        }

        public static int SumLeaveDeductionHours(IEnumerable<LeaveDuration?> leaveDurations) =>
            leaveDurations.Sum(d => (int)GetLeaveDeductionHours(d));
    }
}
