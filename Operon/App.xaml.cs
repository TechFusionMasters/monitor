using System;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SystemActivityTracker.Services;
using SystemActivityTracker.Services.Abstractions;
using SystemActivityTracker.Services.Platform;
using SystemActivityTracker.Utilities;
using SystemActivityTracker.ViewModels;
using SystemActivityTracker.Views;
using System.Windows.Threading;
using System.Diagnostics;

namespace SystemActivityTracker
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        public IServiceProvider Services { get; private set; } = null!;

        private SingleInstanceManager? _singleInstanceManager;
        private SessionStateService? _sessionStateService;
        private TrackingService? _trackingService;
        private TrayIconService? _trayIconService;
        private SettingsService? _settingsService;
        private CloseTrackingService? _closeTrackingService;
        private DispatcherTimer? _uiHeartbeatTimer;

        public bool IsShuttingDown { get; set; }

        public CloseTrackingService? CloseTrackingService => _closeTrackingService;

        public TrackingService? TrackingService => _trackingService;
        public SettingsService? SettingsService => _settingsService;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Step 1: Check for existing instance and start activation listener
            _singleInstanceManager = new SingleInstanceManager();
            if (!_singleInstanceManager.AcquireInstance())
            {
                // Another instance is already running; the first instance will be signalled.
                // No need for manual window enumeration, just exit quietly.
                this.Shutdown();
                return;
            }

            // If we are the first instance, listen for activation signals from second instances.
            _singleInstanceManager.InstanceActivated += (s, ev) =>
            {
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        if (MainWindow != null)
                        {
                            var h = new System.Windows.Interop.WindowInteropHelper(MainWindow).Handle;
                            WindowActivator.ActivateWindow(h);
                        }
                    }
                    catch
                    {
                        // ignore errors during activation
                    }
                });
            };

            var serviceCollection = new ServiceCollection();

            serviceCollection.AddSingleton<SettingsService>();
            serviceCollection.AddSingleton<CloseTrackingService>();
            serviceCollection.AddSingleton<SessionStateService>();

            serviceCollection.AddSingleton<IClock, SystemClock>();
            serviceCollection.AddSingleton<IIdleTimeProvider, SystemIdleTimeProvider>();
            serviceCollection.AddSingleton<IActiveWindowProvider, SystemActiveWindowProvider>();
            serviceCollection.AddSingleton<IActivityLogWriter, ActivityLogWriter>();

            serviceCollection.AddSingleton<TrackingService>(sp =>
            {
                var session = sp.GetRequiredService<SessionStateService>();
                var settings = sp.GetRequiredService<SettingsService>();
                return new TrackingService(session, settings);
            });

            serviceCollection.AddSingleton<IActivityLogReader, ActivityLogReader>();
            serviceCollection.AddSingleton<ICrashLogReader, CrashLogReader>();
            serviceCollection.AddTransient<ManualTaskService>();
            serviceCollection.AddTransient<LeaveService>();
            serviceCollection.AddTransient<HolidayService>();
            serviceCollection.AddTransient<AppCategoryService>();

            serviceCollection.AddTransient<LastCrashViewModel>(sp =>
                new LastCrashViewModel(sp.GetRequiredService<ICrashLogReader>()));

            serviceCollection.AddTransient<HolidaysViewModel>(sp =>
                new HolidaysViewModel(sp.GetRequiredService<HolidayService>()));

            serviceCollection.AddTransient<WorkSummaryViewModel>(sp =>
                new WorkSummaryViewModel(
                    sp.GetRequiredService<IActivityLogReader>(),
                    sp.GetRequiredService<ManualTaskService>(),
                    sp.GetRequiredService<LeaveService>(),
                    sp.GetRequiredService<HolidayService>()));

            serviceCollection.AddTransient<AppCategoriesViewModel>(sp =>
                new AppCategoriesViewModel(
                    sp.GetRequiredService<AppCategoryService>(),
                    sp.GetRequiredService<IActivityLogReader>()));

            serviceCollection.AddTransient<AppUsageBreakdownViewModel>(sp =>
                new AppUsageBreakdownViewModel(
                    sp.GetRequiredService<IActivityLogReader>(),
                    sp.GetRequiredService<AppCategoryService>()));

            serviceCollection.AddTransient<MainWindowViewModel>(sp =>
                new MainWindowViewModel(
                    sp.GetService<TrackingService>(),
                    sp.GetService<SettingsService>(),
                    sp.GetRequiredService<IActivityLogReader>(),
                    sp.GetRequiredService<ManualTaskService>(),
                    sp.GetRequiredService<LeaveService>(),
                    sp.GetRequiredService<LastCrashViewModel>(),
                    sp.GetRequiredService<HolidaysViewModel>(),
                    sp.GetRequiredService<WorkSummaryViewModel>(),
                    sp.GetRequiredService<AppCategoriesViewModel>(),
                    sp.GetRequiredService<AppUsageBreakdownViewModel>()));

            Services = serviceCollection.BuildServiceProvider();

            _settingsService = Services.GetRequiredService<SettingsService>();
            var settings = _settingsService.Load();

            _closeTrackingService = Services.GetRequiredService<CloseTrackingService>();
            _closeTrackingService.ApplyCrashLogPolicy(settings.CrashLogRetentionDays, settings.CrashLogMaxSizeMB);
            _closeTrackingService.CleanupCrashLogs();
            RegisterCloseTrackingHooks();
            StartUiHeartbeat();

            _sessionStateService = Services.GetRequiredService<SessionStateService>();
            _trackingService = Services.GetRequiredService<TrackingService>();
            _trayIconService = new TrayIconService(this, _trackingService);

            SystemEvents.SessionEnding += OnSystemSessionEnding;

            try
            {
                MainWindow = CreateMainWindowForMode(settings.UiMode, null);
                MainWindow.Show();
            }
            catch
            {
                MainWindow = new UiAMainWindow();
                MainWindow.Show();
            }
        }

        internal Window CreateMainWindowForMode(string? uiMode, MainWindowViewModel? existingVm)
        {
            var normalized = string.Equals(uiMode, UiModes.UIB, StringComparison.OrdinalIgnoreCase)
                ? UiModes.UIB
                : UiModes.UIA;

            MainWindowViewModel vm = existingVm ?? Services.GetRequiredService<MainWindowViewModel>();

            Window window;
            if (string.Equals(normalized, UiModes.UIB, StringComparison.Ordinal))
            {
                window = new UiBMainWindow();
            }
            else
            {
                window = new UiAMainWindow();
            }

            window.DataContext = vm;
            return window;
        }

        internal void SwitchUiMode(string uiMode, MainWindowViewModel? existingVm)
        {
            Dispatcher.Invoke(() =>
            {
                var old = MainWindow;
                var newWindow = CreateMainWindowForMode(uiMode, existingVm);
                MainWindow = newWindow;

                try
                {
                    newWindow.Show();
                    newWindow.Activate();
                }
                catch
                {
                }

                try
                {
                    old?.Close();
                }
                catch
                {
                }
            });
        }

        private void RegisterCloseTrackingHooks()
        {
            DispatcherUnhandledException += (_, exArgs) =>
            {
                try
                {
                    _closeTrackingService?.MarkUnhandledUiException(exArgs.Exception);
                }
                catch
                {
                }
            };

            AppDomain.CurrentDomain.UnhandledException += (_, exArgs) =>
            {
                try
                {
                    if (exArgs.ExceptionObject is Exception ex)
                    {
                        _closeTrackingService?.MarkUnhandledBackgroundException(ex);
                    }
                }
                catch
                {
                }
            };

            TaskScheduler.UnobservedTaskException += (_, exArgs) =>
            {
                try
                {
                    _closeTrackingService?.MarkUnobservedTaskException(exArgs.Exception);
                    exArgs.SetObserved();
                }
                catch
                {
                }
            };
        }

        private void StartUiHeartbeat()
        {
            _uiHeartbeatTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _uiHeartbeatTimer.Tick += (_, __) =>
            {
                try
                {
                    _closeTrackingService?.TouchUiHeartbeat();
                }
                catch
                {
                }
            };
            _uiHeartbeatTimer.Start();
        }

        private void OnSystemSessionEnding(object? sender, SessionEndingEventArgs e)
        {
            try
            {
                if (_closeTrackingService != null)
                {
                    _closeTrackingService.ShutdownOrLogoffDetected = true;
                }
                _trackingService?.HandleSessionEnding();
            }
            catch
            {
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Clean up single instance manager
            _singleInstanceManager?.Dispose();
            _singleInstanceManager = null;

            SystemEvents.SessionEnding -= OnSystemSessionEnding;

            if (_uiHeartbeatTimer != null)
            {
                _uiHeartbeatTimer.Stop();
                _uiHeartbeatTimer = null;
            }

            try
            {
                _closeTrackingService?.CompleteGracefulExit();
            }
            catch
            {
            }
            finally
            {
                _closeTrackingService?.Dispose();
                _closeTrackingService = null;
            }

            _trayIconService?.Dispose();
            _trayIconService = null;
            _trackingService?.Shutdown();
            _trackingService?.Dispose();
            _trackingService = null;
            _settingsService = null;
            _sessionStateService?.Dispose();
            _sessionStateService = null;

            if (Services is IDisposable disposable)
            {
                disposable.Dispose();
            }
            base.OnExit(e);
        }
    }

}
