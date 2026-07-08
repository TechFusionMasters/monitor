using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SystemActivityTracker.Models;
using SystemActivityTracker.Services;
using SystemActivityTracker.Services.Abstractions;
using SystemActivityTracker.Utilities;

namespace SystemActivityTracker.ViewModels
{
    public enum AppUsageViewMode
    {
        Today,
        Week,
        Month,
        Custom
    }

    // Drives the Application Usage breakdown tree (Category → Application →
    // [Week →] Day → Session) AND the Insights summary card on the Application Usage
    // tab — both read the exact same period's active sessions, so they're kept on one
    // view model to avoid loading the same log files twice. Self-contained, own
    // navigation state — never touches the Monthly/Weekly tabs' existing state.
    // Category/Application totals are computed eagerly per period-load (cheap grouping
    // over already-read log entries); Week/Day/Session rows are only grouped and
    // materialized the first time their parent node is expanded (see UsageTreeNode),
    // so opening a month's breakdown doesn't pay for every individual session up front.
    public sealed class AppUsageBreakdownViewModel : INotifyPropertyChanged
    {
        private const string NoDataText = "—";

        private readonly IActivityLogReader _activityLogReader;
        private readonly AppCategoryService _categoryService;

        private AppUsageViewMode _viewMode = AppUsageViewMode.Today;
        private DateTime _selectedDate = DateTime.Today;
        private DateTime _weekStart = WorkWeekHelper.GetWeekStartMonday(DateTime.Today);
        private DateTime _monthYear = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        private DateTime _customStartDate = DateTime.Today.AddDays(-6);
        private DateTime _customEndDate = DateTime.Today;
        private TimeSpan _totalActive;

        private string _mostUsedApplicationName = NoDataText;
        private string _mostUsedApplicationHours = string.Empty;
        private string _mostUsedCategoryName = NoDataText;
        private string _mostUsedCategoryHours = string.Empty;
        private string _longestSessionName = NoDataText;
        private string _longestSessionHours = string.Empty;
        private string _peakProductivityTimeText = NoDataText;
        private string _averageSessionDurationText = NoDataText;
        private int _totalApplicationsUsed;
        private int _totalSessions;

        public AppUsageBreakdownViewModel(
            IActivityLogReader? activityLogReader = null,
            AppCategoryService? categoryService = null)
        {
            _activityLogReader = activityLogReader ?? new ActivityLogReader();
            _categoryService = categoryService ?? new AppCategoryService();

            PreviousPeriodCommand = new RelayCommand(_ => NavigatePeriod(-1), _ => CanNavigatePeriod());
            NextPeriodCommand = new RelayCommand(_ => NavigatePeriod(1), _ => CanNavigatePeriod());

            Refresh();
        }

        public ObservableCollection<UsageTreeNode> RootCategories { get; } = new ObservableCollection<UsageTreeNode>();

        public bool HasData => RootCategories.Count > 0;
        public string TotalActiveText => FormatHm(_totalActive);

        // Human-readable label for whichever period is currently selected — shown on the
        // Insights card so it's clear at a glance which period the numbers summarize,
        // without needing a second view-mode selector.
        public string PeriodLabel => ViewMode switch
        {
            AppUsageViewMode.Today => _selectedDate.ToString("ddd, MMM d"),
            AppUsageViewMode.Week => WeekRangeLabel,
            AppUsageViewMode.Month => _monthYear.ToString("MMMM yyyy"),
            AppUsageViewMode.Custom => $"{_customStartDate:MMM d} – {_customEndDate:MMM d}",
            _ => string.Empty
        };

        // ── Insights (same period, same active sessions as the tree above) ───────────
        // Name and hours are separate properties (rather than one combined "Name (Xh Ym)"
        // string) so the XAML can trim only the name when it's too long to fit — otherwise
        // a long process/category name plus TextTrimming="CharacterEllipsis" would clip the
        // "(Xh Ym)" suffix off the end first, making the hours silently disappear.
        public string MostUsedApplicationNameText => _mostUsedApplicationName;
        public string MostUsedApplicationHoursText => _mostUsedApplicationHours;
        public string MostUsedCategoryNameText => _mostUsedCategoryName;
        public string MostUsedCategoryHoursText => _mostUsedCategoryHours;
        public string LongestSessionNameText => _longestSessionName;
        public string LongestSessionHoursText => _longestSessionHours;
        public string PeakProductivityTimeText => _peakProductivityTimeText;
        public string AverageSessionDurationText => _averageSessionDurationText;
        public int TotalApplicationsUsed => _totalApplicationsUsed;
        public int TotalSessions => _totalSessions;

        public AppUsageViewMode ViewMode
        {
            get => _viewMode;
            set
            {
                if (_viewMode != value)
                {
                    _viewMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsTodayView));
                    OnPropertyChanged(nameof(IsWeekView));
                    OnPropertyChanged(nameof(IsMonthView));
                    OnPropertyChanged(nameof(IsCustomView));
                    OnPropertyChanged(nameof(PeriodLabel));
                    Refresh();
                }
            }
        }

        public bool IsTodayView => ViewMode == AppUsageViewMode.Today;
        public bool IsWeekView => ViewMode == AppUsageViewMode.Week;
        public bool IsMonthView => ViewMode == AppUsageViewMode.Month;
        public bool IsCustomView => ViewMode == AppUsageViewMode.Custom;

        public DateTime SelectedDate
        {
            get => _selectedDate;
            set
            {
                var normalized = value.Date > DateTime.Today ? DateTime.Today : value.Date;
                if (_selectedDate != normalized)
                {
                    _selectedDate = normalized;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PeriodLabel));
                    Refresh();
                }
            }
        }

        public DateTime MaxSelectableDate => DateTime.Today;

        public DateTime WeekPickerDate
        {
            get => _weekStart;
            set
            {
                var normalized = WorkWeekHelper.GetWeekStartMonday(value);
                if (_weekStart != normalized)
                {
                    _weekStart = normalized;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(WeekRangeLabel));
                    OnPropertyChanged(nameof(PeriodLabel));
                    Refresh();
                }
            }
        }

        public string WeekRangeLabel => WorkWeekHelper.FormatWeekRange(_weekStart);

        public DateTime MonthYear
        {
            get => _monthYear;
            set
            {
                var normalized = new DateTime(value.Year, value.Month, 1);
                if (_monthYear != normalized)
                {
                    _monthYear = normalized;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PeriodLabel));
                    Refresh();
                }
            }
        }

        public DateTime CustomStartDate
        {
            get => _customStartDate;
            set
            {
                var normalized = value.Date;
                if (_customStartDate != normalized)
                {
                    _customStartDate = normalized;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PeriodLabel));
                    Refresh();
                }
            }
        }

        public DateTime CustomEndDate
        {
            get => _customEndDate;
            set
            {
                var normalized = value.Date > DateTime.Today ? DateTime.Today : value.Date;
                if (_customEndDate != normalized)
                {
                    _customEndDate = normalized;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PeriodLabel));
                    Refresh();
                }
            }
        }

        public ICommand PreviousPeriodCommand { get; }
        public ICommand NextPeriodCommand { get; }

        private bool CanNavigatePeriod() => ViewMode == AppUsageViewMode.Week || ViewMode == AppUsageViewMode.Month;

        private void NavigatePeriod(int direction)
        {
            if (ViewMode == AppUsageViewMode.Week)
            {
                WeekPickerDate = _weekStart.AddDays(7 * direction);
            }
            else if (ViewMode == AppUsageViewMode.Month)
            {
                MonthYear = _monthYear.AddMonths(direction);
            }
        }

        // Called after Application Categories are edited/saved so this breakdown
        // reflects the new mapping without needing to switch tabs.
        public void Refresh()
        {
            var today = DateTime.Today;
            var mode = ViewMode;

            List<ActivityLogEntry> entries = mode switch
            {
                AppUsageViewMode.Today => ReadDayEntries(_selectedDate),
                AppUsageViewMode.Week => ReadRangeEntries(_weekStart, _weekStart.AddDays(6), today),
                AppUsageViewMode.Month => ReadRangeEntries(
                    new DateTime(_monthYear.Year, _monthYear.Month, 1),
                    new DateTime(_monthYear.Year, _monthYear.Month, 1).AddMonths(1).AddDays(-1),
                    today),
                AppUsageViewMode.Custom => ReadRangeEntries(_customStartDate, _customEndDate, today),
                _ => new List<ActivityLogEntry>()
            };

            // Application Usage (and Insights, computed below from the same list) only
            // ever reflects real active application sessions — Offline Work, Leave, and
            // Public Holidays never produce ActivityLogEntry rows, so nothing further is
            // needed to keep them out of this breakdown.
            var activeEntries = entries
                .Where(e => !e.IsLocked && !e.IsIdle && !string.IsNullOrWhiteSpace(e.ProcessName))
                .ToList();

            RootCategories.Clear();
            _totalActive = TimeSpan.Zero;

            if (activeEntries.Count == 0)
            {
                ResetInsights();
                RaiseTreeAndInsightsChanged();
                return;
            }

            var categories = _categoryService.LoadAll();
            var grandTotal = SumDuration(activeEntries);
            _totalActive = grandTotal;

            var byCategory = new Dictionary<AppCategory, List<ActivityLogEntry>>();
            foreach (var entry in activeEntries)
            {
                var category = _categoryService.GetCategoryForProcess(entry.ProcessName, categories);
                if (!byCategory.TryGetValue(category, out var list))
                {
                    list = new List<ActivityLogEntry>();
                    byCategory[category] = list;
                }
                list.Add(entry);
            }

            foreach (var kvp in byCategory.OrderByDescending(k => SumDuration(k.Value)))
            {
                var categoryEntries = kvp.Value;
                var categoryDuration = SumDuration(categoryEntries);
                var categoryPercent = grandTotal > TimeSpan.Zero
                    ? categoryDuration.TotalSeconds / grandTotal.TotalSeconds * 100
                    : 0;

                RootCategories.Add(new UsageTreeNode(
                    UsageNodeKind.Category,
                    kvp.Key.Name,
                    categoryDuration,
                    categoryPercent,
                    childrenFactory: () => BuildApplicationNodes(categoryEntries, categoryDuration, mode)));
            }

            ComputeInsights(activeEntries, byCategory);
            RaiseTreeAndInsightsChanged();
        }

        private void ComputeInsights(List<ActivityLogEntry> activeEntries, Dictionary<AppCategory, List<ActivityLogEntry>> byCategory)
        {
            var byApp = activeEntries.GroupBy(e => e.ProcessName, StringComparer.OrdinalIgnoreCase).ToList();

            var topApp = byApp.OrderByDescending(SumDuration).First();
            _mostUsedApplicationName = topApp.Key;
            _mostUsedApplicationHours = FormatHm(SumDuration(topApp));

            var topCategory = byCategory.OrderByDescending(kvp => SumDuration(kvp.Value)).First();
            _mostUsedCategoryName = topCategory.Key.Name;
            _mostUsedCategoryHours = FormatHm(SumDuration(topCategory.Value));

            var peakHour = activeEntries.GroupBy(e => e.StartTime.Hour).OrderByDescending(SumDuration).First().Key;
            _peakProductivityTimeText = FormatHourRange(peakHour);

            // Longest/Average/Total Sessions all mean a continuous block of time in one app —
            // but TrackingService writes a new ActivityLogEntry row every time the window
            // TITLE changes, not just the process (see the record-rotation condition in
            // TrackingService), so a single long browsing/coding session is split across many
            // short, genuinely back-to-back rows. Treating each raw row as its own "session"
            // made Longest/Average measure title-change fragments (often just seconds) instead
            // of real usage length, rounding down to "0h 0m" almost every time.
            // MergeIntoSessionBlocks (also used by BuildSessionNodes for the tree's Session
            // rows, so both can't drift apart) collapses only truly contiguous consecutive
            // same-process rows back into one session first — no gap-tolerance threshold, so a
            // real interruption always starts a new session; Most Used Application/Category are
            // unaffected since they already sum across all rows per app regardless of
            // fragmentation.
            var sessions = MergeIntoSessionBlocks(activeEntries.OrderBy(e => e.StartTime).ToList());

            var longest = sessions.OrderByDescending(s => s.End - s.Start).First();
            _longestSessionName = longest.ProcessName;
            _longestSessionHours = FormatHm(longest.End - longest.Start);

            var totalSessionTicks = sessions.Sum(s => (s.End - s.Start).Ticks);
            _averageSessionDurationText = FormatHm(TimeSpan.FromTicks(totalSessionTicks / sessions.Count));

            _totalApplicationsUsed = byApp.Count;
            _totalSessions = sessions.Count;
        }

        // Merges time-ordered entries into logical session blocks: consecutive rows for the
        // same process with no gap between them (the next row's StartTime is at or before the
        // running block's current EndTime — i.e. genuinely back-to-back or overlapping) count
        // as one continuous session. A different process, or any real time gap, ends the
        // current block and starts a new one — no tolerance threshold, so only data that
        // unambiguously belongs to the same session is merged. Shared by ComputeInsights
        // (Longest/Average/Total Session stats) and BuildSessionNodes (the tree's Session rows)
        // so the two can't define "session" differently.
        private static List<(string ProcessName, DateTime Start, DateTime End)> MergeIntoSessionBlocks(
            List<ActivityLogEntry> orderedEntries)
        {
            var blocks = new List<(string ProcessName, DateTime Start, DateTime End)>();
            if (orderedEntries.Count == 0)
            {
                return blocks;
            }

            var groupProcessName = orderedEntries[0].ProcessName;
            var groupStart = orderedEntries[0].StartTime;
            var groupEnd = orderedEntries[0].EndTime;

            for (int i = 1; i < orderedEntries.Count; i++)
            {
                var entry = orderedEntries[i];
                bool sameProcess = string.Equals(entry.ProcessName, groupProcessName, StringComparison.OrdinalIgnoreCase);
                bool contiguous = entry.StartTime <= groupEnd;

                if (sameProcess && contiguous)
                {
                    if (entry.EndTime > groupEnd)
                    {
                        groupEnd = entry.EndTime;
                    }
                }
                else
                {
                    blocks.Add((groupProcessName, groupStart, groupEnd));
                    groupProcessName = entry.ProcessName;
                    groupStart = entry.StartTime;
                    groupEnd = entry.EndTime;
                }
            }

            blocks.Add((groupProcessName, groupStart, groupEnd));
            return blocks;
        }

        private void ResetInsights()
        {
            _mostUsedApplicationName = NoDataText;
            _mostUsedApplicationHours = string.Empty;
            _mostUsedCategoryName = NoDataText;
            _mostUsedCategoryHours = string.Empty;
            _longestSessionName = NoDataText;
            _longestSessionHours = string.Empty;
            _peakProductivityTimeText = NoDataText;
            _averageSessionDurationText = NoDataText;
            _totalApplicationsUsed = 0;
            _totalSessions = 0;
        }

        private void RaiseTreeAndInsightsChanged()
        {
            OnPropertyChanged(nameof(HasData));
            OnPropertyChanged(nameof(TotalActiveText));
            OnPropertyChanged(nameof(MostUsedApplicationNameText));
            OnPropertyChanged(nameof(MostUsedApplicationHoursText));
            OnPropertyChanged(nameof(MostUsedCategoryNameText));
            OnPropertyChanged(nameof(MostUsedCategoryHoursText));
            OnPropertyChanged(nameof(LongestSessionNameText));
            OnPropertyChanged(nameof(LongestSessionHoursText));
            OnPropertyChanged(nameof(PeakProductivityTimeText));
            OnPropertyChanged(nameof(AverageSessionDurationText));
            OnPropertyChanged(nameof(TotalApplicationsUsed));
            OnPropertyChanged(nameof(TotalSessions));
        }

        private static string FormatHourRange(int hour)
        {
            var start = new DateTime(2000, 1, 1, hour, 0, 0);
            var end = start.AddHours(1);
            return $"{start:h tt} – {end:h tt}";
        }

        private static List<UsageTreeNode> BuildApplicationNodes(List<ActivityLogEntry> categoryEntries, TimeSpan categoryDuration, AppUsageViewMode mode)
        {
            var nodes = new List<UsageTreeNode>();

            foreach (var group in categoryEntries.GroupBy(e => e.ProcessName, StringComparer.OrdinalIgnoreCase)
                         .OrderByDescending(SumDuration))
            {
                var appEntries = group.ToList();
                var appDuration = SumDuration(appEntries);
                var appPercent = categoryDuration > TimeSpan.Zero
                    ? appDuration.TotalSeconds / categoryDuration.TotalSeconds * 100
                    : 0;

                nodes.Add(new UsageTreeNode(
                    UsageNodeKind.Application,
                    group.Key,
                    appDuration,
                    appPercent,
                    childrenFactory: () => BuildBelowApplicationNodes(appEntries, mode)));
            }

            return nodes;
        }

        // Custom ranges don't align to calendar weeks, so they get the same depth as
        // Week (Day → Session) rather than trying to group into partial weeks.
        private static List<UsageTreeNode> BuildBelowApplicationNodes(List<ActivityLogEntry> appEntries, AppUsageViewMode mode) => mode switch
        {
            AppUsageViewMode.Today => BuildSessionNodes(appEntries),
            AppUsageViewMode.Week => BuildDayNodes(appEntries),
            AppUsageViewMode.Month => BuildWeekNodes(appEntries),
            AppUsageViewMode.Custom => BuildDayNodes(appEntries),
            _ => new List<UsageTreeNode>()
        };

        private static List<UsageTreeNode> BuildWeekNodes(List<ActivityLogEntry> entries)
        {
            var nodes = new List<UsageTreeNode>();

            foreach (var group in entries.GroupBy(e => WorkWeekHelper.GetWeekStartMonday(e.StartTime.Date)).OrderBy(g => g.Key))
            {
                var weekEntries = group.ToList();
                nodes.Add(new UsageTreeNode(
                    UsageNodeKind.Week,
                    $"Week of {group.Key:MMM d}",
                    SumDuration(weekEntries),
                    childrenFactory: () => BuildDayNodes(weekEntries)));
            }

            return nodes;
        }

        private static List<UsageTreeNode> BuildDayNodes(List<ActivityLogEntry> entries)
        {
            var nodes = new List<UsageTreeNode>();

            foreach (var group in entries.GroupBy(e => e.StartTime.Date).OrderBy(g => g.Key))
            {
                var dayEntries = group.ToList();
                nodes.Add(new UsageTreeNode(
                    UsageNodeKind.Day,
                    group.Key.ToString("ddd, MMM d"),
                    SumDuration(dayEntries),
                    childrenFactory: () => BuildSessionNodes(dayEntries)));
            }

            return nodes;
        }

        // Groups only truly contiguous consecutive same-process rows into one Session node,
        // via the same MergeIntoSessionBlocks used by ComputeInsights, so a session here means
        // the same thing it does there. A different process in between, or any real time gap,
        // still ends the current group and starts a new one — no gap-tolerance threshold, so
        // re-focusing the same app later shows as a separate session rather than being merged
        // across an interruption. Each row shows just the app name (not window title, which
        // can change several times within one merged session), the earliest start time, the
        // latest end time, and the total duration. Chronological order is preserved
        // throughout.
        private static List<UsageTreeNode> BuildSessionNodes(List<ActivityLogEntry> entries) =>
            MergeIntoSessionBlocks(entries.OrderBy(e => e.StartTime).ToList())
                .Select(b => new UsageTreeNode(
                    UsageNodeKind.Session,
                    b.ProcessName,
                    b.End - b.Start,
                    sessionStart: b.Start,
                    sessionEnd: b.End))
                .ToList();

        private static TimeSpan SumDuration(IEnumerable<ActivityLogEntry> entries) =>
            TimeSpan.FromTicks(entries.Sum(e => (e.EndTime - e.StartTime).Ticks));

        private List<ActivityLogEntry> ReadDayEntries(DateTime date) =>
            _activityLogReader.TryReadDay(date.Date, out var entries) ? entries.ToList() : new List<ActivityLogEntry>();

        private List<ActivityLogEntry> ReadRangeEntries(DateTime start, DateTime end, DateTime today)
        {
            var cappedEnd = end.Date > today.Date ? today.Date : end.Date;
            if (cappedEnd < start.Date)
            {
                return new List<ActivityLogEntry>();
            }

            return _activityLogReader.ReadRange(start.Date, cappedEnd).ToList();
        }

        private static string FormatHm(TimeSpan span)
        {
            int h = (int)span.TotalHours;
            int m = span.Minutes;
            return $"{h}h {m}m";
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
