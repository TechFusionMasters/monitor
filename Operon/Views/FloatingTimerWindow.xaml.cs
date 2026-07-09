using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using SystemActivityTracker.ViewModels;

namespace SystemActivityTracker.Views
{
    // Small always-on-top chip showing the same live running-time value as the main
    // window's header timer (bound to MainWindowViewModel.HeaderRunningTimerText, so it
    // never computes its own duration — see FloatingTimerService for lifecycle/positioning).
    public partial class FloatingTimerWindow : Window
    {
        public event EventHandler? ReopenRequested;
        public event EventHandler? PositionChanged;
        public event EventHandler? HideRequested;
        public event EventHandler? ShutdownRequested;

        // Reacting to Deactivated/ContextMenu.Closed (an earlier attempt at this fix) turned
        // out to depend on an event ordering that isn't actually guaranteed — right-click
        // then immediately clicking the taskbar could still leave the window behind it, or
        // even visibly jump it, because the taskbar gets special Z-order treatment among
        // topmost windows that a single reactive Topmost toggle doesn't reliably beat.
        // Instead, this periodically re-asserts HWND_TOPMOST directly via Win32 regardless
        // of *why* the window might have lost it, which is unconditional and doesn't depend
        // on catching the right event at the right moment.
        private readonly DispatcherTimer _topmostTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };

        public FloatingTimerWindow()
        {
            InitializeComponent();
            _topmostTimer.Tick += (_, __) => ReassertTopmostNative();
            IsVisibleChanged += (_, __) =>
            {
                if (IsVisible)
                {
                    ReassertTopmostNative();
                    _topmostTimer.Start();
                }
                else
                {
                    _topmostTimer.Stop();
                }
            };
            Closed += (_, __) => _topmostTimer.Stop();
        }

        private void ReassertTopmostNative()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            NativeMethods.SetWindowPos(
                hwnd,
                NativeMethods.HWND_TOPMOST,
                0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }

        private static class NativeMethods
        {
            [DllImport("user32.dll", SetLastError = true)]
            public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

            public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
            public const uint SWP_NOMOVE = 0x0002;
            public const uint SWP_NOSIZE = 0x0001;
            public const uint SWP_NOACTIVATE = 0x0010;
        }

        private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount >= 2)
            {
                ReopenRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            try
            {
                DragMove();
                PositionChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (InvalidOperationException)
            {
                // DragMove throws if called outside a mouse-down gesture (e.g. touch input
                // edge cases) — the window just stays put, nothing to recover.
            }
        }

        private void OpenMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ReopenRequested?.Invoke(this, EventArgs.Empty);
        }

        private void HideMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // Flips the same VM property the Settings-tab checkbox binds to — its setter
            // already persists to disk immediately, so there's no separate save step needed
            // here (see MainWindowViewModel.ShowFloatingTimerOnMinimize).
            if (DataContext is MainWindowViewModel vm)
            {
                vm.ShowFloatingTimerOnMinimize = false;
            }

            HideRequested?.Invoke(this, EventArgs.Empty);
        }

        private void ShutdownMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                this,
                "Are you sure you want to exit Operon? Tracking will stop.",
                "Shutdown Operon",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (result == MessageBoxResult.Yes)
            {
                ShutdownRequested?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
