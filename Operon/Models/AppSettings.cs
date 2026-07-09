namespace SystemActivityTracker.Models
{
    public class AppSettings
    {
        public int IdleThresholdMinutes { get; set; } = Utilities.AppConstants.Defaults.IdleThresholdMinutes;
        public int PollIntervalSeconds { get; set; } = Utilities.AppConstants.Defaults.PollIntervalSeconds;
        public bool EnableLiveRefresh { get; set; } = Utilities.AppConstants.Defaults.EnableLiveRefresh;
        public int LiveRefreshIntervalSeconds { get; set; } = Utilities.AppConstants.Defaults.LiveRefreshIntervalSeconds;
        public bool AutoStartTrackingOnLaunch { get; set; } = Utilities.AppConstants.Defaults.AutoStartTrackingOnLaunch;

        public string UiMode { get; set; } = Utilities.UiModes.Default;

        public int CrashLogRetentionDays { get; set; } = Utilities.AppConstants.Defaults.CrashLogRetentionDays;
        public int CrashLogMaxSizeMB { get; set; } = Utilities.AppConstants.Defaults.CrashLogMaxSizeMB;

        // Last dragged position of the floating mini timer (screen coordinates). Null until
        // the user drags it at least once, in which case FloatingTimerService falls back to
        // its default bottom-right-above-the-taskbar placement.
        public double? FloatingTimerLeft { get; set; }
        public double? FloatingTimerTop { get; set; }

        // Whether minimizing/closing-to-tray should show the floating mini timer at all.
        // Unlike most settings here, this one is applied and persisted immediately when
        // toggled (Settings tab checkbox, or the floating timer's own "Hide" quick action) —
        // see MainWindowViewModel.ShowFloatingTimerOnMinimize.
        public bool ShowFloatingTimerOnMinimize { get; set; } = true;
    }
}
