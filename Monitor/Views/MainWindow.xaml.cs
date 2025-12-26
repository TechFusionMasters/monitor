using System.ComponentModel;
using System.Windows;
using SystemActivityTracker.ViewModels;

namespace SystemActivityTracker.Views
{
    public partial class MainWindow : Window
    {
        private bool _isExplicitExit;

        public MainWindow()
        {
            InitializeComponent();
            if (System.Windows.Application.Current is App app && app.TrackingService is { } trackingService)
            {
                DataContext = new MainWindowViewModel(trackingService, app.SettingsService);
            }
            else
            {
                DataContext = new MainWindowViewModel(null, null);
            }
        }

        private void HideToTray()
        {
            ShowInTaskbar = false;
            Hide();
        }

        private void RestoreFromTray()
        {
            ShowInTaskbar = true;

            if (!IsVisible)
            {
                Show();
            }

            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Activate();
        }

        internal void RestoreFromTrayInternal()
        {
            RestoreFromTray();
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            _isExplicitExit = true;

            if (System.Windows.Application.Current is App app)
            {
                app.IsShuttingDown = true;
                app.Shutdown();
            }
            else
            {
                System.Windows.Application.Current?.Shutdown();
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            if (_isExplicitExit)
            {
                return;
            }

            if (System.Windows.Application.Current is App app && !app.IsShuttingDown)
            {
                e.Cancel = true;
                HideToTray();
            }
        }
    }
}
