using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using LiveCharts;
using LiveCharts.Wpf;
using SystemActivityTracker.Models;
using SystemActivityTracker.Services;
using SystemActivityTracker.Services.Abstractions;
using SystemActivityTracker.Utilities;

namespace SystemActivityTracker.ViewModels
{
    // Base class for calendar items
    public abstract class CalendarDayItemBase
    {
        public abstract bool IsWeeklySummary { get; }
        public abstract bool IsCurrentMonth { get; set; }
        public abstract bool IsWeekend { get; }
        public abstract bool HasData { get; }
        public abstract DateTime Date { get; set; }
        public abstract int WeekNumber { get; set; }
        public virtual bool HasFullDayLeave => false;
        public virtual bool HasMorningHalfLeave => false;
        public virtual bool HasAfternoonHalfLeave => false;
        public virtual bool IsToday => false;
    }

    // Monthly Calendar Data Model
    public class MonthlyDayItem : CalendarDayItemBase, INotifyPropertyChanged, HoursCalculationHelper.MonthlyDayItemLike
    {
        private int _weekNumber;
        private bool _hasManualTasks;
        private bool _isHoliday;

        public override DateTime Date { get; set; }
        public TimeSpan TotalActive { get; set; }
        public TimeSpan TotalIdle { get; set; }
        public TimeSpan TotalLocked { get; set; }
        public TimeSpan ManualTime { get; set; }
        public override bool IsCurrentMonth { get; set; }
        public override bool IsWeekend => Date.DayOfWeek == DayOfWeek.Saturday || Date.DayOfWeek == DayOfWeek.Sunday;
        public override bool HasData => TotalActive > TimeSpan.Zero || TotalIdle > TimeSpan.Zero || TotalLocked > TimeSpan.Zero || ManualTime > TimeSpan.Zero;
        public override int WeekNumber { get => _weekNumber; set => _weekNumber = value; }
        public override bool IsWeeklySummary => false;
        public override bool IsToday => Date.Date == DateTime.Today;
        public ActivityChartViewModel? ChartViewModel { get; set; }
        public HorizontalActivityBarViewModel? HorizontalBarViewModel { get; set; }
        public bool IsFuture { get; set; }
        public bool HasChart { get; set; }
        public bool HasManualTasks
        {
            get => _hasManualTasks;
            set
            {
                if (_hasManualTasks == value)
                {
                    return;
                }

                _hasManualTasks = value;
                OnPropertyChanged();
            }
        }
        public LeaveDuration? LeaveDuration { get; set; }
        public LeaveType? LeaveType { get; set; }
        public bool HasLeave => LeaveDuration.HasValue;
        public override bool HasFullDayLeave => LeaveDuration == Models.LeaveDuration.FullDay;
        public override bool HasMorningHalfLeave => LeaveDuration == Models.LeaveDuration.MorningHalf;
        public override bool HasAfternoonHalfLeave => LeaveDuration == Models.LeaveDuration.AfternoonHalf;

        public bool IsHoliday
        {
            get => _isHoliday;
            set
            {
                if (_isHoliday == value)
                {
                    return;
                }

                _isHoliday = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowActivityBar));
            }
        }

        // Progress bar is hidden by default for non-current-month tiles and for
        // weekends/leave/holidays — unless the day actually has recorded activity, in
        // which case it shows regardless. Purely a display rule; none of the underlying
        // hour calculations are affected.
        public bool ShowActivityBar
        {
            get
            {
                if (!IsCurrentMonth || IsFuture || !HasChart)
                {
                    return false;
                }

                bool isSpecialDay = IsWeekend || HasLeave || IsHoliday;
                return !isSpecialDay || HasData;
            }
        }

        // GIF badge key for the day's Total Active hours (tracked + manual — the same
        // total already shown as "Total Active" in this tile). Outside these ranges, no
        // GIF is shown. Purely a display rule; does not affect any hour calculation.
        //   [6h, 8h)  -> TowardsTarget
        //   [8h, 10h) -> ReachedTarget
        //   [10h,12h] -> OverBurned
        public string? ActiveHoursGifKey
        {
            get
            {
                if (!IsCurrentMonth || IsFuture)
                {
                    return null;
                }

                var hours = (TotalActive + ManualTime).TotalHours;
                if (hours >= 6 && hours < 8) return "TowardsTarget";
                if (hours >= 8 && hours < 10) return "ReachedTarget";
                if (hours >= 10 && hours <= 12) return "OverBurned";
                return null;
            }
        }

        // Leave credit derived from the already-applied LeaveDuration (Mon–Fri only).
        public TimeSpan LeaveCredit =>
            ExpectedHoursCalculator.GetDayLeaveCredit(Date, LeaveDuration);

        DateTime HoursCalculationHelper.MonthlyDayItemLike.Date => Date;
        bool HoursCalculationHelper.MonthlyDayItemLike.IsCurrentMonth => IsCurrentMonth;
        TimeSpan HoursCalculationHelper.MonthlyDayItemLike.TrackedActive => TotalActive;
        TimeSpan HoursCalculationHelper.MonthlyDayItemLike.Manual => ManualTime;
        TimeSpan HoursCalculationHelper.MonthlyDayItemLike.LeaveCredit => LeaveCredit;
        bool HoursCalculationHelper.MonthlyDayItemLike.IsHoliday => IsHoliday;

        public void ApplyActivityData(TimeSpan active, TimeSpan idle, TimeSpan locked, TimeSpan manual, bool hasManualTasks)
        {
            TotalActive = active;
            TotalIdle = idle;
            TotalLocked = locked;
            ManualTime = manual;
            HasManualTasks = hasManualTasks;

            ChartViewModel?.SetData(active, manual, idle, locked);
            HorizontalBarViewModel?.SetData(active, manual, idle, locked);

            OnPropertyChanged(nameof(HasData));
            OnPropertyChanged(nameof(ShowActivityBar));
            OnPropertyChanged(nameof(ActiveHoursGifKey));
        }

        public string LeaveBadgeText => LeaveDuration switch
        {
            Models.LeaveDuration.FullDay => "Leave",
            Models.LeaveDuration.MorningHalf => "½ AM",
            Models.LeaveDuration.AfternoonHalf => "½ PM",
            _ => string.Empty
        };

        // Label for the full-width top-strip leave indicator (more room than the old corner
        // chip, so it spells out "HALF DAY" instead of the compact "½ AM"/"½ PM").
        public string LeaveBadgeStripText => LeaveDuration switch
        {
            Models.LeaveDuration.FullDay => "● LEAVE",
            Models.LeaveDuration.MorningHalf => "● HALF DAY · AM",
            Models.LeaveDuration.AfternoonHalf => "● HALF DAY · PM",
            _ => string.Empty
        };

        public void ApplyLeaveData(LeaveDuration? duration, LeaveType? type)
        {
            LeaveDuration = duration;
            LeaveType = type;
            OnPropertyChanged(nameof(HasLeave));
            OnPropertyChanged(nameof(HasFullDayLeave));
            OnPropertyChanged(nameof(HasMorningHalfLeave));
            OnPropertyChanged(nameof(HasAfternoonHalfLeave));
            OnPropertyChanged(nameof(LeaveBadgeText));
            OnPropertyChanged(nameof(LeaveBadgeStripText));
            OnPropertyChanged(nameof(ShowActivityBar));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class LeaveCalendarDayItem
    {
        public DateTime Date { get; set; }
        public bool IsCurrentMonth { get; set; }
        public bool HasLeave { get; set; }
        public LeaveDuration? LeaveDuration { get; set; }
        public LeaveType? LeaveType { get; set; }
        public string LeaveSummaryText { get; set; } = string.Empty;
    }

    public sealed class LeaveChoiceItem<T>
    {
        public LeaveChoiceItem(T value, string label)
        {
            Value = value;
            Label = label;
        }

        public T Value { get; }
        public string Label { get; }
    }

    // Weekly Summary Data Model for Calendar Grid — the "Summary" column cell shown once per
    // week row. BurntHours = actual worked time (tracked + manual) plus that day's leave
    // credit (8h full-day / 4h half-day, up to today), so a leave day still counts its
    // credited hours and stacks with any hours actually worked that same day — except a
    // public holiday date, which excludes the whole day (tracked, manual, and leave all
    // zeroed), since a holiday is never an expected work day. ExpectedHours follows the same
    // holiday-excluded rule — see HoursCalculationHelper.SumBurntHoursForWeek /
    // GetExpectedHoursForWeek, the single reusable source both this and the month-header
    // figures are built from.
    public class WeeklySummaryDayItem : CalendarDayItemBase, INotifyPropertyChanged
    {
        private int _weekNumber;
        private DateTime _date = DateTime.MinValue;
        private bool _isCurrentMonth = true;
        private TimeSpan _burntHours;
        private TimeSpan _expectedHours;

        public override int WeekNumber { get => _weekNumber; set => _weekNumber = value; }
        public TimeSpan BurntHours
        {
            get => _burntHours;
            set
            {
                if (_burntHours == value)
                {
                    return;
                }

                _burntHours = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BurntHoursText));
                OnPropertyChanged(nameof(HasData));
            }
        }
        public TimeSpan ExpectedHours
        {
            get => _expectedHours;
            set
            {
                if (_expectedHours == value)
                {
                    return;
                }

                _expectedHours = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ExpectedHoursText));
            }
        }
        // TEMP DIAGNOSTIC (BurntHoursText only): seconds appended to help pinpoint a reported
        // minute-level mismatch between Day/Week/Month/Week Report totals. Revert to
        // "{h}h {m}m" once confirmed. ExpectedHoursText uses the standard, permanent
        // "Xh Ym"/"Xh" Expected-hours format used everywhere else.
        public string BurntHoursText => $"{(int)BurntHours.TotalHours}h {BurntHours.Minutes}m {BurntHours.Seconds}s";
        public string ExpectedHoursText => ExpectedHours.ToExpectedHoursText();
        public override bool IsWeeklySummary => true;
        public override bool IsCurrentMonth { get => _isCurrentMonth; set => _isCurrentMonth = value; }
        public override bool IsWeekend => false;
        public override bool HasData => BurntHours > TimeSpan.Zero;
        public override DateTime Date { get => _date; set => _date = value; }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // Weekly Summary Data Model
    public class WeeklySummaryItem
    {
        public int WeekNumber { get; set; }
        public DateTime WeekStart { get; set; }
        public DateTime WeekEnd { get; set; }
        public TimeSpan TotalActiveHours { get; set; }
        public string TotalActiveText => $"{(int)TotalActiveHours.TotalHours}h {TotalActiveHours.Minutes}m";
    }

    // Timeline View Mode Enum
    public enum TimelineViewMode
    {
        Date,
        Week,
        Month
    }

    /// <summary>Tab order for UiAMainWindow TabControl (must match UiAMainWindow.xaml).</summary>
    public enum MainWindowTab
    {
        MonthlyUsage = 0,
        ApplicationUsage = 1,
        ManualTasks = 2,
        Leaves = 3,
        Holidays = 4,
        WeeklyOverview = 5,
        LastCrash = 6,
        Settings = 7
    }

    // Timeline Date Group Model
    public class TimelineDateGroup
    {
        public DateTime Date { get; set; }
        public TimeSpan TotalDuration { get; set; }
        public string TotalDurationText => $"{(int)TotalDuration.TotalHours}h {TotalDuration.Minutes}m";
        public ObservableCollection<ManualTaskEntry> Tasks { get; set; } = new ObservableCollection<ManualTaskEntry>();
    }

    public class MainWindowViewModel : INotifyPropertyChanged
    {
        private readonly TrackingService? _trackingService;
        private readonly SettingsService? _settingsService;
        private readonly IActivityLogReader _activityLogReader;
        private readonly ManualTaskService _manualTaskService;
        private readonly LeaveService _leaveService;
        private readonly HolidayService _holidayService;
        private string _trackingStatus = GetString("TrackingStatusStopped", "Tracking status: Stopped");
        private TimeSpan _totalActiveTimeToday;
        private TimeSpan _totalIdleTimeToday;
        private TimeSpan _totalLockedTimeToday;
        private readonly ObservableCollection<AppUsageSummary> _todayAppUsage = new ObservableCollection<AppUsageSummary>();
        private readonly ObservableCollection<ManualTaskEntry> _manualTasks = new ObservableCollection<ManualTaskEntry>();
        private ManualTaskEntry? _selectedManualTask;
        private bool _isManualEditMode;
        private string _manualTaskName = string.Empty;
        private string _manualHours = string.Empty;
        private string _manualMinutes = string.Empty;
        private string _manualSeconds = string.Empty;
        private int _selectedTabIndex = 0; // Default to Monthly Usage tab (will be repositioned to first)
        private int _idleThresholdMinutes;
        private int _pollIntervalSeconds;
        private bool _enableLiveRefresh;
        private int _liveRefreshIntervalSeconds;
        private bool _showFloatingTimerOnMinimize = true;
        private bool _isTestMode;
        private int _crashLogRetentionDays;
        private int _crashLogMaxSizeMB;
        private DateTime _selectedDate = DateTime.Today;
        private int _selectedYear = DateTime.Today.Year;
        private int _selectedMonth = DateTime.Today.Month;
        private DateTime _manualTasksDate = DateTime.Today;
        private DateTime _weekStartDate;
        private DateTime? _weekPickerDate;
        private bool _isSyncingWeekPicker;
        
        // Timeline-related fields
        private TimelineViewMode _timelineViewMode = TimelineViewMode.Month;
        private readonly ObservableCollection<TimelineDateGroup> _timelineItems = new ObservableCollection<TimelineDateGroup>();
        private DateTime _timelineCurrentDate = DateTime.Today;
        private readonly ObservableCollection<DailySummary> _weeklySummaries = new ObservableCollection<DailySummary>();
        
        // Month/Year picker fields for Manual Tasks timeline
        private string _timelineSelectedMonth = DateTime.Today.ToString("MMMM");
        private int _timelineSelectedYear = DateTime.Today.Year;
        private int _leaveSelectedYear = DateTime.Today.Year;
        private int _leaveSelectedMonth = DateTime.Today.Month;
        private DateTime _leaveFormDate = DateTime.Today;
        private LeaveDuration _leaveFormDuration = LeaveDuration.FullDay;
        private LeaveType _leaveFormType = LeaveType.SickLeave;
        private bool _isLeaveEditMode;
        private LeaveEntry? _selectedLeaveEntry;
        private readonly ObservableCollection<LeaveCalendarDayItem> _leaveCalendarDays = new ObservableCollection<LeaveCalendarDayItem>();
        private List<LeaveEntry> _leaveMonthEntries = new List<LeaveEntry>();
        private readonly List<string> _months = new List<string> 
        { 
            "January", "February", "March", "April", "May", "June",
            "July", "August", "September", "October", "November", "December" 
        };
        private readonly List<int> _years;
        private readonly ObservableCollection<MonthlyAppUsageDto> _monthlyAppUsage = new ObservableCollection<MonthlyAppUsageDto>();
        private readonly ObservableCollection<CalendarDayItemBase> _monthlyCalendarDays = new ObservableCollection<CalendarDayItemBase>();
        private readonly ObservableCollection<WeeklySummaryItem> _monthlyWeeklySummaries = new ObservableCollection<WeeklySummaryItem>();
        private bool _isMonthlyUsageEmpty = true;
        private TimeSpan _weeklyTrackedActiveDuration;
        private readonly ActivityChartViewModel _activityChartViewModel = new ActivityChartViewModel
        {
            ScaleFallbackReference = TimeSpan.FromHours(ExpectedHoursCalculator.StandardDayHours)
        };
        private readonly ActivityChartViewModel _weeklyActivityChartViewModel = new ActivityChartViewModel 
        { 
            ReferenceTime = TimeSpan.FromHours(ExpectedHoursCalculator.StandardWeekHours),
            ScaleFallbackReference = TimeSpan.FromHours(ExpectedHoursCalculator.StandardWeekHours)
        };
        private readonly ActivityChartViewModel _monthlyActivityChartViewModel = new ActivityChartViewModel 
        { 
            ReferenceTime = TimeSpan.FromHours(ExpectedHoursCalculator.StandardDayHours),
            ShowTotalActivityOnly = true // Show only Active bar in monthly view
        };
        private TimeSpan _weeklyManualDuration;
        private TimeSpan _weeklyTotalActiveDuration;
        private TimeSpan _weeklyTotalIdleDuration;
        private TimeSpan _weeklyTotalLockedDuration;
        private DateTime? _selectedDayStartTime;
        private DateTime? _selectedDayEndTime;
        private TimeSpan _selectedDayManualDuration;
        private TimeSpan _selectedDayExpectedHours = TimeSpan.FromHours(ExpectedHoursCalculator.StandardDayHours);
        private TimeSpan _selectedDayLeaveCredit = TimeSpan.Zero;
        private string _selectedDayLeaveSummaryText = string.Empty;
        private bool _hasSelectedDayLeave;
        private TimeSpan _weekExpectedHours = TimeSpan.FromHours(ExpectedHoursCalculator.StandardWeekHours);
        private TimeSpan _weekLeaveCredit = TimeSpan.Zero;
        private TimeSpan _weekHolidayCredit = TimeSpan.Zero;
        private string _weekLeaveSummaryText = string.Empty;
        private bool _hasWeekLeave;
        private TimeSpan _weeklyLeaveDuration;
        private TimeSpan _monthLeaveCredit = TimeSpan.Zero;
        private TimeSpan _monthHolidayCredit = TimeSpan.Zero;
        private int _monthHolidayDayCount;
        private TimeSpan _monthFullHolidayCredit = TimeSpan.Zero;
        private TimeSpan _monthHolidayWorkedCredit = TimeSpan.Zero;
        private DateTime? _runStartUtc;
        private TimeSpan _accumulatedRunTime = TimeSpan.Zero;
        private readonly DispatcherTimer _runningTimer = new DispatcherTimer();
        private int _lastDisplayedRunSecond = -1;
        private string _headerRunningTimerText = "00:00:00";
        private string _headerTimerHours = "00";
        private string _headerTimerMinutes = "00";
        private string _headerTimerSeconds = "00";
        private bool _isHeaderTimerLive;
        private TimeSpan _headerActiveBase = TimeSpan.Zero;
        // Manual/offline portion of _headerActiveBase, tracked separately so the floating
        // timer's quick-info menu can show Active and Offline as two distinct live figures
        // instead of only their combined total (_headerActiveBase = tracked + manual, seeded
        // in ResetHeaderActiveTickerForNewRun/SyncHeaderActiveBaseFromSummary alongside it).
        // Manual time doesn't tick live on its own (it's discrete logged entries, not a
        // running clock), so this needs no separate live-delta handling.
        private TimeSpan _headerManualBase = TimeSpan.Zero;
        // Today's leave credit (0 unless a leave entry exists for today), cached rather
        // than re-read from disk on every RefreshHeaderActiveTimer tick — kept in sync at
        // the same points _headerManualBase is (day sync/reset) plus RefreshLeaveSurfacesForDate
        // when the changed date is today. Deliberately does NOT include holiday credit —
        // "Today Total" must exclude holiday hours per its definition.
        private TimeSpan _headerLeaveCreditToday = TimeSpan.Zero;
        private DateTime? _headerActiveStartLocal;
        private DateTime? _headerActiveLastRecordStartLocal;
        private int _lastDisplayedActiveSecond = -1;
        private string _headerActiveTimerText = "00:00:00";
        private string _todayQuickTotalText = "0h";
        private string _todayQuickActiveText = "0h";
        private string _todayQuickOfflineText = "0h";
        private string _todayQuickStatusText = string.Empty;
        private readonly DispatcherTimer _autoRefreshTimer = new DispatcherTimer();
        private AppSettings _settingsSnapshot = new AppSettings();

        public LastCrashViewModel LastCrash { get; }
        public HolidaysViewModel Holidays { get; }
        public WorkSummaryViewModel WorkSummary { get; }
        public AppCategoriesViewModel AppCategories { get; }
        public AppUsageBreakdownViewModel AppUsageBreakdown { get; }

        public MainWindowViewModel(
            TrackingService? trackingService,
            SettingsService? settingsService = null,
            IActivityLogReader? activityLogReader = null,
            ManualTaskService? manualTaskService = null,
            LeaveService? leaveService = null,
            LastCrashViewModel? lastCrashViewModel = null,
            HolidaysViewModel? holidaysViewModel = null,
            WorkSummaryViewModel? workSummaryViewModel = null,
            AppCategoriesViewModel? appCategoriesViewModel = null,
            AppUsageBreakdownViewModel? appUsageBreakdownViewModel = null,
            HolidayService? holidayService = null)
        {
            _trackingService = trackingService;
            _settingsService = settingsService;
            _activityLogReader = activityLogReader ?? new ActivityLogReader();
            _manualTaskService = manualTaskService ?? new ManualTaskService();
            _leaveService = leaveService ?? new LeaveService();
            _holidayService = holidayService ?? new HolidayService();

            LastCrash = lastCrashViewModel ?? new LastCrashViewModel();
            Holidays = holidaysViewModel ?? new HolidaysViewModel();
            WorkSummary = workSummaryViewModel ?? new WorkSummaryViewModel(_activityLogReader, _manualTaskService, _leaveService);
            AppCategories = appCategoriesViewModel ?? new AppCategoriesViewModel(activityLogReader: _activityLogReader);
            AppUsageBreakdown = appUsageBreakdownViewModel ?? new AppUsageBreakdownViewModel(_activityLogReader);
            AppCategories.CategoriesSaved += (_, _) => AppUsageBreakdown.Refresh();
            // Without this, adding/editing/deleting a holiday on the Holidays tab left the
            // Month View calendar (each day's IsHoliday flag), Monthly Usage summary
            // (Expected Hours' holiday deduction, via _monthHolidayCredit), and the Week
            // view's Expected Hours (via _weekHolidayCredit) showing stale data until some
            // unrelated action (switching months/weeks, app restart) happened to reload them.
            Holidays.HolidaysChanged += (_, _) =>
            {
                LoadMonthlyUsage();
                RefreshWeekExpectedHours();
            };
            TodayText = DateTime.Now.ToString("dddd, dd MMMM yyyy");
            _weekStartDate = WorkWeekHelper.GetWeekStartMonday(DateTime.Today);
            _weekPickerDate = DateTime.Today;

            // Initialize years list for Manual Tasks timeline (current year ± 5)
            var currentYear = DateTime.Today.Year;
            _years = Enumerable.Range(currentYear - 5, 11).ToList();

            _runningTimer.Interval = TimeSpan.FromMilliseconds(100);
            _runningTimer.Tick += (_, __) =>
            {
                RefreshHeaderActiveTimer();
            };
            StartCommand = new RelayCommand(_ => StartTracking());

            StopCommand = new RelayCommand(_ => StopTracking());

            RefreshCommand = new RelayCommand(_ => RefreshForSelectedDate());

            PrimaryManualTaskCommand = new RelayCommand(_ => PrimaryManualTaskAction(), _ => CanEditManualTasks());
            BeginEditManualTaskCommand = new RelayCommand(p => BeginEditManualTask(p as ManualTaskEntry), p => CanEditManualTasks() && !_isManualEditMode);
            CancelManualTaskEditCommand = new RelayCommand(_ => CancelManualTaskEdit(), _ => CanEditManualTasks() && _isManualEditMode);
            DeleteManualTaskRowCommand = new RelayCommand(p => DeleteManualTaskRow(p as ManualTaskEntry), p => CanEditManualTasks() && !_isManualEditMode);

            // Timeline commands
            RefreshTimelineCommand = new RelayCommand(_ => RefreshTimeline());
            NavigatePreviousCommand = new RelayCommand(_ => NavigatePrevious());
            NavigateNextCommand = new RelayCommand(_ => NavigateNext());
            NavigateToManualTasksFromCalendarCommand = new RelayCommand(p => NavigateToManualTasksFromCalendar(p as CalendarDayItemBase));

            SaveLeaveCommand = new RelayCommand(_ => SaveLeaveAction(), _ => CanEditLeave());
            DeleteLeaveCommand = new RelayCommand(_ => DeleteLeaveAction(), _ => CanEditLeave() && _selectedLeaveEntry != null);
            CancelLeaveEditCommand = new RelayCommand(_ => CancelLeaveEdit(), _ => _isLeaveEditMode);
            SelectLeaveCalendarDayCommand = new RelayCommand(p => SelectLeaveCalendarDay(p as LeaveCalendarDayItem));
            NavigateLeavePreviousMonthCommand = new RelayCommand(_ => NavigateLeaveMonth(-1));
            NavigateLeaveNextMonthCommand = new RelayCommand(_ => NavigateLeaveMonth(1));

            SaveSettingsCommand = new RelayCommand(_ => SaveSettings());
            ClearCrashLogsCommand = new RelayCommand(_ => ClearCrashLogs());
            LoadWeeklyCommand = new RelayCommand(_ => LoadWeeklySummary());
            PreviousWeekCommand = new RelayCommand(_ => SelectedWeekStart = SelectedWeekStart.AddDays(-7));
            NextWeekCommand = new RelayCommand(_ => SelectedWeekStart = SelectedWeekStart.AddDays(7));
            CurrentWeekCommand = new RelayCommand(_ => WeekPickerDate = DateTime.Today, _ => !IsCurrentWeekSelected);
            ForceWriteNowCommand = new RelayCommand(_ =>
            {
                _trackingService?.FlushCurrentRecord();
                RefreshSelectedDaySummary();
                RefreshSelectedDayAppUsage();
                RefreshWeekIfNeeded(SelectedDate.Date);
            });

            _autoRefreshTimer.Tick += (_, __) =>
            {
                if (_trackingService != null && _trackingService.IsRunning && SelectedDate.Date == DateTime.Today)
                {
                    RefreshCommand.Execute(null);
                }
            };

            if (_trackingService != null)
            {
                _trackingService.DayRolledOver += (_, newDate) =>
                {
                    var dispatcher = System.Windows.Application.Current?.Dispatcher;
                    if (dispatcher == null)
                    {
                        return;
                    }

                    dispatcher.BeginInvoke(new Action(() =>
                    {
                        var previousToday = newDate.Date.AddDays(-1);
                        if (SelectedDate.Date == previousToday)
                        {
                            SelectedDate = newDate.Date;
                        }

                        RefreshCommand.Execute(null);
                    }));
                };
            }

            // Load settings for UI
            var settings = _settingsService?.Load() ?? new AppSettings();
            if (settings.IdleThresholdMinutes <= 0) settings.IdleThresholdMinutes = 5;
            if (settings.PollIntervalSeconds <= 0) settings.PollIntervalSeconds = 5;
            if (settings.LiveRefreshIntervalSeconds <= 0) settings.LiveRefreshIntervalSeconds = 30;

            // Crash log policy clamps
            if (settings.CrashLogRetentionDays < 1) settings.CrashLogRetentionDays = 14;
            if (settings.CrashLogMaxSizeMB < 1) settings.CrashLogMaxSizeMB = 50;

            _settingsSnapshot = settings;

            IdleThresholdMinutes = settings.IdleThresholdMinutes;
            PollIntervalSeconds = settings.PollIntervalSeconds;
            EnableLiveRefresh = settings.EnableLiveRefresh;
            LiveRefreshIntervalSeconds = settings.LiveRefreshIntervalSeconds;
            CrashLogRetentionDays = settings.CrashLogRetentionDays;
            CrashLogMaxSizeMB = settings.CrashLogMaxSizeMB;
            _showFloatingTimerOnMinimize = settings.ShowFloatingTimerOnMinimize;

            if (settings.AutoStartTrackingOnLaunch)
            {
                StartTracking();
            }

            LoadWeeklySummary();

            LoadMonthlyUsage();

            RefreshForSelectedDate();

            SyncHeaderActiveBaseFromSummary();

            _manualTasks.CollectionChanged += OnManualTasksCollectionChanged;
            _timelineItems.CollectionChanged += OnTimelineItemsCollectionChanged;

            LoadManualTasksForSelectedDate();

            // Initialize timeline with default Month view
            RefreshTimeline();

            LoadLeavesForSelectedMonth();

            // Initialize activity chart with current data
            UpdateActivityChart();
        }

        private static string GetString(string key, string fallback)
        {
            try
            {
                if (System.Windows.Application.Current?.TryFindResource(key) is string value && !string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
            catch
            {
            }

            return fallback;
        }

        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set
            {
                if (_selectedTabIndex != value)
                {
                    _selectedTabIndex = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsDayDetailsMode));
                    UpdateManualTaskCommandsCanExecute();

                    if (IsDayDetailsMode)
                    {
                        LoadManualTasksForSelectedDate();
                    }
                    else
                    {
                        CancelManualTaskEdit();
                    }
                }
            }
        }

        public bool IsDayDetailsMode => SelectedTabIndex == 0;

        public bool IsManualEditMode
        {
            get => _isManualEditMode;
            private set
            {
                if (_isManualEditMode != value)
                {
                    _isManualEditMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PrimaryManualTaskGlyph));
                    OnPropertyChanged(nameof(ManualRowActionGlyph));
                    UpdateManualTaskCommandsCanExecute();
                }
            }
        }

        private void StartTracking()
        {
            if (_trackingService == null)
            {
                return;
            }

            if (_trackingService.IsRunning)
            {
                return;
            }

            _trackingService.Start();

            ResetHeaderActiveTickerForNewRun();

            TrackingStatus = GetString("TrackingStatusRunning", "Tracking status: Running");
            OnPropertyChanged(nameof(IsTrackingRunning));
            StartRunningTimerTicker();
            ApplyLiveRefreshSettings();
        }

        private void StopTracking()
        {
            if (_trackingService == null)
            {
                return;
            }

            if (!_trackingService.IsRunning)
            {
                TrackingStatus = GetString("TrackingStatusStopped", "Tracking status: Stopped");
                OnPropertyChanged(nameof(IsTrackingRunning));
                StopRunningTimerTicker();
                _autoRefreshTimer.Stop();
                return;
            }

            _trackingService.Stop();
            TrackingStatus = GetString("TrackingStatusStopped", "Tracking status: Stopped");
            OnPropertyChanged(nameof(IsTrackingRunning));
            StopRunningTimerTicker();
            ApplyLiveRefreshSettings();
        }

        private void ResetHeaderActiveTickerForNewRun()
        {
            // UI-only reset so Stop -> Start doesn't reuse a stale start timestamp from a previous run.
            // Keep the base as the already-recorded total active for today; the live delta starts fresh.
            var (trackedActive, manual) = ComputeHeaderTimerBaseComponents(DateTime.Today);
            _headerActiveBase = trackedActive + manual;
            _headerManualBase = manual;
            RefreshHeaderLeaveCreditToday();

            _headerActiveStartLocal = DateTime.Now;
            _headerActiveLastRecordStartLocal = null;

            _lastDisplayedActiveSecond = -1;
            RefreshHeaderActiveTimer();
        }

        public string HeaderRunningTimerText
        {
            get => _headerRunningTimerText;
            private set
            {
                if (!string.Equals(_headerRunningTimerText, value, StringComparison.Ordinal))
                {
                    _headerRunningTimerText = value;
                    OnPropertyChanged();
                }
            }
        }

        public string HeaderTimerHours
        {
            get => _headerTimerHours;
            private set
            {
                if (!string.Equals(_headerTimerHours, value, StringComparison.Ordinal))
                {
                    _headerTimerHours = value;
                    OnPropertyChanged();
                }
            }
        }

        public string HeaderTimerMinutes
        {
            get => _headerTimerMinutes;
            private set
            {
                if (!string.Equals(_headerTimerMinutes, value, StringComparison.Ordinal))
                {
                    _headerTimerMinutes = value;
                    OnPropertyChanged();
                }
            }
        }

        public string HeaderTimerSeconds
        {
            get => _headerTimerSeconds;
            private set
            {
                if (!string.Equals(_headerTimerSeconds, value, StringComparison.Ordinal))
                {
                    _headerTimerSeconds = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsHeaderTimerLive
        {
            get => _isHeaderTimerLive;
            private set
            {
                if (_isHeaderTimerLive != value)
                {
                    _isHeaderTimerLive = value;
                    OnPropertyChanged();
                }
            }
        }

        public string HeaderActiveTimerText
        {
            get => _headerActiveTimerText;
            private set
            {
                if (!string.Equals(_headerActiveTimerText, value, StringComparison.Ordinal))
                {
                    _headerActiveTimerText = value;
                    OnPropertyChanged();
                }
            }
        }

        // Today's live Today Total/Active/Offline/Status, shown in both the floating
        // timer's and the tray icon's quick-info menus (same labels/values in both — see
        // FloatingTimerWindow.xaml and TrayIconService). Always DateTime.Today, deliberately
        // NOT tied to SelectedDate (which the user may have navigated away from in the Day
        // view) — updated alongside the header timer in RefreshHeaderActiveTimer, at the
        // same once-per-second cadence, using the exact same tracked-active/manual sources
        // as Total Active/Burnt Hours elsewhere (HoursCalculationHelper.SumActiveOnly /
        // GetManualSecondsForDate) so nothing here can drift from those.
        //
        // Today Total = Active + Offline Work + Leave, excluding holiday hours (holiday
        // credit is never added to any of these — it only ever reduces Expected Hours
        // elsewhere in the app, never worked/credited time). Full TimeSpan-tick precision
        // is kept throughout; only the final display text truncates to whole minutes (no
        // intermediate rounding).
        public string TodayQuickTotalText
        {
            get => _todayQuickTotalText;
            private set
            {
                if (!string.Equals(_todayQuickTotalText, value, StringComparison.Ordinal))
                {
                    _todayQuickTotalText = value;
                    OnPropertyChanged();
                }
            }
        }

        // Tracked active time only (no manual, no leave).
        public string TodayQuickActiveText
        {
            get => _todayQuickActiveText;
            private set
            {
                if (!string.Equals(_todayQuickActiveText, value, StringComparison.Ordinal))
                {
                    _todayQuickActiveText = value;
                    OnPropertyChanged();
                }
            }
        }

        // Manual/offline work only (no tracked active, no leave).
        public string TodayQuickOfflineText
        {
            get => _todayQuickOfflineText;
            private set
            {
                if (!string.Equals(_todayQuickOfflineText, value, StringComparison.Ordinal))
                {
                    _todayQuickOfflineText = value;
                    OnPropertyChanged();
                }
            }
        }

        // Exactly one of "Remaining: Xh Ym" (before target) / "Achieved" (target reached) /
        // "Overtime: Xh Ym" (past target), comparing Today Total (Active + Offline + Leave)
        // against ExpectedHoursCalculator.GetDayExpectedHours — the same 8h-on-weekdays rule
        // used everywhere else, with no holiday-hours involvement.
        public string TodayQuickStatusText
        {
            get => _todayQuickStatusText;
            private set
            {
                if (!string.Equals(_todayQuickStatusText, value, StringComparison.Ordinal))
                {
                    _todayQuickStatusText = value;
                    OnPropertyChanged();
                }
            }
        }

        private void StartRunningTimerTicker()
        {
            if (_runStartUtc.HasValue)
            {
                return;
            }

            _runStartUtc = DateTime.UtcNow;
            _lastDisplayedRunSecond = -1;
            RefreshHeaderActiveTimer();
            _runningTimer.Start();
        }

        private void StopRunningTimerTicker()
        {
            if (_runStartUtc.HasValue)
            {
                _accumulatedRunTime += DateTime.UtcNow - _runStartUtc.Value;
                _runStartUtc = null;
            }

            _runningTimer.Stop();
            RefreshHeaderActiveTimer();
        }

        private void RefreshHeaderActiveTimer()
        {
            DateTime now = DateTime.Now;

            TrackingService.TrackingSnapshot snapshot = default;
            bool hasSnapshot = _trackingService != null && _trackingService.TryGetSnapshot(out snapshot);
            bool isActive = hasSnapshot && !snapshot.IsLocked && !snapshot.IsIdle;

            if (isActive)
            {
                if (_headerActiveStartLocal == null)
                {
                    var start = snapshot.CurrentRecordStartTime ?? now;
                    if (start > now)
                    {
                        start = now;
                    }

                    _headerActiveStartLocal = start;
                    _headerActiveLastRecordStartLocal = snapshot.CurrentRecordStartTime;
                }
                else if (snapshot.CurrentRecordStartTime.HasValue && _headerActiveLastRecordStartLocal.HasValue && snapshot.CurrentRecordStartTime.Value != _headerActiveLastRecordStartLocal.Value)
                {
                    var delta = now - _headerActiveStartLocal.Value;
                    if (delta < TimeSpan.Zero)
                    {
                        delta = TimeSpan.Zero;
                    }

                    _headerActiveBase += delta;

                    var start = snapshot.CurrentRecordStartTime.Value;
                    if (start > now)
                    {
                        start = now;
                    }

                    _headerActiveStartLocal = start;
                    _headerActiveLastRecordStartLocal = snapshot.CurrentRecordStartTime.Value;
                }
            }
            else if (_headerActiveStartLocal.HasValue)
            {
                var delta = now - _headerActiveStartLocal.Value;
                if (delta < TimeSpan.Zero)
                {
                    delta = TimeSpan.Zero;
                }

                _headerActiveBase += delta;
                _headerActiveStartLocal = null;
                _headerActiveLastRecordStartLocal = null;
            }

            var total = _headerActiveBase;
            if (_headerActiveStartLocal.HasValue)
            {
                var delta = now - _headerActiveStartLocal.Value;
                if (delta < TimeSpan.Zero)
                {
                    delta = TimeSpan.Zero;
                }

                total += delta;
            }

            if (total < TimeSpan.Zero)
            {
                total = TimeSpan.Zero;
            }

            IsHeaderTimerLive = isActive;

            int wholeSeconds = (int)total.TotalSeconds;
            if (wholeSeconds == _lastDisplayedActiveSecond)
            {
                return;
            }

            _lastDisplayedActiveSecond = wholeSeconds;
            _lastDisplayedRunSecond = wholeSeconds;

            int hours = (int)total.TotalHours;
            int minutes = total.Minutes;
            int seconds = total.Seconds;
            var formatted = string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", hours, minutes, seconds);

            HeaderRunningTimerText = formatted;
            HeaderActiveTimerText = formatted;
            HeaderTimerHours = string.Format(CultureInfo.InvariantCulture, "{0:00}", hours);
            HeaderTimerMinutes = string.Format(CultureInfo.InvariantCulture, "{0:00}", minutes);
            HeaderTimerSeconds = string.Format(CultureInfo.InvariantCulture, "{0:00}", seconds);

            // Active = total minus the (static, non-ticking) manual/offline portion.
            var offlineToday = _headerManualBase;
            var activeToday = total - offlineToday;
            if (activeToday < TimeSpan.Zero)
            {
                activeToday = TimeSpan.Zero;
            }

            // Today Total = Active + Offline Work + Leave, excluding holiday hours (holiday
            // credit never enters here — see _headerLeaveCreditToday's comment). Full
            // TimeSpan-tick precision throughout; only FormatQuickMenuHours's final display
            // truncates to whole minutes, with no intermediate rounding.
            var todayTotal = activeToday + offlineToday + _headerLeaveCreditToday;

            TodayQuickTotalText = FormatQuickMenuHours(todayTotal);
            TodayQuickActiveText = FormatQuickMenuHours(activeToday);
            TodayQuickOfflineText = FormatQuickMenuHours(offlineToday);

            var expectedToday = ExpectedHoursCalculator.GetDayExpectedHours(DateTime.Today);
            var statusDiff = todayTotal - expectedToday;
            if (statusDiff < TimeSpan.Zero)
            {
                TodayQuickStatusText = $"Remaining: {FormatQuickMenuHours(-statusDiff)}";
            }
            else if (statusDiff > TimeSpan.Zero)
            {
                TodayQuickStatusText = $"Overtime: {FormatQuickMenuHours(statusDiff)}";
            }
            else
            {
                TodayQuickStatusText = "Achieved";
            }
        }

        // Always "Xh Ym" — unlike ToExpectedHoursText (which omits minutes when 0, e.g. just
        // "8h"), the quick-info menus (floating timer + tray) always show both units so a
        // value is never just a bare, ambiguous-looking hour number like "0h".
        private static string FormatQuickMenuHours(TimeSpan span)
        {
            int hours = (int)span.TotalHours;
            int minutes = span.Minutes;
            return $"{hours}h {minutes}m";
        }

        private void SyncHeaderActiveBaseFromSummary()
        {
            var (trackedActive, manual) = ComputeHeaderTimerBaseComponents(DateTime.Today);
            _headerActiveBase = trackedActive + manual;
            _headerManualBase = manual;
            RefreshHeaderLeaveCreditToday();
            _headerActiveStartLocal = null;
            _headerActiveLastRecordStartLocal = null;
            _lastDisplayedActiveSecond = -1;
            RefreshHeaderActiveTimer();
        }

        // Caches today's leave credit for the "Today Total" quick-menu figure — read once
        // here (day sync/reset) and again from RefreshLeaveSurfacesForDate when today's own
        // leave entry changes, rather than on every RefreshHeaderActiveTimer tick (leave
        // entries are discrete/infrequent edits, not something that needs a per-second
        // disk read).
        private void RefreshHeaderLeaveCreditToday()
        {
            var today = DateTime.Today;
            var leaveEntry = _leaveService.GetForDate(today);
            _headerLeaveCreditToday = ExpectedHoursCalculator.GetDayLeaveCredit(today, leaveEntry?.Duration);
        }

        // Top header running timer's base = tracked active time + Offline Work/Manual time
        // for the date, so the ticking display reflects total worked time, not just tracked
        // activity. The live per-second delta added on top in RefreshHeaderActiveTimer is
        // still tracked-active-only (there's no "currently running" manual timer to tick),
        // but the base it starts from now correctly folds in already-logged manual entries.
        // Returns the two components separately (not just their sum) so callers can track
        // _headerManualBase alongside _headerActiveBase — see that field's comment.
        private (TimeSpan TrackedActive, TimeSpan Manual) ComputeHeaderTimerBaseComponents(DateTime date)
        {
            var trackedActive = _activityLogReader.TryReadDay(date.Date, out var entries)
                ? HoursCalculationHelper.SumActiveOnly(entries)
                : TimeSpan.Zero;

            var manual = TimeSpan.FromSeconds(GetManualSecondsForDate(date.Date));

            return (trackedActive, manual);
        }

        public string TodayText { get; }

        public string TrackingStatus
        {
            get => _trackingStatus;
            set
            {
                if (_trackingStatus != value)
                {
                    _trackingStatus = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsTrackingRunning));
                }
            }
        }

        public bool IsTrackingRunning => _trackingService != null && _trackingService.IsRunning;

        /// <summary>Selected month+year for Monthly Usage tab (first day of month). Single picker binding.</summary>
        public DateTime SelectedMonthYear
        {
            get => new DateTime(_selectedYear, _selectedMonth, 1);
            set
            {
                var year = value.Year;
                var month = value.Month;
                if (_selectedYear != year || _selectedMonth != month)
                {
                    _selectedYear = year;
                    _selectedMonth = month;
                    OnPropertyChanged();
                    ReloadMonth();
                }
            }
        }

        /// <summary>First day of selected month for internal use (e.g. GetManualSecondsForMonth).</summary>
        private DateTime SelectedMonthDateTime => new DateTime(_selectedYear, _selectedMonth, 1);

        public bool IsTestMode
        {
            get => _isTestMode;
            set
            {
                if (_isTestMode != value)
                {
                    _isTestMode = value;
                    OnPropertyChanged();
                }
            }
        }

        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand SaveSettingsCommand { get; }
        public ICommand ClearCrashLogsCommand { get; }
        public ICommand LoadWeeklyCommand { get; }
        public ICommand PreviousWeekCommand { get; }
        public ICommand NextWeekCommand { get; }
        public ICommand CurrentWeekCommand { get; }
        public ICommand ForceWriteNowCommand { get; }

        public ObservableCollection<AppUsageSummary> TodayAppUsage => _todayAppUsage;
        public ObservableCollection<DailySummary> WeeklySummaries => _weeklySummaries;
        public ObservableCollection<MonthlyAppUsageDto> MonthlyAppUsage => _monthlyAppUsage;
        public ObservableCollection<CalendarDayItemBase> MonthlyCalendarDays => _monthlyCalendarDays;
        public ObservableCollection<WeeklySummaryItem> MonthlyWeeklySummaries => _monthlyWeeklySummaries;

        private int _monthlyCalendarRowCount = 6;

        // Actual number of Monday–Sunday week rows in the selected month's calendar grid
        // (4, 5, or 6 depending on the month) — drives the calendar UniformGrid's Rows so
        // a 5-week month doesn't reserve a wasted, mostly-empty 6th row's worth of height.
        public int MonthlyCalendarRowCount
        {
            get => _monthlyCalendarRowCount;
            private set
            {
                if (_monthlyCalendarRowCount == value)
                {
                    return;
                }

                _monthlyCalendarRowCount = value;
                OnPropertyChanged(nameof(MonthlyCalendarRowCount));
            }
        }

        public bool IsMonthlyUsageEmpty
        {
            get => _isMonthlyUsageEmpty;
            private set
            {
                if (_isMonthlyUsageEmpty != value)
                {
                    _isMonthlyUsageEmpty = value;
                    OnPropertyChanged();
                }
            }
        }

        public DateTime SelectedDate
        {
            get => _selectedDate;
            set
            {
                var normalized = (value == default ? DateTime.Today : value).Date;
                if (_selectedDate != normalized)
                {
                    _selectedDate = normalized;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsSelectedDateInFuture));
                    ApplyLiveRefreshSettings();
                    RefreshForSelectedDate();
                }
            }
        }

        public bool ShowSelectedDayChart => !IsSelectedDateInFuture && (TotalActiveTimeToday + TotalIdleTimeToday + TotalLockedTimeToday + _selectedDayManualDuration) > TimeSpan.Zero;

        public bool IsSelectedDateInFuture => SelectedDate.Date > DateTime.Today;

        public ObservableCollection<ManualTaskEntry> ManualTasks => _manualTasks;

        public ManualTaskEntry? SelectedManualTask
        {
            get => _selectedManualTask;
            set
            {
                if (!ReferenceEquals(_selectedManualTask, value))
                {
                    _selectedManualTask = value;
                    OnPropertyChanged();
                    UpdateManualTaskCommandsCanExecute();

                    if (_selectedManualTask != null)
                    {
                        ManualTaskName = _selectedManualTask.TaskName;
                        var ts = TimeSpan.FromSeconds(_selectedManualTask.TotalSeconds < 0 ? 0 : _selectedManualTask.TotalSeconds);
                        ManualHours = ((int)ts.TotalHours).ToString(CultureInfo.InvariantCulture);
                        ManualMinutes = ts.Minutes.ToString(CultureInfo.InvariantCulture);
                        ManualSeconds = ts.Seconds.ToString(CultureInfo.InvariantCulture);
                    }
                }
            }
        }

        public string ManualTaskName
        {
            get => _manualTaskName;
            set
            {
                if (!string.Equals(_manualTaskName, value, StringComparison.Ordinal))
                {
                    _manualTaskName = value;
                    OnPropertyChanged();
                    UpdateManualTaskCommandsCanExecute();
                }
            }
        }

        public string ManualHours
        {
            get => _manualHours;
            set
            {
                if (!string.Equals(_manualHours, value, StringComparison.Ordinal))
                {
                    _manualHours = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ManualMinutes
        {
            get => _manualMinutes;
            set
            {
                if (!string.Equals(_manualMinutes, value, StringComparison.Ordinal))
                {
                    _manualMinutes = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ManualSeconds
        {
            get => _manualSeconds;
            set
            {
                if (!string.Equals(_manualSeconds, value, StringComparison.Ordinal))
                {
                    _manualSeconds = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ManualTotalText => FormatTimeSpan(TimeSpan.FromSeconds(_manualTasks.Sum(t => Math.Max(0, t.TotalSeconds))));
        public string GrandTotalText => FormatTimeSpan((TotalActiveTimeToday + TotalIdleTimeToday + TotalLockedTimeToday) + TimeSpan.FromSeconds(_manualTasks.Sum(t => Math.Max(0, t.TotalSeconds))));

        // Segoe MDL2 Assets glyphs
        public string PrimaryManualTaskGlyph => IsManualEditMode ? "\uE74E" : "\uE710"; // Save / Add
        public string ManualRowActionGlyph => IsManualEditMode ? "\uE711" : "\uE70F"; // Cancel / Edit
        public string ManualDeleteGlyph => "\uE74D"; // Delete

        public ICommand PrimaryManualTaskCommand { get; }
        public ICommand BeginEditManualTaskCommand { get; }
        public ICommand CancelManualTaskEditCommand { get; }
        public ICommand DeleteManualTaskRowCommand { get; }
        public ICommand NavigateToManualTasksFromCalendarCommand { get; }

        public ObservableCollection<LeaveCalendarDayItem> LeaveCalendarDays => _leaveCalendarDays;

        public DateTime LeaveSelectedMonthYear
        {
            get => new DateTime(_leaveSelectedYear, _leaveSelectedMonth, 1);
            set
            {
                var year = value.Year;
                var month = value.Month;
                if (_leaveSelectedYear != year || _leaveSelectedMonth != month)
                {
                    _leaveSelectedYear = year;
                    _leaveSelectedMonth = month;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(LeaveMonthLabel));
                    LoadLeavesForSelectedMonth();
                }
            }
        }

        public string LeaveMonthLabel => LeaveSelectedMonthYear.ToString("MMMM yyyy");

        public DateTime LeaveFormDate
        {
            get => _leaveFormDate;
            set
            {
                var normalized = (value == default ? DateTime.Today : value).Date;
                if (_leaveFormDate != normalized)
                {
                    _leaveFormDate = normalized;
                    OnPropertyChanged();
                }
            }
        }

        public LeaveDuration LeaveFormDuration
        {
            get => _leaveFormDuration;
            set
            {
                if (_leaveFormDuration != value)
                {
                    _leaveFormDuration = value;
                    OnPropertyChanged();
                }
            }
        }

        public LeaveType LeaveFormType
        {
            get => _leaveFormType;
            set
            {
                if (_leaveFormType != value)
                {
                    _leaveFormType = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsLeaveEditMode
        {
            get => _isLeaveEditMode;
            private set
            {
                if (_isLeaveEditMode != value)
                {
                    _isLeaveEditMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(LeaveFormPrimaryGlyph));
                    OnPropertyChanged(nameof(LeaveFormPrimaryTooltip));
                    UpdateLeaveCommandsCanExecute();
                }
            }
        }

        public string LeaveFormPrimaryGlyph => IsLeaveEditMode ? "\uE74E" : "\uE710";
        public string LeaveFormPrimaryTooltip => IsLeaveEditMode ? "Save" : "Add";

        public Array LeaveDurationOptions => Enum.GetValues(typeof(LeaveDuration));
        public Array LeaveTypeOptions => Enum.GetValues(typeof(LeaveType));

        public IReadOnlyList<LeaveChoiceItem<LeaveDuration>> LeaveDurationChoices { get; } = new[]
        {
            new LeaveChoiceItem<LeaveDuration>(LeaveDuration.FullDay, "Full day"),
            new LeaveChoiceItem<LeaveDuration>(LeaveDuration.MorningHalf, "Morning half day"),
            new LeaveChoiceItem<LeaveDuration>(LeaveDuration.AfternoonHalf, "Afternoon half day")
        };

        public IReadOnlyList<LeaveChoiceItem<LeaveType>> LeaveTypeChoices { get; } = new[]
        {
            new LeaveChoiceItem<LeaveType>(LeaveType.SickLeave, "Sick Leave"),
            new LeaveChoiceItem<LeaveType>(LeaveType.CasualLeave, "Casual Leave"),
            new LeaveChoiceItem<LeaveType>(LeaveType.EarnedLeave, "Earned Leave"),
            new LeaveChoiceItem<LeaveType>(LeaveType.CompOff, "Comp Off")
        };

        public ICommand SaveLeaveCommand { get; }
        public ICommand DeleteLeaveCommand { get; }
        public ICommand CancelLeaveEditCommand { get; }
        public ICommand SelectLeaveCalendarDayCommand { get; }
        public ICommand NavigateLeavePreviousMonthCommand { get; }
        public ICommand NavigateLeaveNextMonthCommand { get; }

        // Timeline public properties
        public TimelineViewMode TimelineViewMode
        {
            get => _timelineViewMode;
            set
            {
                if (_timelineViewMode != value)
                {
                    _timelineViewMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PeriodLabelFormatted));
                    OnPropertyChanged(nameof(ShowDateViewEmptyState));
                    OnPropertyChanged(nameof(ShowTimelineViewEmptyState));

                    if (_manualTasksDate != _timelineCurrentDate.Date)
                    {
                        SetManualTasksDateInternal(_timelineCurrentDate.Date, syncTimeline: false);
                    }

                    RefreshTimeline();
                }
            }
        }

        public ObservableCollection<TimelineDateGroup> TimelineItems => _timelineItems;

        public DateTime TimelineCurrentDate
        {
            get => _timelineCurrentDate;
            set => SetTimelineCurrentDateInternal(
                (value == default ? DateTime.Today : value).Date,
                syncManualTasksDate: true);
        }

        public bool ShowDateViewEmptyState =>
            TimelineViewMode == TimelineViewMode.Date && _manualTasks.Count == 0;

        public bool ShowTimelineViewEmptyState =>
            (TimelineViewMode == TimelineViewMode.Week || TimelineViewMode == TimelineViewMode.Month)
            && _timelineItems.Count == 0;

        public string PeriodLabelFormatted
        {
            get
            {
                return TimelineViewMode switch
                {
                    TimelineViewMode.Date => TimelineCurrentDate.ToString("MMMM d, yyyy"),
                    TimelineViewMode.Week => GetWeekLabel(TimelineCurrentDate),
                    TimelineViewMode.Month => TimelineCurrentDate.ToString("MMMM yyyy"),
                    _ => TimelineCurrentDate.ToString("MMMM d, yyyy")
                };
            }
        }

        // Month/Year picker properties for Manual Tasks timeline
        public List<string> Months => _months;
        public List<int> Years => _years;

        public string SelectedMonth
        {
            get => _timelineSelectedMonth;
            set
            {
                if (_timelineSelectedMonth != value)
                {
                    _timelineSelectedMonth = value;
                    OnPropertyChanged();
                    UpdateTimelineDateFromMonthYear();
                }
            }
        }

        public int SelectedYear
        {
            get => _timelineSelectedYear;
            set
            {
                if (_timelineSelectedYear != value)
                {
                    _timelineSelectedYear = value;
                    OnPropertyChanged();
                    UpdateTimelineDateFromMonthYear();
                }
            }
        }

        private void UpdateTimelineDateFromMonthYear()
        {
            var monthIndex = _months.IndexOf(_timelineSelectedMonth) + 1;
            if (monthIndex > 0 && monthIndex <= 12)
            {
                SetTimelineCurrentDateInternal(
                    new DateTime(_timelineSelectedYear, monthIndex, 1),
                    syncManualTasksDate: true);
            }
        }

        private void SetTimelineCurrentDateInternal(DateTime normalized, bool syncManualTasksDate)
        {
            if (_timelineCurrentDate == normalized)
            {
                return;
            }

            _timelineCurrentDate = normalized;
            OnPropertyChanged(nameof(TimelineCurrentDate));
            OnPropertyChanged(nameof(PeriodLabelFormatted));

            if (_timelineViewMode == TimelineViewMode.Month)
            {
                SyncMonthYearPickersFromTimelineDate(normalized);
            }

            if (syncManualTasksDate
                && (_timelineViewMode == TimelineViewMode.Date
                    || _timelineViewMode == TimelineViewMode.Week
                    || _timelineViewMode == TimelineViewMode.Month))
            {
                SetManualTasksDateInternal(normalized, syncTimeline: false);
            }

            RefreshTimeline();
        }

        private void SyncMonthYearPickersFromTimelineDate(DateTime date)
        {
            var monthName = _months[date.Month - 1];
            if (_timelineSelectedMonth != monthName)
            {
                _timelineSelectedMonth = monthName;
                OnPropertyChanged(nameof(SelectedMonth));
            }

            if (_timelineSelectedYear != date.Year)
            {
                _timelineSelectedYear = date.Year;
                OnPropertyChanged(nameof(SelectedYear));
            }
        }

        private void OnManualTasksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(ShowDateViewEmptyState));
        }

        private void OnTimelineItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(ShowTimelineViewEmptyState));
        }

        private string GetWeekLabel(DateTime date)
        {
            var startOfWeek = WorkWeekHelper.GetWeekStartMonday(date);
            var endOfWeek = startOfWeek.AddDays(6);
            return $"{startOfWeek:MMM d} - {endOfWeek:MMM d, yyyy}";
        }

        // Timeline commands
        public ICommand RefreshTimelineCommand { get; }
        public ICommand NavigatePreviousCommand { get; }
        public ICommand NavigateNextCommand { get; }

        private void RefreshForSelectedDate()
        {
            if (_trackingService != null && _trackingService.IsRunning && SelectedDate.Date == DateTime.Today)
            {
                _trackingService.FlushCurrentRecord();
            }

            RefreshTodaySummary();
            RefreshSelectedDayExpectedHours();
            RefreshWeekIfNeeded(SelectedDate.Date);
            SyncHeaderActiveBaseFromSummary();
            RefreshMonthSurfacesForDate(SelectedDate.Date);
        }

        private void NotifySelectedWeekChanged()
        {
            OnPropertyChanged(nameof(WeekReportHeaderText));
            OnPropertyChanged(nameof(SelectedWeekRangeText));
            OnPropertyChanged(nameof(WeekRangeText));
            OnPropertyChanged(nameof(IsSelectedWeekInFuture));
            OnPropertyChanged(nameof(IsCurrentWeekSelected));
            if (CurrentWeekCommand is RelayCommand currentWeek)
            {
                currentWeek.RaiseCanExecuteChanged();
            }
        }

        private void NotifyManualTotalsChanged()
        {
            OnPropertyChanged(nameof(ManualTotalText));
            OnPropertyChanged(nameof(GrandTotalText));
            OnPropertyChanged(nameof(SelectedDayManualTasksText));
            OnPropertyChanged(nameof(SelectedDayActiveText));
            OnPropertyChanged(nameof(SelectedDayTotalActiveText));
            OnPropertyChanged(nameof(SelectedDayStatusText));
            OnPropertyChanged(nameof(MonthlyManualTasksText));
            OnPropertyChanged(nameof(MonthlyActiveText));
            OnPropertyChanged(nameof(MonthlyTotalActiveText));
            OnPropertyChanged(nameof(MonthlyStatusText));
            OnPropertyChanged(nameof(MonthlyStatusKind));
            OnPropertyChanged(nameof(MonthlyStatusLabel));
            OnPropertyChanged(nameof(MonthlyStatusIcon));
            OnPropertyChanged(nameof(MonthlyProgressPercent));
            OnPropertyChanged(nameof(MonthlyProgressPercentText));
            OnPropertyChanged(nameof(MonthlyProgressFractionText));
            OnPropertyChanged(nameof(MonthlyStatusDetailText));
            OnPropertyChanged(nameof(MonthlyActiveHoursValue));
            OnPropertyChanged(nameof(MonthlyOfflineHoursValue));
            OnPropertyChanged(nameof(MonthlyLeaveHoursValue));
            OnPropertyChanged(nameof(MonthlyProgressPercentRaw));
            OnPropertyChanged(nameof(MonthlyProgressGifKey));
            OnPropertyChanged(nameof(HasMonthlyProgressGif));
        }

        private void NotifyMonthHeaderTotalsChanged()
        {
            OnPropertyChanged(nameof(MonthlyActiveTrackedText));
            OnPropertyChanged(nameof(MonthlyManualTasksText));
            OnPropertyChanged(nameof(MonthlyTotalActiveText));
            OnPropertyChanged(nameof(MonthlyExpectedText));
            OnPropertyChanged(nameof(MonthlyHoursText));
            OnPropertyChanged(nameof(MonthlyActiveText));
            OnPropertyChanged(nameof(MonthlyLeaveText));
            OnPropertyChanged(nameof(MonthlyHolidayCountText));
            OnPropertyChanged(nameof(MonthlyStatusText));
            OnPropertyChanged(nameof(MonthlyStatusKind));
            OnPropertyChanged(nameof(MonthlyStatusLabel));
            OnPropertyChanged(nameof(MonthlyStatusIcon));
            OnPropertyChanged(nameof(MonthlyProgressPercent));
            OnPropertyChanged(nameof(MonthlyProgressPercentText));
            OnPropertyChanged(nameof(MonthlyProgressFractionText));
            OnPropertyChanged(nameof(MonthlyStatusDetailText));
            OnPropertyChanged(nameof(MonthlyActiveHoursValue));
            OnPropertyChanged(nameof(MonthlyOfflineHoursValue));
            OnPropertyChanged(nameof(MonthlyLeaveHoursValue));
            OnPropertyChanged(nameof(MonthlyProgressPercentRaw));
            OnPropertyChanged(nameof(MonthlyProgressGifKey));
            OnPropertyChanged(nameof(HasMonthlyProgressGif));
        }

        /// <summary>
        /// Refreshes day panel, week panel, and month cell/header surfaces for one date.
        /// </summary>
        private void RefreshDate(DateTime date)
        {
            RefreshDates(new[] { date });
        }

        /// <summary>
        /// Batch refresh for one or more dates without rebuilding the full month grid.
        /// </summary>
        private void RefreshDates(IEnumerable<DateTime> dates)
        {
            var normalizedDates = dates.Select(d => d.Date).Distinct().ToList();
            if (normalizedDates.Count == 0)
            {
                return;
            }

            foreach (var date in normalizedDates)
            {
                if (date == SelectedDate.Date)
                {
                    RefreshSelectedDayManualDuration();
                }
            }

            var weekDates = normalizedDates.Where(IsDateInSelectedWeek).Distinct().ToList();
            foreach (var date in weekDates)
            {
                RefreshWeeklySummaryDay(date);
            }

            if (weekDates.Count > 0)
            {
                RecalculateWeeklyTotals();
            }

            var monthDates = normalizedDates.Where(IsDateInSelectedMonth).ToList();
            if (monthDates.Count > 0)
            {
                foreach (var date in monthDates)
                {
                    RefreshMonthDayCell(date);
                }

                foreach (var weekNumber in monthDates.Select(WorkWeekHelper.GetIsoWeekNumber).Distinct())
                {
                    RefreshMonthWeekSummary(weekNumber);
                }

                // Without this, MonthlyActiveTrackedText/MonthlyActiveText/MonthlyTotalActiveText
                // (raised by NotifyMonthHeaderTotalsChanged below) keep reading the stale
                // _monthlyAppUsage snapshot from the last full LoadMonthlyUsage() — see
                // RebuildMonthlyAppUsage's comment for the full explanation.
                RebuildMonthlyAppUsage();
                RecalculateMonthHolidayWorkedCredit();
                NotifyMonthHeaderTotalsChanged();
            }

            NotifyManualTotalsChanged();

            // Today's header/tray/floating-timer "Today Total" quick-info figures cache their
            // own manual/offline base (_headerManualBase) rather than re-reading it every
            // tick, so an offline-work edit for today needs to force that cache — and the
            // per-second display guard in RefreshHeaderActiveTimer — past their normal
            // once-per-tick gate. Same pattern already used for today's leave edits (see
            // RefreshHeaderLeaveCreditToday's call site).
            if (normalizedDates.Contains(DateTime.Today.Date))
            {
                SyncHeaderActiveBaseFromSummary();
            }

            // WorkSummaryViewModel (the Work Summary panel's Today/Week/Month/Custom view)
            // owns its own cached Active/Offline/Expected fields, computed only on Refresh().
            // Nothing else here touches it, so without this call an offline-work edit made
            // through the Manual Tasks tab would leave that panel showing stale figures until
            // the user manually changes its view mode or navigates — refresh it unconditionally
            // here since it's a cheap, disk-backed recompute over its own (usually small) date
            // range, not worth gating on whether the edited date falls inside it.
            WorkSummary.Refresh();
        }

        private void RefreshMonthSurfacesForDate(DateTime date)
        {
            if (!IsDateInSelectedMonth(date))
            {
                return;
            }

            RefreshMonthDayCell(date);
            RefreshMonthWeekSummary(WorkWeekHelper.GetIsoWeekNumber(date));
            RebuildMonthlyAppUsage();
            RecalculateMonthHolidayWorkedCredit();
            NotifyMonthHeaderTotalsChanged();
        }

        private void RefreshMonthDayCell(DateTime date)
        {
            var dayItem = _monthlyCalendarDays
                .OfType<MonthlyDayItem>()
                .FirstOrDefault(d => d.IsCurrentMonth && d.Date.Date == date.Date);

            if (dayItem == null || dayItem.IsFuture)
            {
                return;
            }

            var (activeTime, idleTime, lockedTime) = GetActivityTotalsForDate(date);
            var manualTime = TimeSpan.FromSeconds(GetManualSecondsForDate(date));
            var hasManualTasks = HasManualTasksForDate(date);
            dayItem.ApplyActivityData(activeTime, idleTime, lockedTime, manualTime, hasManualTasks);
            ApplyLeaveDataToMonthDay(dayItem, date);
        }

        private void RefreshMonthDayLeaveCell(DateTime date)
        {
            var dayItem = _monthlyCalendarDays
                .OfType<MonthlyDayItem>()
                .FirstOrDefault(d => d.IsCurrentMonth && d.Date.Date == date.Date);

            if (dayItem == null)
            {
                return;
            }

            ApplyLeaveDataToMonthDay(dayItem, date);
        }

        private void ApplyLeaveDataToMonthDay(MonthlyDayItem dayItem, DateTime date)
        {
            var entry = _leaveService.GetForDate(date.Date);
            dayItem.ApplyLeaveData(entry?.Duration, entry?.Type);
            ApplyLeaveAdjustedReferenceToMonthDayCharts(dayItem, entry?.Duration);
        }

        private static void ApplyLeaveAdjustedReferenceToMonthDayCharts(MonthlyDayItem dayItem, LeaveDuration? _)
        {
            // Reference is always date-based expected (8h weekday / 0h weekend); leave no longer reduces it.
            var expected = ExpectedHoursCalculator.GetDayExpectedHours(dayItem.Date);
            if (dayItem.ChartViewModel != null)
            {
                dayItem.ChartViewModel.ReferenceTime = expected;
                dayItem.ChartViewModel.UpdateChart();
            }

            if (dayItem.HorizontalBarViewModel != null)
            {
                dayItem.HorizontalBarViewModel.ReferenceTime = expected;
            }
        }

        private void RefreshLeaveSurfacesForDate(DateTime date)
        {
            var normalized = date.Date;

            if (normalized == DateTime.Today)
            {
                // Keeps the floating timer's/tray's "Today Total" quick-menu figure in sync
                // the moment today's own leave entry is added/edited/deleted, rather than
                // waiting for the next full day sync. RefreshHeaderActiveTimer normally
                // skips its body when the displayed second hasn't changed — force past that
                // guard since leave doesn't affect the ticking seconds, only Today Total.
                RefreshHeaderLeaveCreditToday();
                _lastDisplayedActiveSecond = -1;
                RefreshHeaderActiveTimer();
            }

            if (normalized == SelectedDate.Date)
            {
                RefreshSelectedDayExpectedHours();
            }

            if (IsDateInSelectedWeek(normalized))
            {
                RefreshWeeklySummaryDay(normalized);
                RefreshWeekExpectedHours();
                RecalculateWeeklyTotals();
            }

            if (IsDateInSelectedMonth(normalized))
            {
                RefreshMonthDayLeaveCell(normalized);
                RefreshMonthWeekSummary(WorkWeekHelper.GetIsoWeekNumber(normalized));
                // Month-to-date aware, same as LoadMonthlyUsage's _monthLeaveCredit computation:
                // a future-dated leave entry in the current month must not count until that day
                // arrives. For past months every day is <= today, so this still sums the whole
                // month unchanged.
                _monthLeaveCredit = TimeSpan.FromSeconds(
                    _monthlyCalendarDays
                        .OfType<MonthlyDayItem>()
                        .Where(d => d.IsCurrentMonth && d.Date.Date <= DateTime.Today.Date)
                        .Sum(d => d.LeaveCredit.TotalSeconds));
                RecalculateMonthHolidayWorkedCredit();
                NotifyMonthHeaderTotalsChanged();
            }
        }

        private void RefreshSelectedDayExpectedHours()
        {
            var entry = _leaveService.GetForDate(SelectedDate.Date);
            _hasSelectedDayLeave = entry != null;
            _selectedDayLeaveSummaryText = entry == null
                ? string.Empty
                : $"Leave: {FormatLeaveType(entry.Type)} — {FormatLeaveDuration(entry.Duration)}";

            // Expected is always 8h for Mon–Fri regardless of leave.
            _selectedDayExpectedHours = ExpectedHoursCalculator.GetDayExpectedHours(SelectedDate);
            _selectedDayLeaveCredit = ExpectedHoursCalculator.GetDayLeaveCredit(SelectedDate, entry?.Duration);

            OnPropertyChanged(nameof(HasSelectedDayLeave));
            OnPropertyChanged(nameof(SelectedDayLeaveSummaryText));
            OnPropertyChanged(nameof(SelectedDayExpectedHours));
            OnPropertyChanged(nameof(SelectedDayExpectedHoursText));
            OnPropertyChanged(nameof(SelectedDayLeaveHoursText));
            OnPropertyChanged(nameof(SelectedDayActiveText));
            OnPropertyChanged(nameof(SelectedDayTotalActiveText));
            OnPropertyChanged(nameof(SelectedDayStatusText));

            if (!IsSelectedDateInFuture)
            {
                UpdateActivityChart();
            }
        }

        private void RefreshWeekExpectedHours()
        {
            var weekStart = SelectedWeekStart.Date;
            var leaveEntries = WorkWeekHelper.EnumerateWeekDays(weekStart)
                .Select(date => _leaveService.GetForDate(date))
                .ToList();

            var leaveDayCount = leaveEntries.Count(e => e != null);

            // Expected is always 40h and leave does NOT reduce it — but public holidays
            // do, same exclusion rule as Monthly Usage's GetMonthlyExpectedHoursAdjusted().
            // Reuses WorkSummaryCalculator's day-level holiday-credit helper; does not
            // touch ExpectedHoursCalculator.GetWeekExpectedHours() itself.
            _weekHolidayCredit = TimeSpan.Zero;
            foreach (var date in WorkWeekHelper.EnumerateWeekDays(weekStart))
            {
                _weekHolidayCredit += WorkSummaryCalculator.GetDayHolidayCredit(date, _holidayService.IsHoliday(date));
            }

            var baseWeekExpected = ExpectedHoursCalculator.GetWeekExpectedHours();
            var adjustedWeekExpected = baseWeekExpected - _weekHolidayCredit;
            _weekExpectedHours = adjustedWeekExpected < TimeSpan.Zero ? TimeSpan.Zero : adjustedWeekExpected;

            _weekLeaveCredit = ExpectedHoursCalculator.GetWeekLeaveCredit(
                weekStart,
                date => _leaveService.GetForDate(date)?.Duration);
            _hasWeekLeave = leaveDayCount > 0;
            _weekLeaveSummaryText = _hasWeekLeave
                ? $"{leaveDayCount} leave day(s)"
                : string.Empty;

            OnPropertyChanged(nameof(HasWeekLeave));
            OnPropertyChanged(nameof(WeekLeaveSummaryText));
            OnPropertyChanged(nameof(WeekExpectedHours));
            OnPropertyChanged(nameof(WeekExpectedHoursText));
            OnPropertyChanged(nameof(WeekLeaveHoursText));
            OnPropertyChanged(nameof(WeekActiveText));
            OnPropertyChanged(nameof(WeekTotalActiveText));
            OnPropertyChanged(nameof(WeekStatusText));

            if (!IsSelectedWeekInFuture)
            {
                UpdateWeeklyActivityChart();
            }
        }

        private static TimeSpan GetChartReferenceTime(TimeSpan expectedHours)
        {
            return expectedHours < TimeSpan.Zero ? TimeSpan.Zero : expectedHours;
        }

        private void RefreshMonthWeekSummary(int weekNumber)
        {
            var monthDays = _monthlyCalendarDays.OfType<MonthlyDayItem>();
            var burntHours = HoursCalculationHelper.SumBurntHoursForWeek(
                monthDays, weekNumber, _selectedYear, _selectedMonth, DateTime.Today);
            var expectedHours = HoursCalculationHelper.GetExpectedHoursForWeek(
                monthDays, weekNumber, _selectedYear, _selectedMonth, DateTime.Today);

            foreach (var summary in _monthlyCalendarDays.OfType<WeeklySummaryDayItem>().Where(s => s.WeekNumber == weekNumber))
            {
                summary.BurntHours = burntHours;
                summary.ExpectedHours = expectedHours;
            }
        }

        private (TimeSpan Active, TimeSpan Idle, TimeSpan Locked) GetActivityTotalsForDate(DateTime date)
        {
            if (!_activityLogReader.TryReadDay(date.Date, out var entries))
            {
                return (TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);
            }

            var totals = HoursCalculationHelper.SumActivityEntries(entries);
            return (totals.Active, totals.Idle, totals.Locked);
        }

        private void ResetManualEditState()
        {
            SelectedManualTask = null;
            IsManualEditMode = false;
        }

        private void NotifySelectedDaySummaryTextsChanged()
        {
            OnPropertyChanged(nameof(SelectedDayStartText));
            OnPropertyChanged(nameof(SelectedDayEndText));
            OnPropertyChanged(nameof(SelectedDayActiveTrackedText));
            OnPropertyChanged(nameof(SelectedDayManualTasksText));
            OnPropertyChanged(nameof(SelectedDayActiveText));
            OnPropertyChanged(nameof(SelectedDayTotalActiveText));
            OnPropertyChanged(nameof(SelectedDayStatusText));
            if (!IsSelectedDateInFuture)
            {
                UpdateActivityChart();
            }
        }

        private bool IsDateInSelectedWeek(DateTime date) =>
            WorkWeekHelper.IsDateInWeek(date, SelectedWeekStart.Date);

        private void RefreshWeekIfNeeded(DateTime date)
        {
            if (!IsDateInSelectedWeek(date))
            {
                return;
            }

            RefreshWeeklySummaryDay(date);
            RecalculateWeeklyTotals();
        }

        private bool IsDateInSelectedMonth(DateTime date)
        {
            return date.Year == _selectedYear && date.Month == _selectedMonth;
        }

        private bool IsSelectedDateInSelectedMonth() => IsDateInSelectedMonth(SelectedDate);

        private void ReloadMonth()
        {
            LoadMonthlyUsage();
        }

        private void LoadMonthlyUsage()
        {
            _monthlyCalendarDays.Clear();
            _monthlyWeeklySummaries.Clear();

            var monthLeaves = _leaveService.LoadMonth(_selectedYear, _selectedMonth);
            var leaveByDate = monthLeaves.ToDictionary(l => l.Date.Date);

            var holidayDates = new System.Collections.Generic.HashSet<DateTime>(_holidayService.LoadYear(_selectedYear).Select(h => h.Date.Date));

            var (calendarStart, calendarEnd) = WorkWeekHelper.GetMonthCalendarGridRange(_selectedYear, _selectedMonth);
            MonthlyCalendarRowCount = (int)((calendarEnd - calendarStart).TotalDays + 1) / 7;

            RebuildMonthlyAppUsage();

            // Second pass: populate calendar days with per-day data
            for (var date = calendarStart; date <= calendarEnd; date = date.AddDays(1))
            {
                var dayItem = new MonthlyDayItem
                {
                    Date = date,
                    IsCurrentMonth = date.Month == _selectedMonth && date.Year == _selectedYear
                };

                dayItem.IsFuture = date.Date > DateTime.Today;
                if (!dayItem.IsFuture)
                {
                    // Expected = 8h on weekdays, 0 on weekends — not reduced by leave.
                    var dayExpectedHours = ExpectedHoursCalculator.GetDayExpectedHours(date);

                    // Create vertical chart (existing)
                    var chartViewModel = new ActivityChartViewModel();
                    chartViewModel.ReferenceTime = dayExpectedHours;
                    chartViewModel.ShowReferenceLabel = false;
                    chartViewModel.ShowLegend = false;
                    chartViewModel.ShowDataLabels = false;
                    chartViewModel.ShowTotalActivityOnly = true; // Show only Total Activity bar in month view
                    chartViewModel.UpdateBarSizing(90, 80); // Slightly larger for monthly calendar to increase bar width
                    chartViewModel.XAxisLabels = new string[] { "", "", "" }; // Hide axis labels for compact monthly view
                    dayItem.ChartViewModel = chartViewModel;

                    // Create horizontal bar (new compact view)
                    var horizontalBarViewModel = new HorizontalActivityBarViewModel();
                    horizontalBarViewModel.ReferenceTime = dayExpectedHours;
                    dayItem.HorizontalBarViewModel = horizontalBarViewModel;

                    dayItem.HasChart = true;
                }
                else
                {
                    dayItem.HasChart = false;
                }

                // Set week number
                dayItem.WeekNumber = WorkWeekHelper.GetIsoWeekNumber(date);

                if (dayItem.IsCurrentMonth)
                {
                    var (activeTime, idleTime, lockedTime) = GetActivityTotalsForDate(date.Date);
                    var manualTime = TimeSpan.FromSeconds(GetManualSecondsForDate(date.Date));
                    var hasManualTasks = HasManualTasksForDate(date.Date);

                    if (leaveByDate.TryGetValue(date.Date, out var leaveEntry))
                    {
                        dayItem.LeaveDuration = leaveEntry.Duration;
                        dayItem.LeaveType = leaveEntry.Type;
                    }

                    dayItem.IsHoliday = holidayDates.Contains(date.Date);

                    dayItem.ApplyActivityData(activeTime, idleTime, lockedTime, manualTime, hasManualTasks);
                }

                _monthlyCalendarDays.Add(dayItem);
            }

            // Calculate weekly summaries (Burnt Hours + Expected Hours) via the same reusable
            // helpers RefreshMonthWeekSummary uses later, so the two code paths can never drift
            // apart the way the old inline-LINQ version here and the shared helper used to.
            var monthDaysForWeeklySummary = _monthlyCalendarDays.OfType<MonthlyDayItem>();
            var weeklyGroups = _monthlyCalendarDays
                .Where(d => d.IsCurrentMonth && d is MonthlyDayItem)
                .Cast<MonthlyDayItem>()
                .Select(d => d.WeekNumber)
                .Distinct()
                .OrderBy(w => w)
                .ToDictionary(
                    w => w,
                    w => (
                        Burnt: HoursCalculationHelper.SumBurntHoursForWeek(monthDaysForWeeklySummary, w, _selectedYear, _selectedMonth, DateTime.Today),
                        Expected: HoursCalculationHelper.GetExpectedHoursForWeek(monthDaysForWeeklySummary, w, _selectedYear, _selectedMonth, DateTime.Today)));

            // Create the final calendar grid with proper layout
            var finalCalendarItems = new List<CalendarDayItemBase>();
            var currentWeekItems = new List<CalendarDayItemBase>();
            int currentWeekNumber = -1;

            foreach (var item in _monthlyCalendarDays)
            {
                if (item is MonthlyDayItem dayItem)
                {
                    // Check if we're moving to a new week
                    if (dayItem.WeekNumber != currentWeekNumber)
                    {
                        // Add previous week items + summary if we had a week
                        if (currentWeekItems.Count > 0)
                        {
                            finalCalendarItems.AddRange(currentWeekItems);
                            
                            // Add weekly summary if we have data for this week
                            if (weeklyGroups.TryGetValue(currentWeekNumber, out var weekTotals))
                            {
                                var summaryItem = new WeeklySummaryDayItem
                                {
                                    WeekNumber = currentWeekNumber,
                                    BurntHours = weekTotals.Burnt,
                                    ExpectedHours = weekTotals.Expected
                                };
                                finalCalendarItems.Add(summaryItem);
                            }

                            // Fill remaining slots in the week if needed
                            while (currentWeekItems.Count < 8)
                            {
                                currentWeekItems.Add(new WeeklySummaryDayItem { WeekNumber = currentWeekNumber, BurntHours = TimeSpan.Zero, ExpectedHours = TimeSpan.Zero });
                            }
                        }
                        
                        // Start new week
                        currentWeekItems = new List<CalendarDayItemBase> { dayItem };
                        currentWeekNumber = dayItem.WeekNumber;
                    }
                    else
                    {
                        currentWeekItems.Add(dayItem);
                    }
                }
            }

            // Add the last week
            if (currentWeekItems.Count > 0)
            {
                finalCalendarItems.AddRange(currentWeekItems);
                
                // Add weekly summary for the last week
                if (weeklyGroups.TryGetValue(currentWeekNumber, out var weekTotals))
                {
                    var summaryItem = new WeeklySummaryDayItem
                    {
                        WeekNumber = currentWeekNumber,
                        BurntHours = weekTotals.Burnt,
                        ExpectedHours = weekTotals.Expected
                    };
                    finalCalendarItems.Add(summaryItem);
                }

                // Fill remaining slots in the last week if needed
                while (currentWeekItems.Count < 8)
                {
                    currentWeekItems.Add(new WeeklySummaryDayItem { WeekNumber = currentWeekNumber, BurntHours = TimeSpan.Zero, ExpectedHours = TimeSpan.Zero });
                }
            }

            // Clear and rebuild the calendar collection with proper layout
            _monthlyCalendarDays.Clear();
            foreach (var item in finalCalendarItems)
            {
                _monthlyCalendarDays.Add(item);
            }

            // Compute monthly leave credit from in-month Mon–Fri leave entries — month-to-date
            // aware (same WorkSummaryCalculator.EnumerateMonthView enumeration already used for
            // _monthHolidayCredit below), NOT ExpectedHoursCalculator.GetMonthLeaveCredit's plain
            // full-month sum: a leave entry planned for later in the current month must not
            // inflate Total Active Hours / progress before that date actually happens. Past
            // months are unaffected since EnumerateMonthView yields the whole month once
            // "today" is past month-end.
            var monthLeaveLookup = monthLeaves.ToDictionary(l => l.Date.Date);
            _monthLeaveCredit = TimeSpan.Zero;
            foreach (var leaveDate in WorkSummaryCalculator.EnumerateMonthView(_selectedYear, _selectedMonth, DateTime.Today))
            {
                var duration = monthLeaveLookup.TryGetValue(leaveDate, out var leaveEntry) ? leaveEntry?.Duration : null;
                _monthLeaveCredit += ExpectedHoursCalculator.GetDayLeaveCredit(leaveDate, duration);
            }

            // Monthly Usage summary's Expected Hours excludes public holidays — reuses the
            // same additive, month-to-date-aware day enumeration and holiday-credit helper
            // already used by the Work Summary panel, so this doesn't touch
            // ExpectedHoursCalculator.GetMonthExpectedHours itself (still used unadjusted
            // elsewhere) or any other view's calculations.
            _monthHolidayCredit = TimeSpan.Zero;
            _monthHolidayDayCount = 0;
            foreach (var holidayDate in WorkSummaryCalculator.EnumerateMonthView(_selectedYear, _selectedMonth, DateTime.Today))
            {
                if (!holidayDates.Contains(holidayDate.Date))
                {
                    continue;
                }

                _monthHolidayCredit += WorkSummaryCalculator.GetDayHolidayCredit(holidayDate, isHoliday: true);
                _monthHolidayDayCount++;
            }

            RecalculateMonthHolidayWorkedCredit();

            // "Monthly Hours" (MonthlyHoursText) — the full selected month's working hours,
            // NOT month-to-date capped: unlike _monthHolidayCredit above (which stops at
            // today so Expected Hours only reflects days that have actually happened),
            // this always covers the whole month regardless of "today", for every month
            // (past, current, or future).
            var monthStart = new DateTime(_selectedYear, _selectedMonth, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            _monthFullHolidayCredit = TimeSpan.Zero;
            for (var fullMonthDate = monthStart; fullMonthDate <= monthEnd; fullMonthDate = fullMonthDate.AddDays(1))
            {
                if (!holidayDates.Contains(fullMonthDate.Date))
                {
                    continue;
                }

                _monthFullHolidayCredit += WorkSummaryCalculator.GetDayHolidayCredit(fullMonthDate, isHoliday: true);
            }

            NotifyMonthHeaderTotalsChanged();
        }

        // Rebuilds the per-process _monthlyAppUsage aggregate (the Monthly Usage tab's
        // app-breakdown list, and the source for MonthlyActiveTrackedText/MonthlyActiveText/
        // MonthlyTotalActiveText) by rescanning every day's log file for the selected month.
        //
        // Must be called not just from LoadMonthlyUsage() but also from every lighter-weight
        // refresh path (RefreshDates, RefreshMonthSurfacesForDate) that updates today's
        // MonthlyDayItem tile — those previously only called NotifyMonthHeaderTotalsChanged(),
        // which re-raises PropertyChanged on these Text properties without recomputing the
        // _monthlyAppUsage data they read from, so the header kept showing whatever totals
        // existed at the last full LoadMonthlyUsage() (e.g. app startup) while the calendar
        // tile for today kept growing throughout the day — always looking "too low" until
        // the user switched months or restarted the app.
        private void RebuildMonthlyAppUsage()
        {
            _monthlyAppUsage.Clear();

            var start = new DateTime(_selectedYear, _selectedMonth, 1);
            var end = start.AddMonths(1).AddDays(-1);

            var perProcess = new System.Collections.Generic.Dictionary<string, (TimeSpan Active, TimeSpan Idle, TimeSpan Locked)>(StringComparer.OrdinalIgnoreCase);

            for (var date = start; date <= end; date = date.AddDays(1))
            {
                if (!_activityLogReader.TryReadDay(date.Date, out var entries))
                {
                    continue;
                }

                foreach (var entry in entries)
                {
                    var totals = HoursCalculationHelper.SumActivityEntries(new[] { entry });
                    string processName = entry.ProcessName;
                    if (string.IsNullOrWhiteSpace(processName))
                    {
                        processName = "(Unknown)";
                    }

                    if (!perProcess.TryGetValue(processName, out var existing))
                    {
                        existing = (TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);
                    }

                    perProcess[processName] = (
                        existing.Active + totals.Active,
                        existing.Idle + totals.Idle,
                        existing.Locked + totals.Locked);
                }
            }

            foreach (var kvp in perProcess
                         .OrderByDescending(k => k.Value.Active)
                         .ThenByDescending(k => k.Value.Idle)
                         .ThenByDescending(k => k.Value.Locked))
            {
                _monthlyAppUsage.Add(new MonthlyAppUsageDto
                {
                    ProcessName = kvp.Key,
                    TotalActive = kvp.Value.Active,
                    TotalIdle = kvp.Value.Idle,
                    TotalLocked = kvp.Value.Locked
                });
            }

            IsMonthlyUsageEmpty = _monthlyAppUsage.Count == 0;
        }

        // Recomputes _monthHolidayWorkedCredit — the tracked/manual/leave hours that fell on
        // a holiday date this month (month-to-date aware), which GetMonthlyProgressHours
        // subtracts out. Reads straight from the live _monthlyCalendarDays tiles (already
        // kept current by RefreshMonthDayCell) rather than caching a separate date scan, so
        // it must be re-run any time those tiles' TotalActive/ManualTime change — every
        // lighter-weight refresh path that already calls RebuildMonthlyAppUsage() calls this
        // alongside it, the same way MonthlyActiveTrackedText/MonthlyTotalActiveText need
        // RebuildMonthlyAppUsage() re-run to avoid reading a stale snapshot.
        private void RecalculateMonthHolidayWorkedCredit()
        {
            var today = DateTime.Today;
            _monthHolidayWorkedCredit = TimeSpan.FromTicks(_monthlyCalendarDays
                .OfType<MonthlyDayItem>()
                .Where(d => d.IsCurrentMonth && d.IsHoliday && d.Date.Date <= today)
                .Sum(d => (d.TotalActive + d.ManualTime + d.LeaveCredit).Ticks));
        }

        public string MonthlyActiveTrackedText
        {
            get
            {
                var tracked = TimeSpan.FromSeconds(_monthlyAppUsage.Sum(x => Math.Max(0, x.TotalActive.TotalSeconds)));
                return FormatTimeSpan(tracked);
            }
        }

        public string MonthlyManualTasksText => FormatTimeSpan(TimeSpan.FromSeconds(GetManualSecondsForMonth(SelectedMonthDateTime)));

        // ── New unified monthly properties ────────────────────────────────────────

        // Month-to-date Expected Hours minus public-holiday hours (weekday holidays
        // only — weekends already contribute 0). Reuses the same additive
        // WorkSummaryCalculator helpers as the Work Summary panel; does not alter
        // ExpectedHoursCalculator.GetMonthExpectedHours itself, so nothing else that
        // calls it (e.g. HoursCalculationHelper.CalculateMonthSummary, tests) is affected.
        // Labeled "Expected So Far" in the header — deliberately stops at today, unlike
        // MonthlyHoursText below.
        private TimeSpan GetMonthlyExpectedHoursAdjusted()
        {
            var expected = ExpectedHoursCalculator.GetMonthExpectedHours(_selectedYear, _selectedMonth, DateTime.Today) - _monthHolidayCredit;
            return expected < TimeSpan.Zero ? TimeSpan.Zero : expected;
        }

        // Total Active / progress hours for the Monthly Usage summary: tracked + manual +
        // leave credit, minus _monthHolidayWorkedCredit (any tracked/manual/leave hours that
        // fell on a holiday date) — a holiday is never an expected work day, so activity
        // recorded on it doesn't count toward the month total, mirroring Expected Hours'
        // own holiday exclusion (_monthHolidayCredit) and
        // HoursCalculationHelper.SumBurntHoursForWeek's per-week rule. Single source shared
        // by MonthlyTotalActiveText and every Status/Progress property below so they can
        // never drift apart.
        private TimeSpan GetMonthlyProgressHours(TimeSpan tracked, TimeSpan manual)
        {
            var progress = tracked + manual + _monthLeaveCredit - _monthHolidayWorkedCredit;
            return progress < TimeSpan.Zero ? TimeSpan.Zero : progress;
        }

        // All "Expected" hours displays app-wide use ToExpectedHoursText() ("Xh Ym", or just
        // "Xh" when minutes is 0) rather than FormatTimeSpan/ToHoursMinutes.
        public string MonthlyExpectedText => GetMonthlyExpectedHoursAdjusted().ToExpectedHoursText();

        // "Monthly Hours" — the full selected month's working hours (weekday count × 8h)
        // minus the FULL month's holiday credit, excluding weekends and holidays. Unlike
        // MonthlyExpectedText ("Expected So Far"), this does NOT depend on today's date:
        // it uses ExpectedHoursCalculator.GetMonthExpectedHours(year, month) — the plain
        // full-month overload, not the month-to-date one — and _monthFullHolidayCredit
        // (computed over the whole month in LoadMonthlyUsage, unlike the month-to-date
        // _monthHolidayCredit). Leave does not reduce this, same rule as Expected Hours.
        public string MonthlyHoursText
        {
            get
            {
                var fullMonthExpected = ExpectedHoursCalculator.GetMonthExpectedHours(_selectedYear, _selectedMonth) - _monthFullHolidayCredit;
                return (fullMonthExpected < TimeSpan.Zero ? TimeSpan.Zero : fullMonthExpected).ToExpectedHoursText();
            }
        }

        // Active = tracked + manual.
        public string MonthlyActiveText
        {
            get
            {
                var tracked = TimeSpan.FromSeconds(_monthlyAppUsage.Sum(x => Math.Max(0, x.TotalActive.TotalSeconds)));
                var manual = TimeSpan.FromSeconds(GetManualSecondsForMonth(SelectedMonthDateTime));
                return FormatTimeSpan(tracked + manual);
            }
        }

        public string MonthlyLeaveText => FormatTimeSpan(_monthLeaveCredit);

        // Shown as a 4th "Holiday" legend chip next to Active/Offline/Leave — a day count
        // rather than an hours total, since holiday hours never contribute Active time and
        // a count is what explains the Expected Hours deduction above (_monthHolidayCredit).
        public string MonthlyHolidayCountText => _monthHolidayDayCount == 1 ? "1 day" : $"{_monthHolidayDayCount} days";

        // TotalActive = tracked + manual + leave credit, minus any hours that fell on a
        // holiday date — see GetMonthlyProgressHours. Shared by both UiA ("Burnt Hours"
        // header) and UiB ("Total Active" figure) — same calculation, only the label/layout
        // differs between the two UIs. Summing every week's BurntHoursText for the month
        // equals this figure exactly.
        public string MonthlyTotalActiveText
        {
            get
            {
                var tracked = TimeSpan.FromSeconds(_monthlyAppUsage.Sum(x => Math.Max(0, x.TotalActive.TotalSeconds)));
                var manual = TimeSpan.FromSeconds(GetManualSecondsForMonth(SelectedMonthDateTime));
                return FormatTimeSpan(GetMonthlyProgressHours(tracked, manual));
            }
        }

        public string MonthlyStatusText
        {
            get
            {
                var tracked = TimeSpan.FromSeconds(_monthlyAppUsage.Sum(x => Math.Max(0, x.TotalActive.TotalSeconds)));
                var manual = TimeSpan.FromSeconds(GetManualSecondsForMonth(SelectedMonthDateTime));
                var summary = new HoursCalculationHelper.PeriodHoursSummary
                {
                    Expected = ExpectedHoursCalculator.GetMonthExpectedHours(_selectedYear, _selectedMonth, DateTime.Today),
                    Active = GetMonthlyProgressHours(tracked, manual),
                    Leave = TimeSpan.Zero
                };
                return summary.StatusText;
            }
        }

        // Same Expected/Progress comparison as MonthlyStatusText above — just surfaced as a
        // key (for visual styling) and a label matching the Work Summary vocabulary, for the
        // redesigned Monthly Usage summary card.
        public string MonthlyStatusKind
        {
            get
            {
                var tracked = TimeSpan.FromSeconds(_monthlyAppUsage.Sum(x => Math.Max(0, x.TotalActive.TotalSeconds)));
                var manual = TimeSpan.FromSeconds(GetManualSecondsForMonth(SelectedMonthDateTime));
                var expected = GetMonthlyExpectedHoursAdjusted();
                var progress = GetMonthlyProgressHours(tracked, manual);

                if (progress < expected) return "RemainingWork";
                if (progress > expected) return "ExtraWorked";
                return "TargetAchieved";
            }
        }

        public string MonthlyStatusLabel => MonthlyStatusKind switch
        {
            "RemainingWork" => "Needed",
            "ExtraWorked" => "Overworked",
            _ => "Achieved"
        };

        // Simple glyph paired with the status banner's color — plain Unicode (not an
        // icon-font codepoint) so it renders correctly without depending on Segoe MDL2
        // Assets glyph availability.
        public string MonthlyStatusIcon => MonthlyStatusKind switch
        {
            "RemainingWork" => "⌛", // hourglass
            "ExtraWorked" => "▲", // up triangle
            _ => "✓" // checkmark
        };

        // Drives the donut ring's fill and the big percentage readout. Progress = Total
        // Active Hours (tracked + manual) + Leave, same as MonthlyStatusKind.
        //
        // No upper cap: below 100% this is the true ratio (e.g. 175h33m/176h is exactly
        // 99.74, never rounded/forced to 100), and once hours exceed Expected it keeps
        // climbing past 100 (e.g. 110h/100h is exactly 110) instead of flattening out at
        // "100%" the moment the target is reached — that flattening previously hid how
        // much extra was worked. CircularProgressRing clamps internally when rendering
        // the arc itself, so a value above 100 just draws as a full ring, never crashes.
        public double MonthlyProgressPercent
        {
            get
            {
                var tracked = TimeSpan.FromSeconds(_monthlyAppUsage.Sum(x => Math.Max(0, x.TotalActive.TotalSeconds)));
                var manual = TimeSpan.FromSeconds(GetManualSecondsForMonth(SelectedMonthDateTime));
                var expected = GetMonthlyExpectedHoursAdjusted();
                var progress = GetMonthlyProgressHours(tracked, manual);

                if (expected <= TimeSpan.Zero)
                {
                    return progress > TimeSpan.Zero ? 100.0 : 0.0;
                }

                var ratio = progress.TotalSeconds / expected.TotalSeconds * 100.0;
                return ratio < 0.0 ? 0.0 : ratio;
            }
        }

        // Up to 2 decimal places (trailing zeros trimmed) so a near-complete month reads
        // as e.g. "99.74%" instead of rounding to a misleading "100%", and an overworked
        // month reads as e.g. "110%" instead of flattening at "100%". Only the exact
        // completion point (progress == expected, to the tick) shows the bare "100%".
        public string MonthlyProgressPercentText =>
            MonthlyProgressPercent == 100.0 ? "100%" : $"{MonthlyProgressPercent:0.##}%";

        // Same Progress/Expected ratio as MonthlyProgressPercent (both are uncapped now),
        // except when there's no Expected Hours at all: Percent shows 100/0 based on
        // whether any progress exists (a sensible display default), while Raw stays 0 so
        // it can't accidentally land in the GIF's 90-110%+ threshold bands below.
        public double MonthlyProgressPercentRaw
        {
            get
            {
                var tracked = TimeSpan.FromSeconds(_monthlyAppUsage.Sum(x => Math.Max(0, x.TotalActive.TotalSeconds)));
                var manual = TimeSpan.FromSeconds(GetManualSecondsForMonth(SelectedMonthDateTime));
                var expected = GetMonthlyExpectedHoursAdjusted();
                var progress = GetMonthlyProgressHours(tracked, manual);

                if (expected <= TimeSpan.Zero)
                {
                    return 0.0;
                }

                return progress.TotalSeconds / expected.TotalSeconds * 100.0;
            }
        }

        // Monthly overview GIF, next to the summary (distinct from the per-day tile GIF
        // logic in MonthlyDayItem.ActiveHoursGifKey, which this does not touch):
        //   [90, 100)  -> TowardsTarget
        //   [100, 110] -> ReachedTarget
        //   (110, ∞)   -> OverBurned
        //   below 90   -> none
        public string? MonthlyProgressGifKey
        {
            get
            {
                var percent = MonthlyProgressPercentRaw;
                if (percent >= 90.0 && percent < 100.0) return "TowardsTarget";
                if (percent >= 100.0 && percent <= 110.0) return "ReachedTarget";
                if (percent > 110.0) return "OverBurned";
                return null;
            }
        }

        public bool HasMonthlyProgressGif => MonthlyProgressGifKey != null;

        // Donut 1's center label — always "completed / expected" (e.g. "72h / 96h"),
        // regardless of which state is active. Same Progress/Expected values as
        // MonthlyStatusKind.
        public string MonthlyProgressFractionText
        {
            get
            {
                var tracked = TimeSpan.FromSeconds(_monthlyAppUsage.Sum(x => Math.Max(0, x.TotalActive.TotalSeconds)));
                var manual = TimeSpan.FromSeconds(GetManualSecondsForMonth(SelectedMonthDateTime));
                var expected = GetMonthlyExpectedHoursAdjusted();
                var progress = GetMonthlyProgressHours(tracked, manual);
                return $"{FormatHoursShort(progress)} / {FormatHoursShort(expected)}";
            }
        }

        // The single indicator shown below Donut 1 — same Expected/Progress comparison
        // as MonthlyStatusKind, phrased so the user never has to do the subtraction
        // themselves. Exactly one of these three is ever shown:
        //   Remaining        -> "Remaining: Xh Ym"
        //   Target Achieved  -> "Target Achieved" (the 72h/96h fraction is already
        //                        equal, so no extra hours are repeated here)
        //   Worked More      -> "Worked More: +Xh Ym"
        // Bare hours value — no "Remaining:"/"Worked More:" text, since the summary's
        // status column shows MonthlyStatusLabel ("Remaining"/"Achieved"/"Overworked")
        // above this as its own line.
        public string MonthlyStatusDetailText
        {
            get
            {
                var tracked = TimeSpan.FromSeconds(_monthlyAppUsage.Sum(x => Math.Max(0, x.TotalActive.TotalSeconds)));
                var manual = TimeSpan.FromSeconds(GetManualSecondsForMonth(SelectedMonthDateTime));
                var expected = GetMonthlyExpectedHoursAdjusted();
                var progress = GetMonthlyProgressHours(tracked, manual);
                var diff = progress - expected;

                return MonthlyStatusKind switch
                {
                    "RemainingWork" => FormatHoursShort(-diff),
                    "ExtraWorked" => $"+{FormatHoursShort(diff)}",
                    _ => $"{FormatHoursShort(progress)} / {FormatHoursShort(expected)}"
                };
            }
        }

        // "Xh Ym" — omits minutes when they're zero (e.g. "96h" rather than "96h 0m").
        // Used only by the Monthly Usage donuts; other figures on that card keep
        // FormatTimeSpan's existing HH:MM style unchanged.
        // TEMP DIAGNOSTIC: seconds appended to help pinpoint a reported minute-level mismatch
        // between Day/Week/Month/Week Report totals. Revert to the m==0 ? "{h}h" : "{h}h {m}m"
        // form once confirmed.
        private static string FormatHoursShort(TimeSpan span)
        {
            int h = (int)span.TotalHours;
            int m = span.Minutes;
            int s = span.Seconds;
            return $"{h}h {m}m {s}s";
        }

        // ── Donut 2 segment values (Active / Offline / Leave), in hours — raw doubles
        // purely for chart-arc proportions. Each reuses the exact same underlying sum
        // already shown as text elsewhere on this card (MonthlyActiveTrackedText,
        // MonthlyManualTasksText, MonthlyLeaveText) — no new calculation.
        public double MonthlyActiveHoursValue =>
            TimeSpan.FromSeconds(_monthlyAppUsage.Sum(x => Math.Max(0, x.TotalActive.TotalSeconds))).TotalHours;

        public double MonthlyOfflineHoursValue =>
            TimeSpan.FromSeconds(GetManualSecondsForMonth(SelectedMonthDateTime)).TotalHours;

        public double MonthlyLeaveHoursValue => _monthLeaveCredit.TotalHours;

        private int GetManualSecondsForMonth(DateTime month) =>
            HoursCalculationHelper.SumManualSecondsForMonth(
                month.Year,
                month.Month,
                date => GetManualSecondsForDate(date));

        private int GetManualSecondsForDate(DateTime date)
        {
            try
            {
                return _manualTaskService.Load(date.Date).Sum(x => Math.Max(0, x.TotalSeconds));
            }
            catch
            {
                return 0;
            }
        }

        private void RefreshSelectedDayManualDuration()
        {
            _selectedDayManualDuration = TimeSpan.FromSeconds(GetManualSecondsForDate(SelectedDate.Date));
            // NotifySelectedDaySummaryTextsChanged (not a bare UpdateActivityChart call) —
            // otherwise SelectedDayManualTasksText/SelectedDayActiveText/
            // SelectedDayTotalActiveText/SelectedDayStatusText (all plain computed
            // properties with no backing-field setter to raise PropertyChanged on their own)
            // silently keep showing stale offline-work figures after an edit: only the pie
            // chart would redraw, while the adjacent "Offline"/"Total Active"/status labels
            // sat frozen until the user navigated away and back.
            NotifySelectedDaySummaryTextsChanged();
        }

        public DateTime ManualTasksDate
        {
            get => _manualTasksDate;
            set => SetManualTasksDateInternal(
                (value == default ? DateTime.Today : value).Date,
                syncTimeline: true);
        }

        private void SetManualTasksDateInternal(DateTime normalized, bool syncTimeline)
        {
            if (_manualTasksDate != normalized)
            {
                _manualTasksDate = normalized;
                OnPropertyChanged(nameof(ManualTasksDate));
                LoadManualTasksForSelectedDate();
            }

            if (syncTimeline && _timelineViewMode == TimelineViewMode.Date && _timelineCurrentDate != normalized)
            {
                SetTimelineCurrentDateInternal(normalized, syncManualTasksDate: false);
            }
        }

        public DateTime SelectedWeekStart
        {
            get => _weekStartDate;
            set
            {
                var normalized = WorkWeekHelper.GetWeekStartMonday(value);
                if (_weekStartDate == normalized)
                {
                    return;
                }

                _weekStartDate = normalized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(WeekStartDate));
                SyncWeekPickerDate(normalized);
                NotifySelectedWeekChanged();
                LoadWeeklySummary();
            }
        }

        public DateTime? WeekPickerDate
        {
            get => _weekPickerDate;
            set
            {
                if (_weekPickerDate == value)
                {
                    return;
                }

                _weekPickerDate = value;
                OnPropertyChanged();

                if (!_isSyncingWeekPicker && value.HasValue)
                {
                    JumpToWeek(value.Value);
                }
            }
        }

        public DateTime WeekStartDate
        {
            get => SelectedWeekStart;
            set => JumpToWeek(value);
        }

        public bool IsSelectedWeekInFuture => SelectedWeekStart.Date > DateTime.Today;

        public bool IsCurrentWeekSelected =>
            SelectedWeekStart.Date == WorkWeekHelper.GetWeekStartMonday(DateTime.Today);

        public string WeekReportHeaderText => $"Week Report (WK{WorkWeekHelper.GetIsoWeekNumber(SelectedWeekStart.Date)})";

        public string SelectedWeekRangeText
        {
            get
            {
                var from = SelectedWeekStart.Date;
                var to = from.AddDays(6);
                return $"{from:dd-MMM-yyyy} → {to:dd-MMM-yyyy}";
            }
        }

        public string WeekRangeText => SelectedWeekRangeText;

        private void JumpToWeek(DateTime date)
        {
            var weekStart = WorkWeekHelper.GetWeekStartMonday(date);
            if (_weekStartDate != weekStart)
            {
                SelectedWeekStart = weekStart;
            }
        }

        private void SyncWeekPickerDate(DateTime date)
        {
            _isSyncingWeekPicker = true;
            try
            {
                var normalized = date.Date;
                if (_weekPickerDate != normalized)
                {
                    _weekPickerDate = normalized;
                    OnPropertyChanged(nameof(WeekPickerDate));
                }
            }
            finally
            {
                _isSyncingWeekPicker = false;
            }
        }

        public int IdleThresholdMinutes
        {
            get => _idleThresholdMinutes;
            set
            {
                if (_idleThresholdMinutes != value)
                {
                    _idleThresholdMinutes = value;
                    OnPropertyChanged();
                }
            }
        }

        public int PollIntervalSeconds
        {
            get => _pollIntervalSeconds;
            set
            {
                if (_pollIntervalSeconds != value)
                {
                    _pollIntervalSeconds = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool EnableLiveRefresh
        {
            get => _enableLiveRefresh;
            set
            {
                if (_enableLiveRefresh != value)
                {
                    _enableLiveRefresh = value;
                    OnPropertyChanged();
                    ApplyLiveRefreshSettings();
                }
            }
        }

        // Unlike most Settings-tab toggles above (which only take effect after "Save
        // Settings" is clicked), this one applies and persists immediately: the floating
        // timer can appear the very next time the window is minimized/closed to tray, well
        // before the user might touch Save for anything else. Also toggled from the
        // floating timer's own "Hide Floating Timer" quick action (FloatingTimerService),
        // which is why the persist step re-reads from disk rather than reusing
        // _settingsSnapshot — that snapshot only reflects the last explicit Save, not this
        // property's own already-applied value.
        public bool ShowFloatingTimerOnMinimize
        {
            get => _showFloatingTimerOnMinimize;
            set
            {
                if (_showFloatingTimerOnMinimize != value)
                {
                    _showFloatingTimerOnMinimize = value;
                    OnPropertyChanged();
                    _settingsSnapshot.ShowFloatingTimerOnMinimize = value;
                    PersistShowFloatingTimerOnMinimize(value);
                }
            }
        }

        private void PersistShowFloatingTimerOnMinimize(bool value)
        {
            try
            {
                var settings = _settingsService?.Load() ?? new AppSettings();
                settings.ShowFloatingTimerOnMinimize = value;
                _settingsService?.Save(settings);
            }
            catch
            {
            }
        }

        public int LiveRefreshIntervalSeconds
        {
            get => _liveRefreshIntervalSeconds;
            set
            {
                if (_liveRefreshIntervalSeconds != value)
                {
                    _liveRefreshIntervalSeconds = value;
                    OnPropertyChanged();
                    ApplyLiveRefreshSettings();
                }
            }
        }

        public int CrashLogRetentionDays
        {
            get => _crashLogRetentionDays;
            set
            {
                if (_crashLogRetentionDays != value)
                {
                    _crashLogRetentionDays = value;
                    OnPropertyChanged();
                }
            }
        }

        public int CrashLogMaxSizeMB
        {
            get => _crashLogMaxSizeMB;
            set
            {
                if (_crashLogMaxSizeMB != value)
                {
                    _crashLogMaxSizeMB = value;
                    OnPropertyChanged();
                }
            }
        }

        public TimeSpan TotalActiveTimeToday
        {
            get => _totalActiveTimeToday;
            private set
            {
                if (_totalActiveTimeToday != value)
                {
                    _totalActiveTimeToday = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TotalActiveTimeTodayDisplay));
                    OnPropertyChanged(nameof(GrandTotalText));
                }
            }
        }

        public TimeSpan TotalIdleTimeToday
        {
            get => _totalIdleTimeToday;
            private set
            {
                if (_totalIdleTimeToday != value)
                {
                    _totalIdleTimeToday = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TotalIdleTimeTodayDisplay));
                    OnPropertyChanged(nameof(GrandTotalText));
                }
            }
        }

        public TimeSpan TotalLockedTimeToday
        {
            get => _totalLockedTimeToday;
            private set
            {
                if (_totalLockedTimeToday != value)
                {
                    _totalLockedTimeToday = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TotalLockedTimeTodayDisplay));
                    OnPropertyChanged(nameof(GrandTotalText));
                }
            }
        }

        public string TotalActiveTimeTodayDisplay => FormatTimeSpan(TotalActiveTimeToday);
        public string TotalIdleTimeTodayDisplay => FormatTimeSpan(TotalIdleTimeToday);
        public string TotalLockedTimeTodayDisplay => FormatTimeSpan(TotalLockedTimeToday);

        // Tracked-only active (for backward compat / chart data).
        public string SelectedDayActiveTrackedText => $"{TotalActiveTimeToday.ToHoursMinutes()}";
        public string SelectedDayManualTasksText => $"{_selectedDayManualDuration.ToHoursMinutes()}";

        // ── New unified day properties ─────────────────────────────────────────────

        // Active = tracked + manual.
        public string SelectedDayActiveText =>
            (TotalActiveTimeToday + _selectedDayManualDuration).ToHoursMinutes();

        // Leave credit in hours.
        public string SelectedDayLeaveHoursText => _selectedDayLeaveCredit.ToHoursMinutes();

        // TotalActive = tracked + manual only — deliberately excludes leave credit, matching
        // MonthlyTotalActiveText/WeeklyTotalActiveDuration's definition (see those properties'
        // comments) so Day/Week/Month "Total Active" figures agree for the same underlying
        // data. Leave is shown separately via SelectedDayLeaveHoursText.
        // SelectedDayStatusText computes its own Achieved/Remaining/Overworked progress
        // independently (Active + Leave) and is unaffected by this.
        public string SelectedDayTotalActiveText =>
            (TotalActiveTimeToday + _selectedDayManualDuration).ToHoursMinutes();

        public string SelectedDayStatusText
        {
            get
            {
                var summary = new HoursCalculationHelper.PeriodHoursSummary
                {
                    Expected = _selectedDayExpectedHours,
                    Active = TotalActiveTimeToday + _selectedDayManualDuration,
                    Leave = _selectedDayLeaveCredit
                };
                return summary.StatusText;
            }
        }

        public string SelectedDayIdleText => $"{TotalIdleTimeToday.ToHoursMinutes()}";
        public string SelectedDayLockedText => $"{TotalLockedTimeToday.ToHoursMinutes()}";

        public bool HasSelectedDayLeave => _hasSelectedDayLeave;

        public string SelectedDayLeaveSummaryText => _selectedDayLeaveSummaryText;

        public TimeSpan SelectedDayExpectedHours => _selectedDayExpectedHours;

        public string SelectedDayExpectedHoursText => _selectedDayExpectedHours.ToExpectedHoursText();

        public bool HasWeekLeave => _hasWeekLeave;

        public string WeekLeaveSummaryText => _weekLeaveSummaryText;

        public TimeSpan WeekExpectedHours => _weekExpectedHours;

        public string WeekExpectedHoursText => _weekExpectedHours.ToExpectedHoursText();

        public string SelectedDayStartText => _selectedDayStartTime.HasValue
            ? $"{_selectedDayStartTime.Value:HH:mm}"
            : "";

        public string SelectedDayEndText => _selectedDayEndTime.HasValue
            ? $"{_selectedDayEndTime.Value:HH:mm}"
            : "";

        // Activity Chart ViewModels
        public ActivityChartViewModel ActivityChartViewModel => _activityChartViewModel;
        public ActivityChartViewModel WeeklyActivityChartViewModel => _weeklyActivityChartViewModel;
        public ActivityChartViewModel MonthlyActivityChartViewModel => _monthlyActivityChartViewModel;

        private void UpdateActivityChart()
        {
            _activityChartViewModel.ReferenceTime = GetChartReferenceTime(_selectedDayExpectedHours);
            _activityChartViewModel.SetData(
                TotalActiveTimeToday,
                _selectedDayManualDuration,
                TotalIdleTimeToday,
                TotalLockedTimeToday
            );
        }

        private void UpdateWeeklyActivityChart()
        {
            _weeklyActivityChartViewModel.ReferenceTime = GetChartReferenceTime(_weekExpectedHours);
            _weeklyActivityChartViewModel.SetData(
                WeeklyTrackedActiveDuration,
                WeeklyManualDuration,
                WeeklyTotalIdleDuration,
                WeeklyTotalLockedDuration
            );
        }

        private void UpdateMonthlyActivityChart()
        {
            // Get data for the selected date in the month
            var selectedDate = SelectedMonthDate;
            TimeSpan activeTime = TimeSpan.Zero;
            TimeSpan idleTime = TimeSpan.Zero;
            TimeSpan lockedTime = TimeSpan.Zero;
            TimeSpan manualTime = TimeSpan.FromSeconds(GetManualSecondsForDate(selectedDate.Date));

            if (_activityLogReader.TryReadDay(selectedDate.Date, out var entries))
            {
                var totals = HoursCalculationHelper.SumActivityEntries(entries);
                activeTime = totals.Active;
                idleTime = totals.Idle;
                lockedTime = totals.Locked;
            }

            var leaveEntry = _leaveService.GetForDate(selectedDate.Date);
            _monthlyActivityChartViewModel.ReferenceTime = ExpectedHoursCalculator.GetDayExpectedHours(leaveEntry?.Duration);
            _monthlyActivityChartViewModel.SetData(activeTime, manualTime, idleTime, lockedTime);
        }

        public DateTime SelectedMonthDate
        {
            get => _selectedMonthDate;
            set
            {
                if (_selectedMonthDate != value)
                {
                    _selectedMonthDate = value;
                    OnPropertyChanged();
                    UpdateMonthlyActivityChart();
                }
            }
        }

        private DateTime _selectedMonthDate = DateTime.Today;

        public TimeSpan WeeklyTrackedActiveDuration
        {
            get => _weeklyTrackedActiveDuration;
            private set
            {
                if (_weeklyTrackedActiveDuration != value)
                {
                    _weeklyTrackedActiveDuration = value;
                    OnPropertyChanged();
                }
            }
        }

        public TimeSpan WeeklyManualDuration
        {
            get => _weeklyManualDuration;
            private set
            {
                if (_weeklyManualDuration != value)
                {
                    _weeklyManualDuration = value;
                    OnPropertyChanged();
                }
            }
        }

        public TimeSpan WeeklyTotalActiveDuration
        {
            get => _weeklyTotalActiveDuration;
            private set
            {
                if (_weeklyTotalActiveDuration != value)
                {
                    _weeklyTotalActiveDuration = value;
                    OnPropertyChanged();
                }
            }
        }

        public TimeSpan WeeklyTotalIdleDuration
        {
            get => _weeklyTotalIdleDuration;
            private set
            {
                if (_weeklyTotalIdleDuration != value)
                {
                    _weeklyTotalIdleDuration = value;
                    OnPropertyChanged();
                }
            }
        }

        public TimeSpan WeeklyTotalLockedDuration
        {
            get => _weeklyTotalLockedDuration;
            private set
            {
                if (_weeklyTotalLockedDuration != value)
                {
                    _weeklyTotalLockedDuration = value;
                    OnPropertyChanged();
                }
            }
        }

        public TimeSpan WeeklyLeaveDuration
        {
            get => _weeklyLeaveDuration;
            private set
            {
                if (_weeklyLeaveDuration != value)
                {
                    _weeklyLeaveDuration = value;
                    OnPropertyChanged();
                }
            }
        }

        public string WeeklyTrackedActiveText => $"{WeeklyTrackedActiveDuration.ToHoursMinutes()}";
        public string WeeklyManualText => $"{WeeklyManualDuration.ToHoursMinutes()}";
        public string WeeklyLeaveDurationText => $"{WeeklyLeaveDuration.ToHoursMinutes()}";
        public string WeeklyTotalActiveText => $"{WeeklyTotalActiveDuration.ToHoursMinutes()}";
        public string WeeklyTotalIdleText => $"{WeeklyTotalIdleDuration.ToHoursMinutes()}";
        public string WeeklyTotalLockedText => $"{WeeklyTotalLockedDuration.ToHoursMinutes()}";

        // ── New unified week panel properties (used in UIAShell sidebar) ─────────────

        // Active = tracked + manual (all actual work).
        public string WeekActiveText =>
            (WeeklyTrackedActiveDuration + WeeklyManualDuration).ToHoursMinutes();

        public string WeekLeaveHoursText => WeeklyLeaveDuration.ToHoursMinutes();

        // TotalActive = tracked + manual + leave credit — see WeeklyTotalActiveDuration's comment.
        public string WeekTotalActiveText => WeeklyTotalActiveDuration.ToHoursMinutes();

        public string WeekStatusText
        {
            get
            {
                var summary = new HoursCalculationHelper.PeriodHoursSummary
                {
                    Expected = _weekExpectedHours,
                    Active = WeeklyTotalActiveDuration,
                    Leave = TimeSpan.Zero
                };
                return summary.StatusText;
            }
        }

        private void SaveSettings()
        {
            _settingsSnapshot.IdleThresholdMinutes = IdleThresholdMinutes;
            _settingsSnapshot.PollIntervalSeconds = PollIntervalSeconds;
            _settingsSnapshot.EnableLiveRefresh = EnableLiveRefresh;
            _settingsSnapshot.LiveRefreshIntervalSeconds = LiveRefreshIntervalSeconds;

            _settingsSnapshot.CrashLogRetentionDays = Clamp(CrashLogRetentionDays, 1, 365);
            _settingsSnapshot.CrashLogMaxSizeMB = Clamp(CrashLogMaxSizeMB, 1, 2048);
            CrashLogRetentionDays = _settingsSnapshot.CrashLogRetentionDays;
            CrashLogMaxSizeMB = _settingsSnapshot.CrashLogMaxSizeMB;

            _settingsService?.Save(_settingsSnapshot);
            _trackingService?.ApplySettings(_settingsSnapshot);
            ApplyLiveRefreshSettings();

            if (System.Windows.Application.Current is App app && app.CloseTrackingService != null)
            {
                app.CloseTrackingService.ApplyCrashLogPolicy(_settingsSnapshot.CrashLogRetentionDays, _settingsSnapshot.CrashLogMaxSizeMB);
                app.CloseTrackingService.CleanupCrashLogs();
            }

            LastCrash.Load();

            RefreshCommand.Execute(null);
        }

        private void ClearCrashLogs()
        {
            if (System.Windows.Application.Current is App app && app.CloseTrackingService != null)
            {
                app.CloseTrackingService.ClearCrashLogs();
            }

            LastCrash.Load();
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private bool CanEditManualTasks()
        {
            return true; // Manual tasks can always be edited now that they have their own tab
        }

        private void LoadManualTasksForSelectedDate()
        {
            _manualTasks.Clear();
            foreach (var item in _manualTaskService.Load(ManualTasksDate.Date))
            {
                _manualTasks.Add(item);
            }

            ResetManualEditState();
            NotifyManualTotalsChanged();
        }

        private void PersistManualTasks()
        {
            _manualTaskService.Save(ManualTasksDate.Date, _manualTasks.ToList());

            // Update cached manual duration immediately so Total Active reflects edits without reopening.
            _selectedDayManualDuration = TimeSpan.FromSeconds(_manualTasks.Sum(t => Math.Max(0, t.TotalSeconds)));

            RefreshDate(ManualTasksDate.Date);
            RefreshTimeline();
        }

        #region Timeline Methods

        private void RefreshTimeline()
        {
            switch (TimelineViewMode)
            {
                case TimelineViewMode.Date:
                    LoadTimelineForDate();
                    break;
                case TimelineViewMode.Week:
                    LoadTimelineForWeek();
                    break;
                case TimelineViewMode.Month:
                    LoadTimelineForMonth();
                    break;
            }
        }

        private void NavigatePrevious()
        {
            TimelineCurrentDate = TimelineViewMode switch
            {
                TimelineViewMode.Date => TimelineCurrentDate.AddDays(-1),
                TimelineViewMode.Week => TimelineCurrentDate.AddDays(-7),
                TimelineViewMode.Month => TimelineCurrentDate.AddMonths(-1),
                _ => TimelineCurrentDate
            };
        }

        private void NavigateNext()
        {
            TimelineCurrentDate = TimelineViewMode switch
            {
                TimelineViewMode.Date => TimelineCurrentDate.AddDays(1),
                TimelineViewMode.Week => TimelineCurrentDate.AddDays(7),
                TimelineViewMode.Month => TimelineCurrentDate.AddMonths(1),
                _ => TimelineCurrentDate
            };
        }

        private void NavigateToManualTasksFromCalendar(CalendarDayItemBase? dayItem)
        {
            if (dayItem == null || dayItem.IsWeeklySummary)
            {
                return;
            }

            if (!HasManualTasksForDate(dayItem.Date))
            {
                return;
            }

            NavigateToManualTasksForDate(dayItem.Date);
        }

        public void NavigateToManualTasksForDate(DateTime date)
        {
            var normalized = date.Date;
            ResetManualEditState();

            SelectedTabIndex = (int)MainWindowTab.ManualTasks;
            _timelineViewMode = TimelineViewMode.Date;
            OnPropertyChanged(nameof(TimelineViewMode));
            OnPropertyChanged(nameof(PeriodLabelFormatted));
            OnPropertyChanged(nameof(ShowDateViewEmptyState));
            OnPropertyChanged(nameof(ShowTimelineViewEmptyState));

            SetManualTasksDateInternal(normalized, syncTimeline: false);
            SetTimelineCurrentDateInternal(normalized, syncManualTasksDate: false);
            RefreshTimeline();
        }

        public bool HasManualTasksForDate(DateTime date)
        {
            var tasks = _manualTaskService.Load(date.Date);
            return tasks.Any();
        }

        private void LoadTimelineForDate()
        {
            _timelineItems.Clear();
            var date = TimelineCurrentDate.Date;
            var tasks = _manualTaskService.Load(date);
            
            if (tasks.Any())
            {
                var group = new TimelineDateGroup
                {
                    Date = date,
                    TotalDuration = TimeSpan.FromSeconds(tasks.Sum(t => Math.Max(0, t.TotalSeconds)))
                };
                foreach (var task in tasks)
                {
                    group.Tasks.Add(task);
                }
                _timelineItems.Add(group);
            }
        }

        private void LoadTimelineForWeek()
        {
            _timelineItems.Clear();
            var startOfWeek = WorkWeekHelper.GetWeekStartMonday(TimelineCurrentDate);
            
            for (int i = 0; i < 7; i++)
            {
                var date = startOfWeek.AddDays(i);
                var tasks = _manualTaskService.Load(date);
                
                if (tasks.Any())
                {
                    var group = new TimelineDateGroup
                    {
                        Date = date,
                        TotalDuration = TimeSpan.FromSeconds(tasks.Sum(t => Math.Max(0, t.TotalSeconds)))
                    };
                    foreach (var task in tasks)
                    {
                        group.Tasks.Add(task);
                    }
                    _timelineItems.Add(group);
                }
            }
        }

        private void LoadTimelineForMonth()
        {
            _timelineItems.Clear();
            var year = TimelineCurrentDate.Year;
            var month = TimelineCurrentDate.Month;
            var daysInMonth = DateTime.DaysInMonth(year, month);
            
            for (int day = 1; day <= daysInMonth; day++)
            {
                var date = new DateTime(year, month, day);
                var tasks = _manualTaskService.Load(date);
                
                if (tasks.Any())
                {
                    var group = new TimelineDateGroup
                    {
                        Date = date,
                        TotalDuration = TimeSpan.FromSeconds(tasks.Sum(t => Math.Max(0, t.TotalSeconds)))
                    };
                    foreach (var task in tasks)
                    {
                        group.Tasks.Add(task);
                    }
                    _timelineItems.Add(group);
                }
            }
        }

        #endregion

        private static int ParseNonNegativeInt(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0;
            }

            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return 0;
            }

            return parsed < 0 ? 0 : parsed;
        }

        private static int NormalizeToTotalSeconds(int hours, int minutes, int seconds)
        {
            long total = 0;
            total += (long)Math.Max(0, hours) * 3600L;
            total += (long)Math.Max(0, minutes) * 60L;
            total += (long)Math.Max(0, seconds);
            if (total < 0) total = 0;
            if (total > int.MaxValue) total = int.MaxValue;
            return (int)total;
        }

        private void PrimaryManualTaskAction()
        {
            if (!CanEditManualTasks())
            {
                return;
            }

            if (IsManualEditMode)
            {
                UpdateManualTask();
                return;
            }

            if (string.IsNullOrWhiteSpace(ManualTaskName))
            {
                return;
            }

            int hours = ParseNonNegativeInt(ManualHours);
            int minutes = ParseNonNegativeInt(ManualMinutes);
            int seconds = ParseNonNegativeInt(ManualSeconds);

            int totalSeconds = NormalizeToTotalSeconds(hours, minutes, seconds);

            var entry = new ManualTaskEntry
            {
                TaskName = ManualTaskName.Trim(),
                TotalSeconds = totalSeconds
            };

            _manualTasks.Add(entry);

            ManualTaskName = string.Empty;
            ManualHours = string.Empty;
            ManualMinutes = string.Empty;
            ManualSeconds = string.Empty;

            PersistManualTasks();
            UpdateManualTaskCommandsCanExecute();
        }

        private void BeginEditManualTask(ManualTaskEntry? entry)
        {
            if (!CanEditManualTasks() || entry == null)
            {
                return;
            }

            // For Week/Month view: switch to the task's actual date and load its collection
            // Find the date by checking if entry is in timeline or using ManualTasksDate for Date view
            DateTime taskDate = ManualTasksDate;
            bool foundInTimeline = false;
            
            // Search in timeline items to find the task's actual date
            foreach (var group in _timelineItems)
            {
                if (group.Tasks.Any(t => t.Id == entry.Id))
                {
                    taskDate = group.Date;
                    foundInTimeline = true;
                    break;
                }
            }

            // If task is from a different date than currently loaded, switch to that date
            if (foundInTimeline && taskDate != ManualTasksDate.Date)
            {
                ManualTasksDate = taskDate;
                LoadManualTasksForSelectedDate();
            }

            // Find the matching task instance in _manualTasks by Id
            var matchingTask = _manualTasks.FirstOrDefault(t => t.Id == entry.Id);
            if (matchingTask != null)
            {
                SelectedManualTask = matchingTask;
                IsManualEditMode = true;
            }
        }

        private void CancelManualTaskEdit()
        {
            IsManualEditMode = false;
            SelectedManualTask = null;
            ManualTaskName = string.Empty;
            ManualHours = string.Empty;
            ManualMinutes = string.Empty;
            ManualSeconds = string.Empty;
        }

        private void UpdateManualTask()
        {
            if (!CanEditManualTasks() || SelectedManualTask == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(ManualTaskName))
            {
                return;
            }

            int hours = ParseNonNegativeInt(ManualHours);
            int minutes = ParseNonNegativeInt(ManualMinutes);
            int seconds = ParseNonNegativeInt(ManualSeconds);

            int totalSeconds = NormalizeToTotalSeconds(hours, minutes, seconds);

            SelectedManualTask.TaskName = ManualTaskName.Trim();
            SelectedManualTask.TotalSeconds = totalSeconds;
            PersistManualTasks();
            CancelManualTaskEdit();
        }

        private void DeleteManualTaskRow(ManualTaskEntry? entry)
        {
            if (!CanEditManualTasks() || entry == null)
            {
                return;
            }

            if (_isManualEditMode)
            {
                CancelManualTaskEdit();
            }

            bool wasSelected = SelectedManualTask != null && SelectedManualTask.Id == entry.Id;

            var taskDate = ResolveManualTaskDate(entry);
            if (!RemoveManualTaskById(taskDate, entry.Id))
            {
                return;
            }

            if (wasSelected)
            {
                CancelManualTaskEdit();
            }

            RefreshTimeline();
            UpdateManualTaskCommandsCanExecute();
        }

        private DateTime ResolveManualTaskDate(ManualTaskEntry entry)
        {
            foreach (var group in _timelineItems)
            {
                if (group.Tasks.Any(t => t.Id == entry.Id))
                {
                    return group.Date;
                }
            }

            if (_manualTasks.Any(t => t.Id == entry.Id))
            {
                return ManualTasksDate.Date;
            }

            return ManualTasksDate.Date;
        }

        private bool RemoveManualTaskById(DateTime taskDate, Guid entryId)
        {
            var tasksForDate = _manualTaskService.Load(taskDate);
            var matchingTask = tasksForDate.FirstOrDefault(t => t.Id == entryId);
            if (matchingTask == null)
            {
                return false;
            }

            tasksForDate.Remove(matchingTask);
            _manualTaskService.Save(taskDate, tasksForDate);

            if (ManualTasksDate.Date == taskDate)
            {
                var localMatch = _manualTasks.FirstOrDefault(t => t.Id == entryId);
                if (localMatch != null)
                {
                    _manualTasks.Remove(localMatch);
                }

                _selectedDayManualDuration = TimeSpan.FromSeconds(_manualTasks.Sum(t => Math.Max(0, t.TotalSeconds)));
            }

            RefreshDate(taskDate);
            return true;
        }

        private void UpdateManualTaskCommandsCanExecute()
        {
            if (PrimaryManualTaskCommand is RelayCommand primary)
            {
                primary.RaiseCanExecuteChanged();
            }
            if (BeginEditManualTaskCommand is RelayCommand beginEdit)
            {
                beginEdit.RaiseCanExecuteChanged();
            }
            if (CancelManualTaskEditCommand is RelayCommand cancel)
            {
                cancel.RaiseCanExecuteChanged();
            }
            if (DeleteManualTaskRowCommand is RelayCommand del)
            {
                del.RaiseCanExecuteChanged();
            }
        }

        private void RefreshTodaySummary()
        {
            TotalActiveTimeToday = TimeSpan.Zero;
            TotalIdleTimeToday = TimeSpan.Zero;
            TotalLockedTimeToday = TimeSpan.Zero;
            _todayAppUsage.Clear();
            _selectedDayStartTime = null;
            _selectedDayEndTime = null;
            _selectedDayManualDuration = TimeSpan.Zero;

            if (!_activityLogReader.TryReadDay(SelectedDate.Date, out var entries))
            {
                RefreshSelectedDayManualDuration();
                NotifySelectedDaySummaryTextsChanged();
                OnPropertyChanged(nameof(ShowSelectedDayChart));
                return;
            }

            var perProcessDurations = new System.Collections.Generic.Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in entries)
            {
                var start = entry.StartTime;
                var end = entry.EndTime;

                if (!_selectedDayStartTime.HasValue || start < _selectedDayStartTime.Value)
                {
                    _selectedDayStartTime = start;
                }

                if (!_selectedDayEndTime.HasValue || end > _selectedDayEndTime.Value)
                {
                    _selectedDayEndTime = end;
                }

                var duration = end - start;

                if (entry.IsLocked)
                {
                    TotalLockedTimeToday += duration;
                }
                else if (entry.IsIdle)
                {
                    TotalIdleTimeToday += duration;
                }
                else
                {
                    TotalActiveTimeToday += duration;

                    string processName = entry.ProcessName;
                    if (!string.IsNullOrEmpty(processName))
                    {
                        if (perProcessDurations.TryGetValue(processName, out var existing))
                        {
                            perProcessDurations[processName] = existing + duration;
                        }
                        else
                        {
                            perProcessDurations[processName] = duration;
                        }
                    }
                }
            }

            foreach (var kvp in perProcessDurations.OrderByDescending(kvp => kvp.Value))
            {
                _todayAppUsage.Add(new AppUsageSummary
                {
                    ProcessName = kvp.Key,
                    ActiveDuration = kvp.Value
                });
            }

            // Ensure labeled summary text updates even if totals did not change
            RefreshSelectedDayManualDuration();
            NotifySelectedDaySummaryTextsChanged();
            OnPropertyChanged(nameof(ShowSelectedDayChart));
        }

        private void RefreshSelectedDaySummary()
        {
            RefreshTodaySummary();
        }

        private void RefreshSelectedDayAppUsage()
        {
            RefreshTodaySummary();
        }

        private void RefreshWeeklySummary()
        {
            LoadWeeklySummary();
        }

        // TEMP DIAGNOSTIC: seconds appended (":SS") to help pinpoint a reported minute-level
        // mismatch between Day/Week/Month/Week Report totals. Revert to the 2-arg "{0:00}:
        // {1:00}" format once confirmed.
        private static string FormatTimeSpan(TimeSpan value)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", (int)value.TotalHours, value.Minutes, value.Seconds);
        }

        private DailySummary BuildDailySummaryForDate(DateTime date)
        {
            var (active, idle, locked) = GetActivityTotalsForDate(date);
            var manual = TimeSpan.FromSeconds(GetManualSecondsForDate(date));
            var leaveEntry = _leaveService.GetForDate(date.Date);
            var leaveCredit = ExpectedHoursCalculator.GetDayLeaveCredit(date, leaveEntry?.Duration);
            return new DailySummary
            {
                Date = date.Date,
                ActiveDuration = active,
                ManualTaskDuration = manual,
                IdleDuration = idle,
                LockedDuration = locked,
                LeaveCredit = leaveCredit,
                IsHoliday = _holidayService.IsHoliday(date)
            };
        }

        private void RefreshWeeklySummaryDay(DateTime date)
        {
            var normalized = date.Date;
            var dayIndex = (normalized - SelectedWeekStart.Date).Days;
            if (dayIndex < 0 || dayIndex > 6)
            {
                return;
            }

            if (_weeklySummaries.Count != 7)
            {
                LoadWeeklySummary();
                return;
            }

            var summary = _weeklySummaries[dayIndex];
            if (summary.Date.Date != normalized)
            {
                LoadWeeklySummary();
                return;
            }

            var (active, idle, locked) = GetActivityTotalsForDate(normalized);
            var manual = TimeSpan.FromSeconds(GetManualSecondsForDate(normalized));
            var leaveEntry = _leaveService.GetForDate(normalized);
            var leaveCredit = ExpectedHoursCalculator.GetDayLeaveCredit(normalized, leaveEntry?.Duration);
            summary.SetDurations(active, manual, idle, locked, leaveCredit, _holidayService.IsHoliday(normalized));
        }

        private void RecalculateWeeklyTotals()
        {
            WeeklyTrackedActiveDuration = TimeSpan.FromTicks(_weeklySummaries.Sum(d => d.ActiveDuration.Ticks));
            WeeklyManualDuration = TimeSpan.FromTicks(_weeklySummaries.Sum(d => d.ManualTaskDuration.Ticks));
            WeeklyLeaveDuration = TimeSpan.FromTicks(_weeklySummaries.Sum(d => d.LeaveCredit.Ticks));
            // tracked + manual + leave credit, minus any hours that fell on a holiday date
            // (_weekHolidayWorkedCredit) — matching MonthlyTotalActiveText's definition (see
            // that property's comment) so the Week Report panel's total agrees with Month
            // View's weekly Burnt Hours pill and the Monthly Usage header for the same days.
            // A leave day contributes its credited hours (8h full-day / 4h half-day) even
            // with no tracked activity, stacking with any hours actually worked that same
            // day; a holiday day contributes nothing at all, since it's never an expected
            // work day. Leave is still shown separately via WeeklyLeaveDuration/
            // WeekLeaveHoursText. WeekStatusText reads this same total so the two can never
            // drift apart.
            var weekHolidayWorkedCredit = TimeSpan.FromTicks(_weeklySummaries
                .Where(d => d.IsHoliday)
                .Sum(d => (d.ActiveDuration + d.ManualTaskDuration + d.LeaveCredit).Ticks));
            var weeklyTotalActive = WeeklyTrackedActiveDuration + WeeklyManualDuration + WeeklyLeaveDuration - weekHolidayWorkedCredit;
            WeeklyTotalActiveDuration = weeklyTotalActive < TimeSpan.Zero ? TimeSpan.Zero : weeklyTotalActive;
            WeeklyTotalIdleDuration = TimeSpan.FromTicks(_weeklySummaries.Sum(d => d.IdleDuration.Ticks));
            WeeklyTotalLockedDuration = TimeSpan.FromTicks(_weeklySummaries.Sum(d => d.LockedDuration.Ticks));

            OnPropertyChanged(nameof(WeeklyTrackedActiveText));
            OnPropertyChanged(nameof(WeeklyManualText));
            OnPropertyChanged(nameof(WeeklyLeaveDurationText));
            OnPropertyChanged(nameof(WeeklyTotalActiveText));
            OnPropertyChanged(nameof(WeeklyTotalIdleText));
            OnPropertyChanged(nameof(WeeklyTotalLockedText));
            OnPropertyChanged(nameof(WeekActiveText));
            OnPropertyChanged(nameof(WeekLeaveHoursText));
            OnPropertyChanged(nameof(WeekTotalActiveText));
            OnPropertyChanged(nameof(WeekStatusText));

            if (!IsSelectedWeekInFuture)
            {
                UpdateWeeklyActivityChart();
            }
        }

        public void LoadWeeklySummary()
        {
            _weeklySummaries.Clear();

            foreach (var date in WorkWeekHelper.EnumerateWeekDays(SelectedWeekStart.Date))
            {
                _weeklySummaries.Add(BuildDailySummaryForDate(date));
            }

            RefreshWeekExpectedHours();
            RecalculateWeeklyTotals();
        }

        private void ApplyLiveRefreshSettings()
        {
            if (_autoRefreshTimer == null)
            {
                return;
            }

            int interval = LiveRefreshIntervalSeconds;
            if (interval <= 0)
            {
                interval = 30;
                _liveRefreshIntervalSeconds = interval;
                OnPropertyChanged(nameof(LiveRefreshIntervalSeconds));
            }

            _autoRefreshTimer.Interval = TimeSpan.FromSeconds(interval);

            if (!EnableLiveRefresh || _trackingService == null || !_trackingService.IsRunning || SelectedDate.Date != DateTime.Today)
            {
                _autoRefreshTimer.Stop();
                return;
            }

            _autoRefreshTimer.Start();
        }

        #region Leave Management

        private static string FormatLeaveDuration(LeaveDuration duration) => duration switch
        {
            LeaveDuration.FullDay => "Full day",
            LeaveDuration.MorningHalf => "Morning half",
            LeaveDuration.AfternoonHalf => "Afternoon half",
            _ => duration.ToString()
        };

        private static string FormatLeaveType(LeaveType type) => type switch
        {
            LeaveType.SickLeave => "Sick Leave",
            LeaveType.CasualLeave => "Casual Leave",
            LeaveType.EarnedLeave => "Earned Leave",
            LeaveType.CompOff => "Comp Off",
            _ => type.ToString()
        };

        private bool CanEditLeave() => true;

        private void LoadLeavesForSelectedMonth()
        {
            _leaveCalendarDays.Clear();
            _leaveMonthEntries = _leaveService.LoadMonth(_leaveSelectedYear, _leaveSelectedMonth);
            var leaveByDate = _leaveMonthEntries.ToDictionary(e => e.Date.Date);

            var start = new DateTime(_leaveSelectedYear, _leaveSelectedMonth, 1);
            var (calendarStart, calendarEnd) = WorkWeekHelper.GetMonthCalendarGridRange(_leaveSelectedYear, _leaveSelectedMonth);

            for (var date = calendarStart; date <= calendarEnd; date = date.AddDays(1))
            {
                leaveByDate.TryGetValue(date.Date, out var entry);
                _leaveCalendarDays.Add(new LeaveCalendarDayItem
                {
                    Date = date,
                    IsCurrentMonth = date.Month == _leaveSelectedMonth && date.Year == _leaveSelectedYear,
                    HasLeave = entry != null,
                    LeaveDuration = entry?.Duration,
                    LeaveType = entry?.Type,
                    LeaveSummaryText = entry == null
                        ? string.Empty
                        : $"{FormatLeaveDuration(entry.Duration)} · {FormatLeaveType(entry.Type)}"
                });
            }
        }

        private void NavigateLeaveMonth(int deltaMonths)
        {
            var next = LeaveSelectedMonthYear.AddMonths(deltaMonths);
            LeaveSelectedMonthYear = next;
        }

        private void SelectLeaveCalendarDay(LeaveCalendarDayItem? day)
        {
            if (day == null || !day.IsCurrentMonth)
            {
                return;
            }

            LeaveFormDate = day.Date;
            if (day.HasLeave)
            {
                var entry = _leaveMonthEntries.FirstOrDefault(e => e.Date.Date == day.Date.Date);
                if (entry != null)
                {
                    SelectedLeaveEntry = entry;
                    LeaveFormDuration = entry.Duration;
                    LeaveFormType = entry.Type;
                    IsLeaveEditMode = true;
                }
            }
            else
            {
                CancelLeaveEdit();
                LeaveFormDate = day.Date;
            }
        }

        private LeaveEntry? SelectedLeaveEntry
        {
            get => _selectedLeaveEntry;
            set
            {
                if (!ReferenceEquals(_selectedLeaveEntry, value))
                {
                    _selectedLeaveEntry = value;
                    OnPropertyChanged();
                    UpdateLeaveCommandsCanExecute();
                }
            }
        }

        private void SaveLeaveAction()
        {
            if (!CanEditLeave())
            {
                return;
            }

            var date = LeaveFormDate.Date;
            var affectedDates = new List<DateTime> { date };
            if (IsLeaveEditMode && SelectedLeaveEntry != null)
            {
                var existing = _leaveMonthEntries.FirstOrDefault(e => e.Id == SelectedLeaveEntry.Id);
                if (existing != null)
                {
                    var previousDate = existing.Date.Date;
                    existing.Date = date;
                    existing.Duration = LeaveFormDuration;
                    existing.Type = LeaveFormType;
                    if (previousDate != date)
                    {
                        affectedDates.Add(previousDate);
                    }
                }
            }
            else
            {
                if (_leaveMonthEntries.Any(e => e.Date.Date == date))
                {
                    return;
                }

                _leaveMonthEntries.Add(new LeaveEntry
                {
                    Date = date,
                    Duration = LeaveFormDuration,
                    Type = LeaveFormType
                });
            }

            PersistLeaveMonth(affectedDates);
            CancelLeaveEdit();
        }

        private void DeleteLeaveAction()
        {
            if (!CanEditLeave() || SelectedLeaveEntry == null)
            {
                return;
            }

            var entry = _leaveMonthEntries.FirstOrDefault(e => e.Id == SelectedLeaveEntry.Id);
            if (entry != null)
            {
                var affectedDate = entry.Date.Date;
                _leaveMonthEntries.Remove(entry);
                PersistLeaveMonth(new[] { affectedDate });
            }

            CancelLeaveEdit();
        }

        private void CancelLeaveEdit()
        {
            IsLeaveEditMode = false;
            SelectedLeaveEntry = null;
            LeaveFormDuration = LeaveDuration.FullDay;
            LeaveFormType = LeaveType.SickLeave;
        }

        private void PersistLeaveMonth(IEnumerable<DateTime>? affectedDates = null)
        {
            _leaveService.SaveMonth(_leaveSelectedYear, _leaveSelectedMonth, _leaveMonthEntries);
            LoadLeavesForSelectedMonth();

            if (affectedDates == null)
            {
                return;
            }

            foreach (var date in affectedDates.Select(d => d.Date).Distinct())
            {
                RefreshLeaveSurfacesForDate(date);
            }
        }

        private void UpdateLeaveCommandsCanExecute()
        {
            if (SaveLeaveCommand is RelayCommand save)
            {
                save.RaiseCanExecuteChanged();
            }
            if (DeleteLeaveCommand is RelayCommand delete)
            {
                delete.RaiseCanExecuteChanged();
            }
            if (CancelLeaveEditCommand is RelayCommand cancel)
            {
                cancel.RaiseCanExecuteChanged();
            }
        }

        #endregion

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private class RelayCommand : ICommand
        {
            private readonly Action<object?> _execute;
            private readonly Func<object?, bool>? _canExecute;

            public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
            {
                _execute = execute ?? throw new ArgumentNullException(nameof(execute));
                _canExecute = canExecute;
            }

            public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

            public void Execute(object? parameter) => _execute(parameter);

            public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();

            public event EventHandler? CanExecuteChanged
            {
                add { CommandManager.RequerySuggested += value; }
                remove { CommandManager.RequerySuggested -= value; }
            }
        }
    }
}
