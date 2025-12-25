namespace SystemActivityTracker.Models
{
    public class AppSettings
    {
        public int IdleThresholdMinutes { get; set; } = 2;
        public int PollIntervalSeconds { get; set; } = 5;
        public bool EnableLiveRefresh { get; set; } = true;
        public int LiveRefreshIntervalSeconds { get; set; } = 30;
        public bool AutoStartTrackingOnLaunch { get; set; } = true;
    }
}
