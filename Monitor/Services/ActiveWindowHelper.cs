using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SystemActivityTracker.Services
{
    public static class ActiveWindowHelper
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        public static bool TryGetActiveWindow(out string processName, out string windowTitle)
        {
            processName = string.Empty;
            windowTitle = string.Empty;

            IntPtr handle = GetForegroundWindow();
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                const int nChars = 512;
                var titleBuilder = new StringBuilder(nChars);
                if (GetWindowText(handle, titleBuilder, nChars) > 0)
                {
                    windowTitle = titleBuilder.ToString();
                }

                if (GetWindowThreadProcessId(handle, out uint processId) != 0)
                {
                    using var process = Process.GetProcessById((int)processId);
                    processName = process.ProcessName;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
