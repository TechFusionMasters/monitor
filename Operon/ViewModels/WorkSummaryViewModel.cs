using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SystemActivityTracker.Services;
using SystemActivityTracker.Services.Abstractions;
using SystemActivityTracker.Utilities;

namespace SystemActivityTracker.ViewModels
{
    public enum WorkSummaryViewMode
    {
        Today,
        Week,
        Month,
        Custom
    }

    // Drives the Work Summary panel on the Application Usage tab. Self-contained (own
    // navigation state, own commands) so it never touches the Monthly Usage / Weekly
    // Overview tabs' existing SelectedDate/SelectedMonthYear/SelectedWeekStart state.
    // Reuses IActivityLogReader, ManualTaskService, LeaveService, and HolidayService —
    // the same data sources those other tabs already read from — plus the additive
    // WorkSummaryCalculator for the Expected/Progress/Status math.
    public sealed class WorkSummaryViewModel : INotifyPropertyChanged
    {
        private readonly IActivityLogReader _activityLogReader;
        private readonly ManualTaskService _manualTaskService;
        private readonly LeaveService _leaveService;
        private readonly HolidayService _holidayService;

        private WorkSummaryViewMode _viewMode = WorkSummaryViewMode.Today;
        private DateTime _weekStart = WorkWeekHelper.GetWeekStartMonday(DateTime.Today);
        private DateTime _monthYear = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        private DateTime _customStartDate = DateTime.Today.AddDays(-6);
        private DateTime _customEndDate = DateTime.Today;

        private TimeSpan _expectedHours;
        private TimeSpan _activeHours;
        private TimeSpan _offlineWork;
        private TimeSpan _leaveTaken;
        private TimeSpan _publicHolidayHours;
        private TimeSpan _weekendWork;
        private WorkSummaryStatus _status;
        private double _progressFraction;

        public WorkSummaryViewModel(
            IActivityLogReader? activityLogReader = null,
            ManualTaskService? manualTaskService = null,
            LeaveService? leaveService = null,
            HolidayService? holidayService = null)
        {
            _activityLogReader = activityLogReader ?? new ActivityLogReader();
            _manualTaskService = manualTaskService ?? new ManualTaskService();
            _leaveService = leaveService ?? new LeaveService();
            _holidayService = holidayService ?? new HolidayService();

            PreviousPeriodCommand = new RelayCommand(_ => NavigatePeriod(-1), _ => CanNavigatePeriod());
            NextPeriodCommand = new RelayCommand(_ => NavigatePeriod(1), _ => CanNavigatePeriod());

            Refresh();
        }

        public WorkSummaryViewMode ViewMode
        {
            get => _viewMode;
            set
            {
                if (_viewMode != value)
                {
                    _viewMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsWeekView));
                    OnPropertyChanged(nameof(IsMonthView));
                    OnPropertyChanged(nameof(IsCustomView));
                    Refresh();
                }
            }
        }

        public bool IsWeekView => ViewMode == WorkSummaryViewMode.Week;
        public bool IsMonthView => ViewMode == WorkSummaryViewMode.Month;
        public bool IsCustomView => ViewMode == WorkSummaryViewMode.Custom;

        // Bind a plain DatePicker to this — any day clicked snaps to that week's Monday.
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
                    Refresh();
                }
            }
        }

        public string WeekRangeLabel => WorkWeekHelper.FormatWeekRange(_weekStart);

        // Bind controls:MonthYearPicker.SelectedMonthYear to this, same as the Leaves tab.
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
                    Refresh();
                }
            }
        }

        // Bind DatePicker.DisplayDateEnd to this on both custom pickers so the UI itself
        // can't select a future date, on top of the clamp above.
        public DateTime MaxSelectableDate => DateTime.Today;

        public string ExpectedHoursText => FormatHm(_expectedHours);
        public string ActiveHoursText => FormatHm(_activeHours);
        public string OfflineWorkText => FormatHm(_offlineWork);
        public string TotalActiveHoursText => FormatHm(_activeHours + _offlineWork);
        public string LeaveTakenText => FormatHm(_leaveTaken);
        public string PublicHolidayHoursText => FormatHm(_publicHolidayHours);
        public string WeekendWorkText => FormatHm(_weekendWork);

        public double ProgressPercent => Math.Round(_progressFraction * 100, 0);

        public string StatusText => _status switch
        {
            WorkSummaryStatus.RemainingWork => "Remaining Work",
            WorkSummaryStatus.TargetAchieved => "Target Achieved",
            WorkSummaryStatus.ExtraWorked => "Extra Worked",
            _ => string.Empty
        };

        // Exposed as a plain string so XAML DataTriggers can match on it without a converter.
        public string StatusKey => _status.ToString();

        public ICommand PreviousPeriodCommand { get; }
        public ICommand NextPeriodCommand { get; }

        private bool CanNavigatePeriod() => ViewMode == WorkSummaryViewMode.Week || ViewMode == WorkSummaryViewMode.Month;

        private void NavigatePeriod(int direction)
        {
            if (ViewMode == WorkSummaryViewMode.Week)
            {
                WeekPickerDate = _weekStart.AddDays(7 * direction);
            }
            else if (ViewMode == WorkSummaryViewMode.Month)
            {
                MonthYear = _monthYear.AddMonths(direction);
            }
        }

        private void Refresh()
        {
            var today = DateTime.Today;
            IEnumerable<DateTime> days = ViewMode switch
            {
                WorkSummaryViewMode.Today => WorkSummaryCalculator.EnumerateTodayView(today),
                WorkSummaryViewMode.Week => WorkSummaryCalculator.EnumerateWeekView(_weekStart),
                WorkSummaryViewMode.Month => WorkSummaryCalculator.EnumerateMonthView(_monthYear.Year, _monthYear.Month, today),
                WorkSummaryViewMode.Custom => WorkSummaryCalculator.EnumerateCustomView(_customStartDate, _customEndDate, today),
                _ => Array.Empty<DateTime>()
            };

            Apply(WorkSummaryCalculator.Calculate(days, GetDayInputs));
        }

        private WorkDayInputs GetDayInputs(DateTime date)
        {
            var tracked = TimeSpan.Zero;
            if (_activityLogReader.TryReadDay(date, out var entries))
            {
                tracked = HoursCalculationHelper.SumActiveOnly(entries);
            }

            var offlineSeconds = _manualTaskService.Load(date).Sum(t => Math.Max(0, t.TotalSeconds));
            var leave = _leaveService.GetForDate(date)?.Duration;
            var isHoliday = _holidayService.IsHoliday(date);

            return new WorkDayInputs
            {
                TrackedActive = tracked,
                OfflineWork = TimeSpan.FromSeconds(offlineSeconds),
                Leave = leave,
                IsHoliday = isHoliday
            };
        }

        private void Apply(WorkSummaryResult result)
        {
            _expectedHours = result.ExpectedHours;
            _activeHours = result.ActiveHours;
            _offlineWork = result.OfflineWork;
            _leaveTaken = result.LeaveTaken;
            _publicHolidayHours = result.PublicHolidayHours;
            _weekendWork = result.WeekendWork;
            _status = result.Status;
            _progressFraction = result.ProgressFraction;

            OnPropertyChanged(nameof(ExpectedHoursText));
            OnPropertyChanged(nameof(ActiveHoursText));
            OnPropertyChanged(nameof(OfflineWorkText));
            OnPropertyChanged(nameof(TotalActiveHoursText));
            OnPropertyChanged(nameof(LeaveTakenText));
            OnPropertyChanged(nameof(PublicHolidayHoursText));
            OnPropertyChanged(nameof(WeekendWorkText));
            OnPropertyChanged(nameof(ProgressPercent));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusKey));
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

        private sealed class RelayCommand : ICommand
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
