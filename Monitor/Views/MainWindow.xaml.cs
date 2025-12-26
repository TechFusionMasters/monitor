using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using SystemActivityTracker.ViewModels;

namespace SystemActivityTracker.Views
{
    public partial class MainWindow : Window
    {
        private bool _isExplicitExit;
        private bool _didInitialRefresh;

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

            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_didInitialRefresh)
            {
                return;
            }

            _didInitialRefresh = true;
            RunRefreshCommand();
        }

        private void RunRefreshCommand()
        {
            if (DataContext is not MainWindowViewModel vm)
            {
                return;
            }

            ICommand command = vm.RefreshCommand;
            if (command.CanExecute(null))
            {
                command.Execute(null);
            }
        }

        private void HideToTray()
        {
            RunRefreshCommand();
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

        internal void RunRefreshCommandInternal()
        {
            RunRefreshCommand();
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            RunRefreshCommand();
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
