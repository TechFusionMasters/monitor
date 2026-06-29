using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SystemActivityTracker.Models
{
    public class DailySummary : INotifyPropertyChanged
    {
        private DateTime _date;
        private TimeSpan _activeDuration;
        private TimeSpan _manualTaskDuration;
        private TimeSpan _idleDuration;
        private TimeSpan _lockedDuration;
        private TimeSpan _leaveCredit;

        public DateTime Date
        {
            get => _date;
            set
            {
                if (_date == value) return;
                _date = value;
                OnPropertyChanged();
            }
        }

        // Tracked computer-active time only.
        public TimeSpan ActiveDuration
        {
            get => _activeDuration;
            set => SetDuration(ref _activeDuration, value, nameof(ActiveDurationText));
        }

        public TimeSpan ManualTaskDuration
        {
            get => _manualTaskDuration;
            set => SetDuration(ref _manualTaskDuration, value, nameof(ManualTaskDurationText));
        }

        public TimeSpan IdleDuration
        {
            get => _idleDuration;
            set => SetDuration(ref _idleDuration, value, nameof(IdleDurationText));
        }

        public TimeSpan LockedDuration
        {
            get => _lockedDuration;
            set => SetDuration(ref _lockedDuration, value, nameof(LockedDurationText));
        }

        // Approved leave credit (Mon–Fri only; full-day=8h, half-day=4h).
        public TimeSpan LeaveCredit
        {
            get => _leaveCredit;
            set => SetDuration(ref _leaveCredit, value, nameof(LeaveCreditText));
        }

        // Active = tracked + manual (all actual work, matching the PeriodHoursSummary model).
        public TimeSpan CombinedActiveDuration => ActiveDuration + ManualTaskDuration;

        // Total Active = tracked + manual + leave (the definitive "did you meet your target" value).
        public TimeSpan TotalActiveDuration => ActiveDuration + ManualTaskDuration + LeaveCredit;

        public string ActiveDurationText => ActiveDuration.ToString(@"hh\:mm");
        public string ManualTaskDurationText => ManualTaskDuration.ToString(@"hh\:mm");
        public string LeaveCreditText => LeaveCredit.ToString(@"hh\:mm");
        public string TotalActiveDurationText => TotalActiveDuration.ToString(@"hh\:mm");
        public string IdleDurationText => IdleDuration.ToString(@"hh\:mm");
        public string LockedDurationText => LockedDuration.ToString(@"hh\:mm");

        public void SetDurations(TimeSpan active, TimeSpan manual, TimeSpan idle, TimeSpan locked,
            TimeSpan leaveCredit = default)
        {
            ActiveDuration = active;
            ManualTaskDuration = manual;
            IdleDuration = idle;
            LockedDuration = locked;
            LeaveCredit = leaveCredit;
        }

        private void SetDuration(ref TimeSpan field, TimeSpan value, string textPropertyName)
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(textPropertyName);
            OnPropertyChanged(nameof(CombinedActiveDuration));
            OnPropertyChanged(nameof(TotalActiveDuration));
            OnPropertyChanged(nameof(TotalActiveDurationText));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
