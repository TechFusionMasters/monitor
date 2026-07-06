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

        // Leave credit derived from the already-applied LeaveDuration (Mon–Fri only).
        public TimeSpan LeaveCredit =>
            ExpectedHoursCalculator.GetDayLeaveCredit(Date, LeaveDuration);

        DateTime HoursCalculationHelper.MonthlyDayItemLike.Date => Date;
        bool HoursCalculationHelper.MonthlyDayItemLike.IsCurrentMonth => IsCurrentMonth;
        TimeSpan HoursCalculationHelper.MonthlyDayItemLike.TrackedActive => TotalActive;
        TimeSpan HoursCalculationHelper.MonthlyDayItemLike.Manual => ManualTime;
        TimeSpan HoursCalculationHelper.MonthlyDayItemLike.LeaveCredit => LeaveCredit;

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
        }

        public string LeaveBadgeText => LeaveDuration switch
        {
            Models.LeaveDuration.FullDay => "Leave",
            Models.LeaveDuration.MorningHalf => "½ AM",
            Models.LeaveDuration.AfternoonHalf => "½ PM",
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

    // Weekly Summary Data Model for Calendar Grid
    public class WeeklySummaryDayItem : CalendarDayItemBase, INotifyPropertyChanged
    {
        private int _weekNumber;
        private DateTime _date = DateTime.MinValue;
        private bool _isCurrentMonth = true;
        private TimeSpan _totalActiveHours;
        
        public override int WeekNumber { get => _weekNumber; set => _weekNumber = value; }
        public TimeSpan TotalActiveHours
        {
            get => _totalActiveHours;
            set
            {
                if (_totalActiveHours == value)
                {
                    return;
                }

                _totalActiveHours = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TotalActiveText));
                OnPropertyChanged(nameof(HasData));
            }
        }
        public string TotalActiveText => $"{(int)TotalActiveHours.TotalHours}h {TotalActiveHours.Minutes}m";
        public override bool IsWeeklySummary => true;
        public override bool IsCurrentMonth { get => _isCurrentMonth; set => _isCurrentMonth = value; }
        public override bool IsWeekend => false;
        public override bool HasData => TotalActiveHours > TimeSpan.Zero;
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
        private string _weekLeaveSummaryText = string.Empty;
        private bool _hasWeekLeave;
        private TimeSpan _weeklyLeaveDuration;
        private TimeSpan _monthLeaveCredit = TimeSpan.Zero;
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
        private DateTime? _headerActiveStartLocal;
        private DateTime? _headerActiveLastRecordStartLocal;
        private int _lastDisplayedActiveSecond = -1;
        private string _headerActiveTimerText = "00:00:00";
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
            AppUsageBreakdownViewModel? appUsageBreakdownViewModel = null)
        {
            _trackingService = trackingService;
            _settingsService = settingsService;
            _activityLogReader = activityLogReader ?? new ActivityLogReader();
            _manualTaskService = manualTaskService ?? new ManualTaskService();
            _leaveService = leaveService ?? new LeaveService();

            LastCrash = lastCrashViewModel ?? new LastCrashViewModel();
            Holidays = holidaysViewModel ?? new HolidaysViewModel();
            WorkSummary = workSummaryViewModel ?? new WorkSummaryViewModel(_activityLogReader, _manualTaskService, _leaveService);
            AppCategories = appCategoriesViewModel ?? new AppCategoriesViewModel(activityLogReader: _activityLogReader);
            AppUsageBreakdown = appUsageBreakdownViewModel ?? new AppUsageBreakdownViewModel(_activityLogReader);
            AppCategories.CategoriesSaved += (_, _) => AppUsageBreakdown.Refresh();
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
            _headerActiveBase = ComputeActiveTotalForDate(DateTime.Today);

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
        }

        private void SyncHeaderActiveBaseFromSummary()
        {
            _headerActiveBase = ComputeActiveTotalForDate(DateTime.Today);
            _headerActiveStartLocal = null;
            _headerActiveLastRecordStartLocal = null;
            _lastDisplayedActiveSecond = -1;
            RefreshHeaderActiveTimer();
        }

        private TimeSpan ComputeActiveTotalForDate(DateTime date)
        {
            if (!_activityLogReader.TryReadDay(date.Date, out var entries))
            {
                return TimeSpan.Zero;
            }

            return HoursCalculationHelper.SumActiveOnly(entries);
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
        }

        private void NotifyMonthHeaderTotalsChanged()
        {
            OnPropertyChanged(nameof(MonthlyActiveTrackedText));
            OnPropertyChanged(nameof(MonthlyManualTasksText));
            OnPropertyChanged(nameof(MonthlyTotalActiveText));
            OnPropertyChanged(nameof(MonthlyExpectedText));
            OnPropertyChanged(nameof(MonthlyActiveText));
            OnPropertyChanged(nameof(MonthlyLeaveText));
            OnPropertyChanged(nameof(MonthlyStatusText));
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

                NotifyMonthHeaderTotalsChanged();
            }

            NotifyManualTotalsChanged();
        }

        private void RefreshMonthSurfacesForDate(DateTime date)
        {
            if (!IsDateInSelectedMonth(date))
            {
                return;
            }

            RefreshMonthDayCell(date);
            RefreshMonthWeekSummary(WorkWeekHelper.GetIsoWeekNumber(date));
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
                _monthLeaveCredit = TimeSpan.FromSeconds(
                    _monthlyCalendarDays
                        .OfType<MonthlyDayItem>()
                        .Where(d => d.IsCurrentMonth)
                        .Sum(d => d.LeaveCredit.TotalSeconds));
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

            // Expected is always 40h — leave does NOT reduce it.
            _weekExpectedHours = ExpectedHoursCalculator.GetWeekExpectedHours();
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
            var weekTotal = HoursCalculationHelper.SumInMonthWeekTotalActive(
                _monthlyCalendarDays.OfType<MonthlyDayItem>(),
                weekNumber,
                _selectedYear,
                _selectedMonth);

            foreach (var summary in _monthlyCalendarDays.OfType<WeeklySummaryDayItem>().Where(s => s.WeekNumber == weekNumber))
            {
                summary.TotalActiveHours = weekTotal;
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
            _monthlyAppUsage.Clear();
            _monthlyCalendarDays.Clear();
            _monthlyWeeklySummaries.Clear();

            var monthLeaves = _leaveService.LoadMonth(_selectedYear, _selectedMonth);
            var leaveByDate = monthLeaves.ToDictionary(l => l.Date.Date);

            var start = new DateTime(_selectedYear, _selectedMonth, 1);
            var end = start.AddMonths(1).AddDays(-1);

            var (calendarStart, calendarEnd) = WorkWeekHelper.GetMonthCalendarGridRange(_selectedYear, _selectedMonth);

            var perProcess = new System.Collections.Generic.Dictionary<string, (TimeSpan Active, TimeSpan Idle, TimeSpan Locked)>(StringComparer.OrdinalIgnoreCase);

            // First pass: collect per-process data for the month
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

            // Populate monthly app usage
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

                    dayItem.ApplyActivityData(activeTime, idleTime, lockedTime, manualTime, hasManualTasks);
                }

                _monthlyCalendarDays.Add(dayItem);
            }

            // Calculate weekly summaries: TotalActive = tracked + manual + leave (in-month days only).
            var weeklyGroups = _monthlyCalendarDays
                .Where(d => d.IsCurrentMonth && d is MonthlyDayItem)
                .Cast<MonthlyDayItem>()
                .GroupBy(d => d.WeekNumber)
                .OrderBy(g => g.Key)
                .ToDictionary(g => g.Key, g => TimeSpan.FromSeconds(
                    g.Sum(d => (d.TotalActive + d.ManualTime + d.LeaveCredit).TotalSeconds)));

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
                            if (weeklyGroups.TryGetValue(currentWeekNumber, out var weekTotal))
                            {
                                var summaryItem = new WeeklySummaryDayItem
                                {
                                    WeekNumber = currentWeekNumber,
                                    TotalActiveHours = weekTotal
                                };
                                finalCalendarItems.Add(summaryItem);
                            }
                            
                            // Fill remaining slots in the week if needed
                            while (currentWeekItems.Count < 8)
                            {
                                currentWeekItems.Add(new WeeklySummaryDayItem { WeekNumber = currentWeekNumber, TotalActiveHours = TimeSpan.Zero });
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
                if (weeklyGroups.TryGetValue(currentWeekNumber, out var weekTotal))
                {
                    var summaryItem = new WeeklySummaryDayItem
                    {
                        WeekNumber = currentWeekNumber,
                        TotalActiveHours = weekTotal
                    };
                    finalCalendarItems.Add(summaryItem);
                }
                
                // Fill remaining slots in the last week if needed
                while (currentWeekItems.Count < 8)
                {
                    currentWeekItems.Add(new WeeklySummaryDayItem { WeekNumber = currentWeekNumber, TotalActiveHours = TimeSpan.Zero });
                }
            }

            // Clear and rebuild the calendar collection with proper layout
            _monthlyCalendarDays.Clear();
            foreach (var item in finalCalendarItems)
            {
                _monthlyCalendarDays.Add(item);
            }

            // Compute monthly leave credit from in-month Mon–Fri leave entries.
            var monthLeaveLookup = monthLeaves.ToDictionary(l => l.Date.Date);
            _monthLeaveCredit = ExpectedHoursCalculator.GetMonthLeaveCredit(
                _selectedYear, _selectedMonth,
                date => monthLeaveLookup.TryGetValue(date, out var e) ? e?.Duration : null);

            IsMonthlyUsageEmpty = _monthlyAppUsage.Count == 0;

            NotifyMonthHeaderTotalsChanged();
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

        public string MonthlyExpectedText =>
            FormatTimeSpan(ExpectedHoursCalculator.GetMonthExpectedHours(_selectedYear, _selectedMonth, DateTime.Today));

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

        // TotalActive = tracked + manual + leave.
        public string MonthlyTotalActiveText
        {
            get
            {
                var tracked = TimeSpan.FromSeconds(_monthlyAppUsage.Sum(x => Math.Max(0, x.TotalActive.TotalSeconds)));
                var manual = TimeSpan.FromSeconds(GetManualSecondsForMonth(SelectedMonthDateTime));
                return FormatTimeSpan(tracked + manual + _monthLeaveCredit);
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
                    Active = tracked + manual,
                    Leave = _monthLeaveCredit
                };
                return summary.StatusText;
            }
        }

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
            UpdateActivityChart();
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

        // TotalActive = tracked + manual + leave.
        public string SelectedDayTotalActiveText =>
            (TotalActiveTimeToday + _selectedDayManualDuration + _selectedDayLeaveCredit).ToHoursMinutes();

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

        public string SelectedDayExpectedHoursText => _selectedDayExpectedHours.ToHoursMinutes();

        public bool HasWeekLeave => _hasWeekLeave;

        public string WeekLeaveSummaryText => _weekLeaveSummaryText;

        public TimeSpan WeekExpectedHours => _weekExpectedHours;

        public string WeekExpectedHoursText => _weekExpectedHours.ToHoursMinutes();

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

        // TotalActive = Active + Leave.
        public string WeekTotalActiveText => WeeklyTotalActiveDuration.ToHoursMinutes();

        public string WeekStatusText
        {
            get
            {
                var summary = new HoursCalculationHelper.PeriodHoursSummary
                {
                    Expected = _weekExpectedHours,
                    Active = WeeklyTrackedActiveDuration + WeeklyManualDuration,
                    Leave = WeeklyLeaveDuration
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

        private static string FormatTimeSpan(TimeSpan value)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", (int)value.TotalHours, value.Minutes);
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
                LeaveCredit = leaveCredit
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
            summary.SetDurations(active, manual, idle, locked, leaveCredit);
        }

        private void RecalculateWeeklyTotals()
        {
            WeeklyTrackedActiveDuration = TimeSpan.FromTicks(_weeklySummaries.Sum(d => d.ActiveDuration.Ticks));
            WeeklyManualDuration = TimeSpan.FromTicks(_weeklySummaries.Sum(d => d.ManualTaskDuration.Ticks));
            WeeklyLeaveDuration = TimeSpan.FromTicks(_weeklySummaries.Sum(d => d.LeaveCredit.Ticks));
            // Explicit formula mirrors monthly: tracked + manual + leave.
            WeeklyTotalActiveDuration = WeeklyTrackedActiveDuration + WeeklyManualDuration + WeeklyLeaveDuration;
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
