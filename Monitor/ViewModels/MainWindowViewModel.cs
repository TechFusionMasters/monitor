using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using SystemActivityTracker.Models;
using SystemActivityTracker.Services;

namespace SystemActivityTracker.ViewModels
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        private readonly TrackingService? _trackingService;
        private readonly SettingsService? _settingsService;
        private string _trackingStatus = "Tracking status: Stopped";
        private TimeSpan _totalActiveTimeToday;
        private TimeSpan _totalIdleTimeToday;
        private TimeSpan _totalLockedTimeToday;
        private readonly ObservableCollection<AppUsageSummary> _todayAppUsage = new ObservableCollection<AppUsageSummary>();
        private int _idleThresholdMinutes;
        private int _pollIntervalSeconds;
        private bool _enableLiveRefresh;
        private int _liveRefreshIntervalSeconds;
        private bool _isTestMode;
        private DateTime _selectedDate = DateTime.Today;
        private DateTime _weekStartDate;
        private readonly ObservableCollection<DailySummary> _weeklySummaries = new ObservableCollection<DailySummary>();
        private TimeSpan _weeklyTotalActiveDuration;
        private TimeSpan _weeklyTotalIdleDuration;
        private TimeSpan _weeklyTotalLockedDuration;
        private readonly DispatcherTimer _autoRefreshTimer = new DispatcherTimer();
        private AppSettings _settingsSnapshot = new AppSettings();

        public MainWindowViewModel(TrackingService? trackingService, SettingsService? settingsService = null)
        {
            _trackingService = trackingService;
            _settingsService = settingsService;
            TodayText = DateTime.Now.ToString("dddd, dd MMMM yyyy");
            _weekStartDate = StartOfWeek(DateTime.Today, DayOfWeek.Monday);
            StartCommand = new RelayCommand(_ =>
            {
                _trackingService?.Start();
                TrackingStatus = "Tracking status: Running";
                ApplyLiveRefreshSettings();
            });

            StopCommand = new RelayCommand(_ =>
            {
                _trackingService?.Stop();
                TrackingStatus = "Tracking status: Stopped";
                _autoRefreshTimer.Stop();
            });

            RefreshCommand = new RelayCommand(_ => RefreshTodaySummary());

            SaveSettingsCommand = new RelayCommand(_ => SaveSettings());
            LoadWeeklyCommand = new RelayCommand(_ => LoadWeeklySummary());
            ForceWriteNowCommand = new RelayCommand(_ =>
            {
                _trackingService?.FlushCurrentRecord();
                RefreshSelectedDaySummary();
                RefreshSelectedDayAppUsage();
                RefreshWeeklySummary();
            });

            _autoRefreshTimer.Tick += (_, __) =>
            {
                if (_trackingService != null && _trackingService.IsRunning && SelectedDate.Date == DateTime.Today)
                {
                    RefreshTodaySummary();
                }
            };

            // Load settings for UI
            var settings = _settingsService?.Load() ?? new AppSettings();
            if (settings.IdleThresholdMinutes <= 0) settings.IdleThresholdMinutes = 2;
            if (settings.PollIntervalSeconds <= 0) settings.PollIntervalSeconds = 5;
            if (settings.LiveRefreshIntervalSeconds <= 0) settings.LiveRefreshIntervalSeconds = 30;

            _settingsSnapshot = settings;

            IdleThresholdMinutes = settings.IdleThresholdMinutes;
            PollIntervalSeconds = settings.PollIntervalSeconds;
            EnableLiveRefresh = settings.EnableLiveRefresh;
            LiveRefreshIntervalSeconds = settings.LiveRefreshIntervalSeconds;

            LoadWeeklySummary();
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
                }
            }
        }

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
        public ICommand LoadWeeklyCommand { get; }
        public ICommand ForceWriteNowCommand { get; }

        public ObservableCollection<AppUsageSummary> TodayAppUsage => _todayAppUsage;
        public ObservableCollection<DailySummary> WeeklySummaries => _weeklySummaries;

        public DateTime SelectedDate
        {
            get => _selectedDate;
            set
            {
                if (_selectedDate != value)
                {
                    _selectedDate = value.Date;
                    OnPropertyChanged();
                    ApplyLiveRefreshSettings();
                }
            }
        }

        public DateTime WeekStartDate
        {
            get => _weekStartDate;
            set
            {
                var normalized = value.Date;
                if (_weekStartDate != normalized)
                {
                    _weekStartDate = normalized;
                    OnPropertyChanged();
                }
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
                    OnPropertyChanged(nameof(SelectedDayActiveText));
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
                    OnPropertyChanged(nameof(SelectedDayIdleText));
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
                    OnPropertyChanged(nameof(SelectedDayLockedText));
                }
            }
        }

        public string TotalActiveTimeTodayDisplay => FormatTimeSpan(TotalActiveTimeToday);
        public string TotalIdleTimeTodayDisplay => FormatTimeSpan(TotalIdleTimeToday);
        public string TotalLockedTimeTodayDisplay => FormatTimeSpan(TotalLockedTimeToday);

        public string SelectedDayActiveText  => $"Active: {TotalActiveTimeToday:hh\\:mm}";
        public string SelectedDayIdleText    => $"Idle: {TotalIdleTimeToday:hh\\:mm}";
        public string SelectedDayLockedText  => $"Locked: {TotalLockedTimeToday:hh\\:mm}";

        public TimeSpan WeeklyTotalActiveDuration
        {
            get => _weeklyTotalActiveDuration;
            private set
            {
                if (_weeklyTotalActiveDuration != value)
                {
                    _weeklyTotalActiveDuration = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(WeeklyTotalActiveText));
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
                    OnPropertyChanged(nameof(WeeklyTotalIdleText));
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
                    OnPropertyChanged(nameof(WeeklyTotalLockedText));
                }
            }
        }

        public string WeeklyTotalActiveText  => $"Total Active: {WeeklyTotalActiveDuration:hh\\:mm}";
        public string WeeklyTotalIdleText    => $"Total Idle: {WeeklyTotalIdleDuration:hh\\:mm}";
        public string WeeklyTotalLockedText  => $"Total Locked: {WeeklyTotalLockedDuration:hh\\:mm}";

        private void SaveSettings()
        {
            _settingsSnapshot.IdleThresholdMinutes = IdleThresholdMinutes;
            _settingsSnapshot.PollIntervalSeconds = PollIntervalSeconds;
            _settingsSnapshot.EnableLiveRefresh = EnableLiveRefresh;
            _settingsSnapshot.LiveRefreshIntervalSeconds = LiveRefreshIntervalSeconds;

            _settingsService?.Save(_settingsSnapshot);
            _trackingService?.ApplySettings(_settingsSnapshot);
            ApplyLiveRefreshSettings();
        }

        private static DateTime StartOfWeek(DateTime date, DayOfWeek startOfWeek)
        {
            int diff = (7 + (date.DayOfWeek - startOfWeek)) % 7;
            return date.AddDays(-1 * diff).Date;
        }

        private void RefreshTodaySummary()
        {
            TotalActiveTimeToday = TimeSpan.Zero;
            TotalIdleTimeToday = TimeSpan.Zero;
            TotalLockedTimeToday = TimeSpan.Zero;
            _todayAppUsage.Clear();

            string baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appFolder = Path.Combine(baseFolder, "SystemActivityTracker");
            string fileName = $"activity-log-{SelectedDate:yyyy-MM-dd}.csv";
            string filePath = Path.Combine(appFolder, fileName);

            if (!File.Exists(filePath))
            {
                return;
            }

            var perProcessDurations = new System.Collections.Generic.Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in File.ReadLines(filePath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (line.StartsWith("StartTime", StringComparison.OrdinalIgnoreCase))
                {
                    continue; // header
                }

                var fields = ParseCsvLine(line);
                if (fields.Length < 6)
                {
                    continue;
                }

                if (!DateTime.TryParse(fields[0], null, DateTimeStyles.RoundtripKind, out var start))
                {
                    continue;
                }

                if (!DateTime.TryParse(fields[1], null, DateTimeStyles.RoundtripKind, out var end))
                {
                    continue;
                }

                if (!bool.TryParse(fields[4], out var isLocked))
                {
                    continue;
                }

                if (!bool.TryParse(fields[5], out var isIdle))
                {
                    continue;
                }

                if (end < start)
                {
                    continue;
                }

                var duration = end - start;

                if (isLocked)
                {
                    TotalLockedTimeToday += duration;
                }
                else if (isIdle)
                {
                    TotalIdleTimeToday += duration;
                }
                else
                {
                    TotalActiveTimeToday += duration;

                    string processName = fields.Length > 2 ? fields[2] : string.Empty;
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
            OnPropertyChanged(nameof(SelectedDayActiveText));
            OnPropertyChanged(nameof(SelectedDayIdleText));
            OnPropertyChanged(nameof(SelectedDayLockedText));
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

        private static string[] ParseCsvLine(string line)
        {
            var result = new System.Collections.Generic.List<string>();
            bool inQuotes = false;
            var current = new System.Text.StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            result.Add(current.ToString());
            return result.ToArray();
        }

        public void LoadWeeklySummary()
        {
            _weeklySummaries.Clear();

            string baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appFolder = Path.Combine(baseFolder, "SystemActivityTracker");

            for (int i = 0; i < 7; i++)
            {
                DateTime date = WeekStartDate.Date.AddDays(i);
                string fileName = $"activity-log-{date:yyyy-MM-dd}.csv";
                string filePath = Path.Combine(appFolder, fileName);

                TimeSpan active = TimeSpan.Zero;
                TimeSpan idle = TimeSpan.Zero;
                TimeSpan locked = TimeSpan.Zero;

                if (File.Exists(filePath))
                {
                    foreach (var line in File.ReadLines(filePath))
                    {
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        if (line.StartsWith("StartTime", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var fields = ParseCsvLine(line);
                        if (fields.Length < 6)
                        {
                            continue;
                        }

                        if (!DateTime.TryParse(fields[0], null, DateTimeStyles.RoundtripKind, out var start))
                        {
                            continue;
                        }

                        if (!DateTime.TryParse(fields[1], null, DateTimeStyles.RoundtripKind, out var end))
                        {
                            continue;
                        }

                        if (!bool.TryParse(fields[4], out var isLocked))
                        {
                            continue;
                        }

                        if (!bool.TryParse(fields[5], out var isIdle))
                        {
                            continue;
                        }

                        if (end < start)
                        {
                            continue;
                        }

                        var duration = end - start;

                        if (isLocked)
                        {
                            locked += duration;
                        }
                        else if (isIdle)
                        {
                            idle += duration;
                        }
                        else
                        {
                            active += duration;
                        }
                    }
                }

                _weeklySummaries.Add(new DailySummary
                {
                    Date = date,
                    ActiveDuration = active,
                    IdleDuration = idle,
                    LockedDuration = locked
                });
            }

            WeeklyTotalActiveDuration = TimeSpan.FromTicks(_weeklySummaries.Sum(d => d.ActiveDuration.Ticks));
            WeeklyTotalIdleDuration   = TimeSpan.FromTicks(_weeklySummaries.Sum(d => d.IdleDuration.Ticks));
            WeeklyTotalLockedDuration = TimeSpan.FromTicks(_weeklySummaries.Sum(d => d.LockedDuration.Ticks));
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

            public event EventHandler? CanExecuteChanged
            {
                add { CommandManager.RequerySuggested += value; }
                remove { CommandManager.RequerySuggested -= value; }
            }
        }
    }
}
