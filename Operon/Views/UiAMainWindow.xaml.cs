using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using SystemActivityTracker.Utilities;
using SystemActivityTracker.ViewModels;

namespace SystemActivityTracker.Views
{
    public partial class UiAMainWindow : Window
    {
        private bool _isExplicitExit;
        private bool _didInitialRefresh;
        private bool _isUiSwap;
        private bool _isInitializingUiMode;

        public UiAMainWindow()
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
                        _isInitializingUiMode = true;
                        SetComboSelection(UiModeComboBox, settings.UiMode);
                        SetComboSelection(UiModeComboBoxHeader, settings.UiMode);
                        _isInitializingUiMode = false;
                    }
                }
                catch
                {
                    _isInitializingUiMode = false;
                }
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
            if (_isInitializingUiMode)
            {
                return;
            }

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

            SystemActivityTracker.Models.AppSettings settings;
            try
            {
                settings = settingsService.Load();
            }
            catch
            {
                settings = new SystemActivityTracker.Models.AppSettings();
            }

            if (string.Equals(settings.UiMode, mode, System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Keep both selectors in sync
            _isInitializingUiMode = true;
            try
            {
                if (!ReferenceEquals(combo, UiModeComboBox))
                {
                    SetComboSelection(UiModeComboBox, mode);
                }
                if (!ReferenceEquals(combo, UiModeComboBoxHeader))
                {
                    SetComboSelection(UiModeComboBoxHeader, mode);
                }
            }
            finally
            {
                _isInitializingUiMode = false;
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

        #region Activity Tooltip Click Handlers

        private System.Windows.Controls.ToolTip? _activeTooltip;

        /// <summary>
        /// Handles click on Total Active text to pin/unpin tooltip
        /// </summary>
        private void OnTotalActiveClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBlock textBlock)
            {
                ToggleTooltipPin(textBlock.ToolTip as System.Windows.Controls.ToolTip, textBlock);
            }
            e.Handled = true;
        }

        /// <summary>
        /// Handles click on Activity Bar to pin/unpin tooltip
        /// </summary>
        private void OnActivityBarClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is SystemActivityTracker.Controls.HorizontalActivityBar bar)
            {
                ToggleTooltipPin(bar.ToolTip as System.Windows.Controls.ToolTip, bar);
            }
            e.Handled = true;
        }

        /// <summary>
        /// Toggles tooltip pinned state. If same tooltip is already pinned, close it.
        /// If different tooltip or none pinned, open and pin this one.
        /// </summary>
        private void ToggleTooltipPin(System.Windows.Controls.ToolTip? tooltip, System.Windows.DependencyObject? placementTarget = null)
        {
            if (tooltip == null) return;

            // If this tooltip is already open (pinned), close it
            if (_activeTooltip == tooltip && tooltip.IsOpen)
            {
                tooltip.IsOpen = false;
                _activeTooltip = null;
            }
            else
            {
                // Close any previously pinned tooltip
                if (_activeTooltip != null && _activeTooltip != tooltip)
                {
                    _activeTooltip.IsOpen = false;
                }

                // Ensure tooltip has proper placement target for positioning
                if (placementTarget != null && tooltip.PlacementTarget == null)
                {
                    tooltip.PlacementTarget = placementTarget as System.Windows.UIElement;
                    tooltip.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                }

                // Open and pin this tooltip
                tooltip.IsOpen = true;
                _activeTooltip = tooltip;
            }
        }

        // Bubbling MouseDown: clicks on the bar/text that owns the pinned tooltip are
        // already marked e.Handled=true by OnActivityBarClick/OnTotalActiveClick, so this
        // never fires for those (their own toggle logic manages open/close instead) — only
        // clicks elsewhere in the window reach here and close the pinned tooltip.
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            CloseActiveTooltip();
        }

        private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _activeTooltip != null)
            {
                CloseActiveTooltip();
                e.Handled = true;
            }
        }

        private void CloseActiveTooltip()
        {
            if (_activeTooltip != null)
            {
                _activeTooltip.IsOpen = false;
                _activeTooltip = null;
            }
        }

        #endregion

        /// <summary>
        /// Handles Delete button PreviewMouseLeftButtonDown to execute delete command without selecting the row.
        /// This prevents the deleted row's values from loading into the edit form.
        /// </summary>
        private void DeleteButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.CommandParameter is SystemActivityTracker.Models.ManualTaskEntry entry)
            {
                if (DataContext is MainWindowViewModel viewModel)
                {
                    viewModel.DeleteManualTaskRowCommand.Execute(entry);
                }
            }
            // Mark event as handled to prevent DataGrid row selection
            e.Handled = true;
        }
    }
}
