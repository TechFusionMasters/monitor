using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using SystemActivityTracker.Models;
using SystemActivityTracker.Utilities;
using SystemActivityTracker.ViewModels;

namespace SystemActivityTracker.Views
{
    public partial class UiBMainWindow : Window
    {
        private bool _isExplicitExit;
        private bool _didInitialRefresh;
        private bool _isUiSwap;

        public UiBMainWindow()
        {
            InitializeComponent();

            if (System.Windows.Application.Current is App app)
            {
                try
                {
                    DataContext = app.Services.GetRequiredService<MainWindowViewModel>();
                }
                catch
                {
                    DataContext = new MainWindowViewModel(app.TrackingService, app.SettingsService);
                }

                try
                {
                    var settings = app.SettingsService?.Load();
                    if (settings != null)
                    {
                        SetUiModeComboSelection(settings.UiMode);
                    }
                }
                catch
                {
                }
            }
            else
            {
                DataContext = new MainWindowViewModel(null, null);
            }

            Loaded += UiBMainWindow_Loaded;
        }

        private void UiBMainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_didInitialRefresh)
            {
                return;
            }

            _didInitialRefresh = true;

            if (DataContext is MainWindowViewModel vm && vm.SelectedTabIndex < 0)
            {
                vm.SelectedTabIndex = 0;
            }

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
            (System.Windows.Application.Current as App)?.FloatingTimerService?.Show();
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
            (System.Windows.Application.Current as App)?.FloatingTimerService?.Hide();
        }

        // Native minimize (taskbar button, not the tray-hiding X close) doesn't go through
        // HideToTray/RestoreFromTray at all, so the floating timer needs its own hook here
        // to show on minimize and hide on restore.
        protected override void OnStateChanged(System.EventArgs e)
        {
            base.OnStateChanged(e);

            if (WindowState == WindowState.Minimized)
            {
                (System.Windows.Application.Current as App)?.FloatingTimerService?.Show();
            }
            else if (IsVisible)
            {
                (System.Windows.Application.Current as App)?.FloatingTimerService?.Hide();
            }
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
                if (app.CloseTrackingService != null)
                {
                    app.CloseTrackingService.IsUserInitiatedExit = true;
                }
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

            if (_isExplicitExit || _isUiSwap)
            {
                return;
            }

            if (System.Windows.Application.Current is App app && !app.IsShuttingDown)
            {
                e.Cancel = true;
                HideToTray();
            }
        }

        private void UiModeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (sender is not System.Windows.Controls.ComboBox combo)
            {
                return;
            }

            if (combo.SelectedItem is not System.Windows.Controls.ComboBoxItem item)
            {
                return;
            }

            if (item.Tag is not string mode || string.IsNullOrWhiteSpace(mode))
            {
                return;
            }

            if (!string.Equals(mode, UiModes.UIA, System.StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(mode, UiModes.UIB, System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (System.Windows.Application.Current is not App app)
            {
                return;
            }

            var settingsService = app.SettingsService;
            if (settingsService == null)
            {
                return;
            }

            AppSettings settings;
            try
            {
                settings = settingsService.Load();
            }
            catch
            {
                settings = new AppSettings();
            }

            if (string.Equals(settings.UiMode, mode, System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            settings.UiMode = mode;
            try
            {
                settingsService.Save(settings);
            }
            catch
            {
            }

            _isUiSwap = true;
            app.SwitchUiMode(mode, DataContext as MainWindowViewModel);
        }

        private void SetUiModeComboSelection(string mode)
        {
            if (UiModeComboBox != null)
            {
                SetComboSelection(UiModeComboBox, mode);
            }
        }

        private static void SetComboSelection(System.Windows.Controls.ComboBox comboBox, string mode)
        {
            foreach (var obj in comboBox.Items)
            {
                if (obj is System.Windows.Controls.ComboBoxItem item && item.Tag is string tag &&
                    string.Equals(tag, mode, System.StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }
        }
    }
}
