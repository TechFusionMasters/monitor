using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SystemActivityTracker.Models;
using SystemActivityTracker.Services;
using SystemActivityTracker.Utilities;

namespace SystemActivityTracker.ViewModels
{
    // Read-only display row for the Holidays grid. Rebuilt whenever the selected
    // year's holiday list is (re)loaded, so it needs no change notification of its own.
    public sealed class HolidayListItem
    {
        public Guid Id { get; set; }
        public DateTime Date { get; set; }
        public string DayName => Date.ToString("dddd");
        public string HolidayName { get; set; } = string.Empty;
        public bool IsWeekend => !ExpectedHoursCalculator.IsWorkingDay(Date);
    }

    // Manages the yearly public-holiday list shown on the Holidays tab. Self-contained
    // (own commands, own persistence via HolidayService) so it can be reused as-is by
    // Daily/Weekly/Monthly/Yearly report surfaces later without touching MainWindowViewModel.
    // Not wired into expected-hours calculations yet — that's a follow-up.
    public sealed class HolidaysViewModel : INotifyPropertyChanged
    {
        private readonly HolidayService _service;
        private List<HolidayEntry> _yearEntries = new List<HolidayEntry>();
        private readonly ObservableCollection<HolidayListItem> _holidays = new ObservableCollection<HolidayListItem>();

        private int _selectedYear = DateTime.Today.Year;
        private bool _isFormVisible;
        private bool _isEditMode;
        private Guid? _editingId;
        private DateTime _formDate = DateTime.Today;
        private string _formName = string.Empty;
        private string _validationMessage = string.Empty;

        public HolidaysViewModel(HolidayService? service = null)
        {
            _service = service ?? new HolidayService();

            var currentYear = DateTime.Today.Year;
            AvailableYears = Enumerable.Range(currentYear - 5, 11).ToList();

            ShowAddFormCommand = new RelayCommand(_ => ShowAddForm());
            BeginEditHolidayCommand = new RelayCommand(p => BeginEditHoliday(p as HolidayListItem));
            SaveHolidayCommand = new RelayCommand(_ => SaveHoliday());
            CancelHolidayEditCommand = new RelayCommand(_ => CancelEdit());
            DeleteHolidayCommand = new RelayCommand(p => DeleteHoliday(p as HolidayListItem));

            LoadForSelectedYear();
        }

        public IReadOnlyList<int> AvailableYears { get; }

        public int SelectedYear
        {
            get => _selectedYear;
            set
            {
                if (_selectedYear != value)
                {
                    _selectedYear = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(YearRangeStart));
                    OnPropertyChanged(nameof(YearRangeEnd));
                    CancelEdit();
                    LoadForSelectedYear();
                }
            }
        }

        // Bound to the form DatePicker's DisplayDateStart/End so a holiday can never be
        // saved under a year other than the one currently selected.
        public DateTime YearRangeStart => new DateTime(SelectedYear, 1, 1);
        public DateTime YearRangeEnd => new DateTime(SelectedYear, 12, 31);

        public ObservableCollection<HolidayListItem> Holidays => _holidays;

        public bool HasHolidays => _holidays.Count > 0;
        public int TotalHolidays => _holidays.Count;
        public int WeekdayHolidayCount => _holidays.Count(h => !h.IsWeekend);
        public int WeekendHolidayCount => _holidays.Count(h => h.IsWeekend);

        public bool IsFormVisible
        {
            get => _isFormVisible;
            private set
            {
                if (_isFormVisible != value)
                {
                    _isFormVisible = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsEditMode
        {
            get => _isEditMode;
            private set
            {
                if (_isEditMode != value)
                {
                    _isEditMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FormTitle));
                    OnPropertyChanged(nameof(FormPrimaryTooltip));
                }
            }
        }

        public string FormTitle => IsEditMode ? "Edit Holiday" : "Add Holiday";
        public string FormPrimaryTooltip => IsEditMode ? "Save" : "Add";

        public DateTime FormDate
        {
            get => _formDate;
            set
            {
                if (_formDate != value)
                {
                    _formDate = value;
                    OnPropertyChanged();
                }
            }
        }

        public string FormName
        {
            get => _formName;
            set
            {
                if (!string.Equals(_formName, value, StringComparison.Ordinal))
                {
                    _formName = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ValidationMessage
        {
            get => _validationMessage;
            private set
            {
                if (!string.Equals(_validationMessage, value, StringComparison.Ordinal))
                {
                    _validationMessage = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasValidationMessage));
                }
            }
        }

        public bool HasValidationMessage => !string.IsNullOrEmpty(ValidationMessage);

        public ICommand ShowAddFormCommand { get; }
        public ICommand BeginEditHolidayCommand { get; }
        public ICommand SaveHolidayCommand { get; }
        public ICommand CancelHolidayEditCommand { get; }
        public ICommand DeleteHolidayCommand { get; }

        private void LoadForSelectedYear()
        {
            _yearEntries = _service.LoadYear(SelectedYear);

            _holidays.Clear();
            foreach (var entry in _yearEntries)
            {
                _holidays.Add(new HolidayListItem
                {
                    Id = entry.Id,
                    Date = entry.Date,
                    HolidayName = entry.Name
                });
            }

            RaiseSummaryChanged();
        }

        private void RaiseSummaryChanged()
        {
            OnPropertyChanged(nameof(HasHolidays));
            OnPropertyChanged(nameof(TotalHolidays));
            OnPropertyChanged(nameof(WeekdayHolidayCount));
            OnPropertyChanged(nameof(WeekendHolidayCount));
        }

        private void ShowAddForm()
        {
            _editingId = null;
            FormDate = DateTime.Today.Year == SelectedYear ? DateTime.Today : YearRangeStart;
            FormName = string.Empty;
            ValidationMessage = string.Empty;
            IsEditMode = false;
            IsFormVisible = true;
        }

        private void BeginEditHoliday(HolidayListItem? item)
        {
            if (item == null)
            {
                return;
            }

            _editingId = item.Id;
            FormDate = item.Date;
            FormName = item.HolidayName;
            ValidationMessage = string.Empty;
            IsEditMode = true;
            IsFormVisible = true;
        }

        private void SaveHoliday()
        {
            var date = FormDate.Date;
            var name = FormName?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(name))
            {
                ValidationMessage = "Enter a holiday name.";
                return;
            }

            bool isDuplicate = _yearEntries.Any(h => h.Date.Date == date && h.Id != _editingId);
            if (isDuplicate)
            {
                ValidationMessage = "A holiday already exists on this date.";
                return;
            }

            if (IsEditMode && _editingId.HasValue)
            {
                var existing = _yearEntries.FirstOrDefault(h => h.Id == _editingId.Value);
                if (existing != null)
                {
                    existing.Date = date;
                    existing.Name = name;
                }
            }
            else
            {
                _yearEntries.Add(new HolidayEntry { Date = date, Name = name });
            }

            Persist();
            CancelEdit();
        }

        private void DeleteHoliday(HolidayListItem? item)
        {
            if (item == null)
            {
                return;
            }

            var existing = _yearEntries.FirstOrDefault(h => h.Id == item.Id);
            if (existing == null)
            {
                return;
            }

            _yearEntries.Remove(existing);
            Persist();

            if (_editingId == item.Id)
            {
                CancelEdit();
            }
        }

        private void CancelEdit()
        {
            _editingId = null;
            IsFormVisible = false;
            IsEditMode = false;
            FormName = string.Empty;
            ValidationMessage = string.Empty;
        }

        private void Persist()
        {
            _service.SaveYear(SelectedYear, _yearEntries);
            LoadForSelectedYear();
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
