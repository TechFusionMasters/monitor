using System;
using System.Windows;
using System.Windows.Forms;
using SystemActivityTracker.Models;
using SystemActivityTracker.ViewModels;
using SystemActivityTracker.Views;

namespace SystemActivityTracker.Services
{
    public class TrayIconService : IDisposable
    {
        private readonly App _app;
        private readonly TrackingService _trackingService;
        private readonly NotifyIcon _notifyIcon;
        private readonly ToolStripMenuItem _todayTotalItem;
        private readonly ToolStripMenuItem _activeItem;
        private readonly ToolStripMenuItem _offlineItem;
        private readonly ToolStripMenuItem _statusItem;
        private bool _disposed;

        public TrayIconService(App app, TrackingService trackingService)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _trackingService = trackingService ?? throw new ArgumentNullException(nameof(trackingService));

            static string GetString(string key, string fallback)
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

            _notifyIcon = new NotifyIcon
            {
                Text = GetString("AppName", "Operon"),
                Icon = GetAppIcon(),
                Visible = true
            };

            // Same labels/values as the floating timer's quick-info menu (FloatingTimerWindow.xaml)
            // — both read from the identical MainWindowViewModel.TodayQuick* properties, just
            // WinForms can't data-bind them, so they're refreshed on-demand in the Opening
            // handler below instead of continuously.
            var todayHeaderItem = new ToolStripMenuItem("Today") { Enabled = false, Font = new System.Drawing.Font(Control.DefaultFont, System.Drawing.FontStyle.Bold) };
            _todayTotalItem = new ToolStripMenuItem("Today Total: —") { Enabled = false, Font = new System.Drawing.Font(Control.DefaultFont, System.Drawing.FontStyle.Bold) };
            _activeItem = new ToolStripMenuItem("Active: —") { Enabled = false };
            _offlineItem = new ToolStripMenuItem("Offline Work: —") { Enabled = false };
            _statusItem = new ToolStripMenuItem("Status: —") { Enabled = false };

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add(todayHeaderItem);
            contextMenu.Items.Add(_todayTotalItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(_activeItem);
            contextMenu.Items.Add(_offlineItem);
            contextMenu.Items.Add(_statusItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(GetString("TrayMenuOpen", "Open"), null, (_, __) => ShowMainWindow());
            contextMenu.Items.Add(GetString("TrayMenuExit", "Exit"), null, (_, __) => ExitApplication());
            contextMenu.Opening += (_, __) => RefreshQuickInfo();

            _notifyIcon.ContextMenuStrip = contextMenu;
            _notifyIcon.DoubleClick += (_, __) => ShowMainWindow();
        }

        // Pulls fresh values from whichever MainWindowViewModel is currently active right
        // before the menu displays — matches the on-open-snapshot approach used for the
        // floating timer's menu; there's no per-second ticking here since a WinForms
        // ContextMenuStrip isn't open long enough for that to matter.
        private void RefreshQuickInfo()
        {
            if (_app.MainWindow?.DataContext is MainWindowViewModel vm)
            {
                _todayTotalItem.Text = $"Today Total: {vm.TodayQuickTotalText}";
                _activeItem.Text = $"Active: {vm.TodayQuickActiveText}";
                _offlineItem.Text = $"Offline Work: {vm.TodayQuickOfflineText}";
                _statusItem.Text = $"Status: {vm.TodayQuickStatusText}";
            }
        }

        // Loads operon.ico directly from the WPF Resource embedded in the assembly (see
        // Operon.csproj's Resource ItemGroup) — the same source every Window.Icon in the
        // app points to via pack://application:,,,/Assets/operon.ico, so the tray icon is
        // guaranteed to be the identical image rather than depending on however the exe's
        // own Win32 icon resource happens to extract (ApplicationIcon embeds the same file
        // there too, but extraction from a running exe can behave inconsistently, e.g. with
        // icon caching, across environments).
        private static System.Drawing.Icon GetAppIcon()
        {
            try
            {
                var resourceInfo = System.Windows.Application.GetResourceStream(
                    new Uri("pack://application:,,,/Assets/operon.ico", UriKind.Absolute));

                if (resourceInfo?.Stream != null)
                {
                    using (resourceInfo.Stream)
                    {
                        return new System.Drawing.Icon(resourceInfo.Stream);
                    }
                }
            }
            catch
            {
            }

            return System.Drawing.SystemIcons.Application;
        }

        // Internal (not private): FloatingTimerService's double-click handler reuses this
        // exact restore logic so reopening from the floating timer behaves identically to
        // reopening from the tray icon (same window either minimized or hidden-to-tray).
        internal void ShowMainWindow()
        {
            _app.Dispatcher.Invoke(() =>
            {
                if (_app.MainWindow == null)
                {
                    try
                    {
                        var settings = _app.SettingsService?.Load() ?? new AppSettings();
                        _app.MainWindow = _app.CreateMainWindowForMode(settings.UiMode, null);
                    }
                    catch
                    {
                        _app.MainWindow = new UiAMainWindow();
                    }
                }

                if (_app.MainWindow is UiAMainWindow mw)
                {
                    mw.RestoreFromTrayInternal();
                }
                else if (_app.MainWindow is UiBMainWindow cmw)
                {
                    cmw.RestoreFromTrayInternal();
                }
                else
                {
                    _app.MainWindow.ShowInTaskbar = true;

                    if (!_app.MainWindow.IsVisible)
                    {
                        _app.MainWindow.Show();
                    }

                    if (_app.MainWindow.WindowState == WindowState.Minimized)
                    {
                        _app.MainWindow.WindowState = WindowState.Normal;
                    }

                    _app.MainWindow.Activate();
                }
            });
        }

        // Internal (not private): FloatingTimerService's "Shutdown Operon" quick action
        // reuses this exact exit path (flush the current record, then shut down) so it
        // behaves identically to the tray menu's "Exit" and the main window's Exit button.
        internal void ExitApplication()
        {
            _app.Dispatcher.Invoke(() =>
            {
                if (_app.MainWindow is UiAMainWindow mw)
                {
                    mw.RunRefreshCommandInternal();
                }
                else if (_app.MainWindow is UiBMainWindow cmw)
                {
                    cmw.RunRefreshCommandInternal();
                }

                _app.IsShuttingDown = true;
                _app.Shutdown();
            });
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
