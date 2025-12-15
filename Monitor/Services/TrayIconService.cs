using System;
using System.Windows;
using System.Windows.Forms;
using SystemActivityTracker.Views;

namespace SystemActivityTracker.Services
{
    public class TrayIconService : IDisposable
    {
        private readonly App _app;
        private readonly TrackingService _trackingService;
        private readonly NotifyIcon _notifyIcon;
        private bool _disposed;

        public TrayIconService(App app, TrackingService trackingService)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _trackingService = trackingService ?? throw new ArgumentNullException(nameof(trackingService));

            _notifyIcon = new NotifyIcon
            {
                Text = "System Activity Tracker",
                Icon = System.Drawing.SystemIcons.Application,
                Visible = true
            };

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Open", null, (_, __) => ShowMainWindow());
            contextMenu.Items.Add("Start Tracking", null, (_, __) => _app.Dispatcher.Invoke(() => _trackingService.Start()));
            contextMenu.Items.Add("Stop Tracking", null, (_, __) => _app.Dispatcher.Invoke(() => _trackingService.Stop()));
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("Exit", null, (_, __) => ExitApplication());

            _notifyIcon.ContextMenuStrip = contextMenu;
            _notifyIcon.DoubleClick += (_, __) => ShowMainWindow();
        }

        private void ShowMainWindow()
        {
            _app.Dispatcher.Invoke(() =>
            {
                if (_app.MainWindow == null)
                {
                    _app.MainWindow = new MainWindow();
                }

                if (!_app.MainWindow.IsVisible)
                {
                    _app.MainWindow.Show();
                }

                if (_app.MainWindow.WindowState == WindowState.Minimized)
                {
                    _app.MainWindow.WindowState = WindowState.Normal;
                }

                _app.MainWindow.Activate();
            });
        }

        private void ExitApplication()
        {
            _app.Dispatcher.Invoke(() =>
            {
                _trackingService.Shutdown();
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

        ~TrayIconService()
        {
            Dispose();
        }
    }
}
