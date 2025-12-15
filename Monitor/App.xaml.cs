using System;
using System.Windows;
using SystemActivityTracker.Services;

namespace SystemActivityTracker
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        private SessionStateService? _sessionStateService;
        private TrackingService? _trackingService;
        private TrayIconService? _trayIconService;
        private SettingsService? _settingsService;

        public bool IsShuttingDown { get; set; }

        public TrackingService? TrackingService => _trackingService;
        public SettingsService? SettingsService => _settingsService;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            _settingsService = new SettingsService();
            _sessionStateService = new SessionStateService();
            _trackingService = new TrackingService(_sessionStateService, _settingsService);
            if (_trackingService != null)
            {
                _trayIconService = new TrayIconService(this, _trackingService);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _trayIconService?.Dispose();
            _trayIconService = null;
            _trackingService?.Shutdown();
            _trackingService?.Dispose();
            _trackingService = null;
            _settingsService = null;
            _sessionStateService?.Dispose();
            _sessionStateService = null;
            base.OnExit(e);
        }
    }

}
