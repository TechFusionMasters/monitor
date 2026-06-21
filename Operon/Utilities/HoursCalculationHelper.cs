using System;
using System.Collections.Generic;
using System.Linq;
using SystemActivityTracker.Models;
using SystemActivityTracker.Services.Abstractions;

namespace SystemActivityTracker.Utilities
{
    public static class HoursCalculationHelper
    {
        public readonly record struct ActivityTotals(TimeSpan Active, TimeSpan Idle, TimeSpan Locked)
        {
            public static ActivityTotals Zero => new(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);
        }

        public static ActivityTotals SumActivityEntries(IEnumerable<ActivityLogEntry> entries)
        {
            TimeSpan active = TimeSpan.Zero;
            TimeSpan idle = TimeSpan.Zero;
            TimeSpan locked = TimeSpan.Zero;

            foreach (var entry in entries)
            {
                var duration = entry.EndTime - entry.StartTime;
                if (entry.IsLocked)
                {
                    locked += duration;
                }
                else if (entry.IsIdle)
                {
                    idle += duration;
                }
                else
                {
                    active += duration;
                }
            }

            return new ActivityTotals(active, idle, locked);
        }

        public static TimeSpan SumActiveOnly(IEnumerable<ActivityLogEntry> entries)
        {
            TimeSpan active = TimeSpan.Zero;
            foreach (var entry in entries)
            {
                if (!entry.IsLocked && !entry.IsIdle)
                {
                    active += entry.EndTime - entry.StartTime;
                }
            }

            return active;
        }

        public static TimeSpan SumTotalActive(TimeSpan trackedActive, TimeSpan manual) =>
            trackedActive + manual;

        /// <summary>
        /// Sums tracked active + manual for in-month days that belong to the given ISO work week.
        /// </summary>
        public static TimeSpan SumInMonthWeekTotalActive(
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

        public interface MonthlyDayItemLike
        {
            DateTime Date { get; }
            bool IsCurrentMonth { get; }
            TimeSpan TrackedActive { get; }
            TimeSpan Manual { get; }
        }
    }
}
