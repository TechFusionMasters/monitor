using System;
using System.Windows;
using SystemActivityTracker.Models;
using SystemActivityTracker.Views;

namespace SystemActivityTracker.Services
{
    // Owns the single FloatingTimerWindow instance shown while the main window is
    // minimized or hidden to the tray. The window itself does no calculation — it just
    // binds to whichever MainWindowViewModel is currently active (App.MainWindow's
    // DataContext), so it always shows the identical value as the main window's header
    // running timer, including the same "no holiday hours" behavior, with no duplicated
    // timer logic to keep in sync.
    public sealed class FloatingTimerService
    {
        private readonly App _app;
        private readonly SettingsService _settingsService;
        private readonly Action _restoreMainWindow;
        private readonly Action _shutdownApplication;
        private FloatingTimerWindow? _window;

        public FloatingTimerService(App app, SettingsService settingsService, Action restoreMainWindow, Action shutdownApplication)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _restoreMainWindow = restoreMainWindow ?? throw new ArgumentNullException(nameof(restoreMainWindow));
            _shutdownApplication = shutdownApplication ?? throw new ArgumentNullException(nameof(shutdownApplication));
        }

        public void Show()
        {
            _app.Dispatcher.Invoke(() =>
            {
                // "Show floating mini timer when minimized to tray" setting — checked fresh
                // here (not cached) so toggling it in the Settings tab or via the floating
                // timer's own "Hide Floating Timer" action applies to the very next
                // minimize/close-to-tray, matching the "apply immediately" requirement.
                bool enabled;
                try
                {
                    enabled = _settingsService.Load().ShowFloatingTimerOnMinimize;
                }
                catch
                {
                    enabled = true;
                }

                if (!enabled)
                {
                    return;
                }

                var window = EnsureWindow();
                window.DataContext = _app.MainWindow?.DataContext;

                if (!window.IsVisible)
                {
                    PositionWindow(window);
                    window.Show();
                }
            });
        }

        public void Hide()
        {
            _app.Dispatcher.Invoke(() =>
            {
                if (_window != null && _window.IsVisible)
                {
                    _window.Hide();
                }
            });
        }

        public void Shutdown()
        {
            if (_window != null)
            {
                _window.ReopenRequested -= OnReopenRequested;
                _window.PositionChanged -= OnPositionChanged;
                _window.HideRequested -= OnHideRequested;
                _window.ShutdownRequested -= OnShutdownRequested;
                _window.Close();
                _window = null;
            }
        }

        private FloatingTimerWindow EnsureWindow()
        {
            if (_window != null)
            {
                return _window;
            }

            _window = new FloatingTimerWindow();
            _window.ReopenRequested += OnReopenRequested;
            _window.PositionChanged += OnPositionChanged;
            _window.HideRequested += OnHideRequested;
            _window.ShutdownRequested += OnShutdownRequested;
            return _window;
        }

        private void OnReopenRequested(object? sender, EventArgs e)
        {
            _restoreMainWindow();
            Hide();
        }

        private void OnHideRequested(object? sender, EventArgs e)
        {
            // The setting itself was already flipped+persisted by the window (via the VM
            // property) before this fires — just hide, don't touch settings here too.
            Hide();
        }

        private void OnShutdownRequested(object? sender, EventArgs e)
        {
            _shutdownApplication();
        }

        private void OnPositionChanged(object? sender, EventArgs e)
        {
            if (_window == null)
            {
                return;
            }

            try
            {
                var settings = _settingsService.Load();
                settings.FloatingTimerLeft = _window.Left;
                settings.FloatingTimerTop = _window.Top;
                _settingsService.Save(settings);
            }
            catch
            {
                // Position persistence is best-effort — losing a drag position on save
                // failure shouldn't disrupt the floating timer itself.
            }
        }

        // Restores the last dragged position if one was saved; otherwise defaults to the
        // bottom-right corner of the work area (i.e. above the taskbar, not under it).
        private void PositionWindow(FloatingTimerWindow window)
        {
            AppSettings settings;
            try
            {
                settings = _settingsService.Load();
            }
            catch
            {
                settings = new AppSettings();
            }

            var workArea = SystemParameters.WorkArea;

            if (settings.FloatingTimerLeft.HasValue && settings.FloatingTimerTop.HasValue &&
                IsOnScreen(settings.FloatingTimerLeft.Value, settings.FloatingTimerTop.Value))
            {
                window.Left = settings.FloatingTimerLeft.Value;
                window.Top = settings.FloatingTimerTop.Value;
                return;
            }

            const double margin = 16;
            // Width/Height aren't measured until the window has laid out once, so use its
            // last known (or default-content-sized) values; SizeToContent has already run
            // by the time Show() is first called for these to be meaningful, and this only
            // needs to be approximately right since it's just the initial placement.
            double width = window.ActualWidth > 0 ? window.ActualWidth : 160;
            double height = window.ActualHeight > 0 ? window.ActualHeight : 48;

            window.Left = workArea.Right - width - margin;
            window.Top = workArea.Bottom - height - margin;
        }

        private static bool IsOnScreen(double left, double top)
        {
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                var bounds = screen.Bounds;
                if (left >= bounds.Left - 50 && left <= bounds.Right && top >= bounds.Top - 50 && top <= bounds.Bottom)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
