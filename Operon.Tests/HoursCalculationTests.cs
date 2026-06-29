using System;
using System.Collections.Generic;
using SystemActivityTracker.Models;
using SystemActivityTracker.Utilities;
using Xunit;

namespace SystemActivityTracker.Tests
{
    public class ExpectedHoursCalculatorTests
    {
        // ── Working-day detection ────────────────────────────────────────────────────

        [Theory]
        [InlineData(DayOfWeek.Monday, true)]
        [InlineData(DayOfWeek.Tuesday, true)]
        [InlineData(DayOfWeek.Wednesday, true)]
        [InlineData(DayOfWeek.Thursday, true)]
        [InlineData(DayOfWeek.Friday, true)]
        [InlineData(DayOfWeek.Saturday, false)]
        [InlineData(DayOfWeek.Sunday, false)]
        public void IsWorkingDay_ReturnsCorrectly(DayOfWeek dow, bool expected)
        {
            // Find a date with the required day-of-week.
            var date = new DateTime(2025, 6, 2); // Monday
            while (date.DayOfWeek != dow) date = date.AddDays(1);
            Assert.Equal(expected, ExpectedHoursCalculator.IsWorkingDay(date));
        }

        // ── Day expected hours ───────────────────────────────────────────────────────

        [Fact]
        public void GetDayExpectedHours_Weekday_Returns8h()
        {
            var monday = new DateTime(2025, 6, 2); // Monday
            Assert.Equal(TimeSpan.FromHours(8), ExpectedHoursCalculator.GetDayExpectedHours(monday));
        }

        [Fact]
        public void GetDayExpectedHours_Weekend_Returns0h()
        {
            var saturday = new DateTime(2025, 6, 7);
            var sunday = new DateTime(2025, 6, 8);
            Assert.Equal(TimeSpan.Zero, ExpectedHoursCalculator.GetDayExpectedHours(saturday));
            Assert.Equal(TimeSpan.Zero, ExpectedHoursCalculator.GetDayExpectedHours(sunday));
        }

        // ── Leave credit ─────────────────────────────────────────────────────────────

        [Fact]
        public void GetDayLeaveCredit_FullDayOnWeekday_Returns8h()
        {
            var monday = new DateTime(2025, 6, 2);
            Assert.Equal(TimeSpan.FromHours(8),
                ExpectedHoursCalculator.GetDayLeaveCredit(monday, LeaveDuration.FullDay));
        }

        [Theory]
        [InlineData(LeaveDuration.MorningHalf)]
        [InlineData(LeaveDuration.AfternoonHalf)]
        public void GetDayLeaveCredit_HalfDayOnWeekday_Returns4h(LeaveDuration duration)
        {
            var tuesday = new DateTime(2025, 6, 3);
            Assert.Equal(TimeSpan.FromHours(4),
                ExpectedHoursCalculator.GetDayLeaveCredit(tuesday, duration));
        }

        [Fact]
        public void GetDayLeaveCredit_OnWeekend_Returns0h()
        {
            var saturday = new DateTime(2025, 6, 7);
            Assert.Equal(TimeSpan.Zero,
                ExpectedHoursCalculator.GetDayLeaveCredit(saturday, LeaveDuration.FullDay));
        }

        [Fact]
        public void GetDayLeaveCredit_NullDuration_Returns0h()
        {
            var monday = new DateTime(2025, 6, 2);
            Assert.Equal(TimeSpan.Zero,
                ExpectedHoursCalculator.GetDayLeaveCredit(monday, null));
        }

        // ── Week expected ────────────────────────────────────────────────────────────

        [Fact]
        public void GetWeekExpectedHours_Always40h()
        {
            Assert.Equal(TimeSpan.FromHours(40), ExpectedHoursCalculator.GetWeekExpectedHours());
        }

        // ── Month expected ───────────────────────────────────────────────────────────

        [Fact]
        public void GetMonthExpectedHours_June2025_Is168h()
        {
            // June 2025: 21 working days × 8h = 168h
            var result = ExpectedHoursCalculator.GetMonthExpectedHours(2025, 6);
            Assert.Equal(TimeSpan.FromHours(21 * 8), result);
        }

        [Fact]
        public void GetMonthExpectedHours_February2025_Is160h()
        {
            // Feb 2025: 20 working days × 8h = 160h
            var result = ExpectedHoursCalculator.GetMonthExpectedHours(2025, 2);
            Assert.Equal(TimeSpan.FromHours(20 * 8), result);
        }

        // ── Month-to-date expected hours ─────────────────────────────────────────────

        [Fact]
        public void GetMonthExpectedHours_CurrentMonth_CountsOnlyUpToToday()
        {
            // June 2025 starts on Sunday. Today = June 10 (Tue).
            // Working days: Jun 2–6 (5) + Jun 9–10 (2) = 7 days × 8h = 56h.
            var today = new DateTime(2025, 6, 10); // Tuesday
            var result = ExpectedHoursCalculator.GetMonthExpectedHours(2025, 6, today);
            Assert.Equal(TimeSpan.FromHours(7 * 8), result);
        }

        [Fact]
        public void GetMonthExpectedHours_PastMonth_ReturnsFullMonth()
        {
            // May 2025 is a past month (today = June 10, 2025): full month = 22 working days.
            var today = new DateTime(2025, 6, 10);
            var result = ExpectedHoursCalculator.GetMonthExpectedHours(2025, 5, today);
            Assert.Equal(TimeSpan.FromHours(22 * 8), result); // May 2025: 22 working days
        }

        [Fact]
        public void GetMonthExpectedHours_FutureMonth_ReturnsZero()
        {
            var today = new DateTime(2025, 6, 10);
            var result = ExpectedHoursCalculator.GetMonthExpectedHours(2025, 7, today);
            Assert.Equal(TimeSpan.Zero, result);
        }
    }

    public class PeriodHoursSummaryTests
    {
        // ── Status text ──────────────────────────────────────────────────────────────

        [Fact]
        public void StatusText_WhenTotalActiveEqualsExpected_IsCompleted()
        {
            var s = new HoursCalculationHelper.PeriodHoursSummary
            {
                Expected = TimeSpan.FromHours(40),
                Active = TimeSpan.FromHours(40),
                Leave = TimeSpan.Zero
            };
            Assert.Equal("Completed", s.StatusText);
        }

        [Fact]
        public void StatusText_WhenTotalActiveLessThanExpected_ShowsRemaining()
        {
            var s = new HoursCalculationHelper.PeriodHoursSummary
            {
                Expected = TimeSpan.FromHours(40),
                Active = TimeSpan.FromHours(32),
                Leave = TimeSpan.Zero
            };
            Assert.Equal("8h remaining", s.StatusText);
        }

        [Fact]
        public void StatusText_WhenTotalActiveMoreThanExpected_ShowsAbove()
        {
            var s = new HoursCalculationHelper.PeriodHoursSummary
            {
                Expected = TimeSpan.FromHours(40),
                Active = TimeSpan.FromHours(42),
                Leave = TimeSpan.Zero
            };
            Assert.Equal("2h above expected", s.StatusText);
        }

        [Fact]
        public void StatusText_WhenExpectedIsZero_IsEmpty()
        {
            var s = new HoursCalculationHelper.PeriodHoursSummary
            {
                Expected = TimeSpan.Zero,
                Active = TimeSpan.FromHours(4),
                Leave = TimeSpan.Zero
            };
            Assert.Equal(string.Empty, s.StatusText);
        }

        [Fact]
        public void StatusText_WithHoursAndMinutes_FormatsCorrectly()
        {
            var s = new HoursCalculationHelper.PeriodHoursSummary
            {
                Expected = TimeSpan.FromHours(40),
                Active = TimeSpan.FromHours(40) - TimeSpan.FromMinutes(30),
                Leave = TimeSpan.Zero
            };
            Assert.Equal("0h 30m remaining", s.StatusText);
        }
    }

    public class DaySummaryCalculationTests
    {
        // Example 1: Normal weekday, no leave.
        [Fact]
        public void Day_NormalWork_NoLeave()
        {
            var monday = new DateTime(2025, 6, 2);
            var s = HoursCalculationHelper.CalculateDaySummary(
                monday,
                trackedActive: TimeSpan.FromHours(7),
                manual: TimeSpan.FromHours(1),
                leaveDuration: null);

            Assert.Equal(TimeSpan.FromHours(8), s.Expected);
            Assert.Equal(TimeSpan.FromHours(8), s.Active);   // 7 tracked + 1 manual
            Assert.Equal(TimeSpan.Zero, s.Leave);
            Assert.Equal(TimeSpan.FromHours(8), s.TotalActive);
            Assert.Equal("Completed", s.StatusText);
        }

        // Example 2: Full-day leave, no work.
        [Fact]
        public void Day_FullLeave_NoWork()
        {
            var tuesday = new DateTime(2025, 6, 3);
            var s = HoursCalculationHelper.CalculateDaySummary(
                tuesday,
                trackedActive: TimeSpan.Zero,
                manual: TimeSpan.Zero,
                leaveDuration: LeaveDuration.FullDay);

            Assert.Equal(TimeSpan.FromHours(8), s.Expected);
            Assert.Equal(TimeSpan.Zero, s.Active);
            Assert.Equal(TimeSpan.FromHours(8), s.Leave);
            Assert.Equal(TimeSpan.FromHours(8), s.TotalActive);
            Assert.Equal("Completed", s.StatusText);
        }

        // Example 3: Full-day leave + 2h work.
        [Fact]
        public void Day_FullLeave_Plus2hWork()
        {
            var wednesday = new DateTime(2025, 6, 4);
            var s = HoursCalculationHelper.CalculateDaySummary(
                wednesday,
                trackedActive: TimeSpan.FromHours(2),
                manual: TimeSpan.Zero,
                leaveDuration: LeaveDuration.FullDay);

            Assert.Equal(TimeSpan.FromHours(8), s.Expected);
            Assert.Equal(TimeSpan.FromHours(2), s.Active);
            Assert.Equal(TimeSpan.FromHours(8), s.Leave);
            Assert.Equal(TimeSpan.FromHours(10), s.TotalActive);
            Assert.Equal("2h above expected", s.StatusText);
        }

        // Example 4: Half-day leave, worked 4h.
        [Fact]
        public void Day_HalfLeave_Worked4h()
        {
            var thursday = new DateTime(2025, 6, 5);
            var s = HoursCalculationHelper.CalculateDaySummary(
                thursday,
                trackedActive: TimeSpan.FromHours(4),
                manual: TimeSpan.Zero,
                leaveDuration: LeaveDuration.MorningHalf);

            Assert.Equal(TimeSpan.FromHours(8), s.Expected);
            Assert.Equal(TimeSpan.FromHours(4), s.Active);
            Assert.Equal(TimeSpan.FromHours(4), s.Leave);
            Assert.Equal(TimeSpan.FromHours(8), s.TotalActive);
            Assert.Equal("Completed", s.StatusText);
        }

        // Example 5: Half-day leave, worked 6h.
        [Fact]
        public void Day_HalfLeave_Worked6h()
        {
            var friday = new DateTime(2025, 6, 6);
            var s = HoursCalculationHelper.CalculateDaySummary(
                friday,
                trackedActive: TimeSpan.FromHours(6),
                manual: TimeSpan.Zero,
                leaveDuration: LeaveDuration.AfternoonHalf);

            Assert.Equal(TimeSpan.FromHours(8), s.Expected);
            Assert.Equal(TimeSpan.FromHours(6), s.Active);
            Assert.Equal(TimeSpan.FromHours(4), s.Leave);
            Assert.Equal(TimeSpan.FromHours(10), s.TotalActive);
            Assert.Equal("2h above expected", s.StatusText);
        }

        // Weekend work: expected=0h, status is empty.
        [Fact]
        public void Day_WeekendWork_ExpectedZero_StatusEmpty()
        {
            var saturday = new DateTime(2025, 6, 7);
            var s = HoursCalculationHelper.CalculateDaySummary(
                saturday,
                trackedActive: TimeSpan.FromHours(4),
                manual: TimeSpan.Zero,
                leaveDuration: null);

            Assert.Equal(TimeSpan.Zero, s.Expected);
            Assert.Equal(TimeSpan.FromHours(4), s.Active);
            Assert.Equal(TimeSpan.Zero, s.Leave);    // weekend leave = 0
            Assert.Equal(TimeSpan.FromHours(4), s.TotalActive);
            Assert.Equal(string.Empty, s.StatusText); // no status for weekends
        }

        // Weekend with a leave entry (e.g. user accidentally added leave): credit is 0.
        [Fact]
        public void Day_WeekendWithLeaveEntry_LeaveCreditIsZero()
        {
            var sunday = new DateTime(2025, 6, 8);
            var s = HoursCalculationHelper.CalculateDaySummary(
                sunday,
                trackedActive: TimeSpan.Zero,
                manual: TimeSpan.Zero,
                leaveDuration: LeaveDuration.FullDay);

            Assert.Equal(TimeSpan.Zero, s.Leave);
        }

        // Manual hours counted as Active.
        [Fact]
        public void Day_ManualHoursCountInActive()
        {
            var monday = new DateTime(2025, 6, 2);
            var s = HoursCalculationHelper.CalculateDaySummary(
                monday,
                trackedActive: TimeSpan.FromHours(6),
                manual: TimeSpan.FromHours(2),
                leaveDuration: null);

            Assert.Equal(TimeSpan.FromHours(8), s.Active);   // 6 + 2
            Assert.Equal(TimeSpan.FromHours(8), s.TotalActive);
            Assert.Equal("Completed", s.StatusText);
        }
    }

    public class WeekSummaryCalculationTests
    {
        // Normal week: 40h tracked, no leave.
        [Fact]
        public void Week_Normal40h_NoLeave()
        {
            var weekStart = new DateTime(2025, 6, 2); // Mon

            static (TimeSpan, TimeSpan) GetActivity(DateTime d) =>
                d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
                    ? (TimeSpan.Zero, TimeSpan.Zero)
                    : (TimeSpan.FromHours(8), TimeSpan.Zero);

            var s = HoursCalculationHelper.CalculateWeekSummary(weekStart, GetActivity, _ => null);

            Assert.Equal(TimeSpan.FromHours(40), s.Expected);
            Assert.Equal(TimeSpan.FromHours(40), s.Active);
            Assert.Equal(TimeSpan.Zero, s.Leave);
            Assert.Equal(TimeSpan.FromHours(40), s.TotalActive);
            Assert.Equal("Completed", s.StatusText);
        }

        // Full leave day (Monday), no work that day.
        [Fact]
        public void Week_FullLeaveDay_MondayNoWork()
        {
            var weekStart = new DateTime(2025, 6, 2); // Mon

            (TimeSpan, TimeSpan) GetActivity(DateTime d) =>
                d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || d == weekStart
                    ? (TimeSpan.Zero, TimeSpan.Zero)
                    : (TimeSpan.FromHours(8), TimeSpan.Zero);

            LeaveDuration? GetLeave(DateTime d) =>
                d == weekStart ? LeaveDuration.FullDay : null;

            var s = HoursCalculationHelper.CalculateWeekSummary(weekStart, GetActivity, GetLeave);

            Assert.Equal(TimeSpan.FromHours(40), s.Expected);
            Assert.Equal(TimeSpan.FromHours(32), s.Active);   // only Tue–Fri
            Assert.Equal(TimeSpan.FromHours(8), s.Leave);
            Assert.Equal(TimeSpan.FromHours(40), s.TotalActive);
            Assert.Equal("Completed", s.StatusText);
        }

        // Full leave day + 2h work on that day.
        [Fact]
        public void Week_FullLeaveDay_Worked2hOnLeaveDay()
        {
            var weekStart = new DateTime(2025, 6, 2); // Mon

            (TimeSpan, TimeSpan) GetActivity(DateTime d)
            {
                if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return (TimeSpan.Zero, TimeSpan.Zero);
                return d == weekStart
                    ? (TimeSpan.FromHours(2), TimeSpan.Zero)  // worked 2h on leave day
                    : (TimeSpan.FromHours(8), TimeSpan.Zero);
            }

            LeaveDuration? GetLeave(DateTime d) => d == weekStart ? LeaveDuration.FullDay : null;

            var s = HoursCalculationHelper.CalculateWeekSummary(weekStart, GetActivity, GetLeave);

            Assert.Equal(TimeSpan.FromHours(40), s.Expected);
            Assert.Equal(TimeSpan.FromHours(34), s.Active);   // 2h Mon + 32h Tue–Fri
            Assert.Equal(TimeSpan.FromHours(8), s.Leave);
            Assert.Equal(TimeSpan.FromHours(42), s.TotalActive);
            Assert.Equal("2h above expected", s.StatusText);
        }

        // Weekend work does NOT affect Expected but is counted in Active.
        [Fact]
        public void Week_WeekendWork_CountsInActiveNotExpected()
        {
            var weekStart = new DateTime(2025, 6, 2); // Mon

            (TimeSpan, TimeSpan) GetActivity(DateTime d)
            {
                if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return (TimeSpan.FromHours(4), TimeSpan.Zero);
                return (TimeSpan.FromHours(8), TimeSpan.Zero);
            }

            var s = HoursCalculationHelper.CalculateWeekSummary(weekStart, GetActivity, _ => null);

            Assert.Equal(TimeSpan.FromHours(40), s.Expected);
            Assert.Equal(TimeSpan.FromHours(48), s.Active);   // 40h weekdays + 8h weekend
            Assert.Equal(TimeSpan.Zero, s.Leave);
            Assert.Equal(TimeSpan.FromHours(48), s.TotalActive);
            Assert.Equal("8h above expected", s.StatusText);
        }

        // Example case: worked 32h (Mon–Thu 8h each) + full-day leave Friday = Total Active 40h.
        [Fact]
        public void Week_Worked32h_PlusFullLeaveDay_TotalActive40h()
        {
            var weekStart = new DateTime(2025, 6, 2); // Mon
            var friday = new DateTime(2025, 6, 6);

            (TimeSpan, TimeSpan) GetActivity(DateTime d)
            {
                // Worked Mon–Thu; took Friday as leave (0 tracked).
                if (d.DayOfWeek == DayOfWeek.Friday) return (TimeSpan.Zero, TimeSpan.Zero);
                if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return (TimeSpan.Zero, TimeSpan.Zero);
                return (TimeSpan.FromHours(8), TimeSpan.Zero);
            }

            LeaveDuration? GetLeave(DateTime d) =>
                d.Date == friday ? LeaveDuration.FullDay : null;

            var s = HoursCalculationHelper.CalculateWeekSummary(weekStart, GetActivity, GetLeave);

            Assert.Equal(TimeSpan.FromHours(40), s.Expected);
            Assert.Equal(TimeSpan.FromHours(32), s.Active);    // Mon–Thu 8h each
            Assert.Equal(TimeSpan.FromHours(8), s.Leave);      // Friday full-day leave credit
            Assert.Equal(TimeSpan.FromHours(40), s.TotalActive); // Active + Leave
            Assert.Equal("Completed", s.StatusText);
        }
    }

    public class MonthSummaryCalculationTests
    {
        // Cross-month week boundary: only days inside the month are counted.
        [Fact]
        public void Month_CrossBoundaryWeek_OnlyInMonthDaysCounted()
        {
            // January 2025: starts on Wed. Week of Jan 27 – Feb 2 has Feb days.
            // Test that Feb days are NOT included in Jan totals.

            int year = 2025, month = 1;
            var monthStart = new DateTime(year, month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1); // Jan 31

            (TimeSpan, TimeSpan) GetActivity(DateTime d)
            {
                // Only provide data for days inside January.
                if (d < monthStart || d > monthEnd) return (TimeSpan.Zero, TimeSpan.Zero);
                if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return (TimeSpan.Zero, TimeSpan.Zero);
                return (TimeSpan.FromHours(8), TimeSpan.Zero);
            }

            var s = HoursCalculationHelper.CalculateMonthSummary(year, month, GetActivity, _ => null);

            // Jan 2025 has 23 working days.
            Assert.Equal(TimeSpan.FromHours(23 * 8), s.Expected);
            Assert.Equal(TimeSpan.FromHours(23 * 8), s.Active);
            Assert.Equal("Completed", s.StatusText);
        }

        // Monthly leave credit only applies to Mon–Fri days.
        [Fact]
        public void Month_LeaveOnWeekend_NotCounted()
        {
            // Provide leave on a Saturday — should yield 0 leave credit.
            var saturday = new DateTime(2025, 6, 7);

            var s = HoursCalculationHelper.CalculateMonthSummary(
                2025, 6,
                _ => (TimeSpan.Zero, TimeSpan.Zero),
                d => d == saturday ? LeaveDuration.FullDay : null);

            Assert.Equal(TimeSpan.Zero, s.Leave);
        }

        // Monthly leave credit on weekdays.
        [Fact]
        public void Month_TwoFullLeavedays_Credits16h()
        {
            var monday1 = new DateTime(2025, 6, 2);
            var tuesday1 = new DateTime(2025, 6, 3);

            var s = HoursCalculationHelper.CalculateMonthSummary(
                2025, 6,
                _ => (TimeSpan.Zero, TimeSpan.Zero),
                d => (d == monday1 || d == tuesday1) ? LeaveDuration.FullDay : null);

            Assert.Equal(TimeSpan.FromHours(16), s.Leave);
        }
    }
}
