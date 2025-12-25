using System.ComponentModel;
using System.Windows;
using SystemActivityTracker.ViewModels;
using Wpf.Ui.Controls;

namespace SystemActivityTracker.Views
{
    public partial class MainWindow : FluentWindow
    {
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

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            if (System.Windows.Application.Current is App app && !app.IsShuttingDown)
            {
                e.Cancel = true;
                Hide();
            }
        }
    }
}
