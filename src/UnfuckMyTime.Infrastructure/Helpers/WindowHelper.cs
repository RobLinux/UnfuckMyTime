using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace UnfuckMyTime.Infrastructure.Helpers
{
    public static class WindowHelper
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_SHOWWINDOW = 0x0040;

        public static async Task ShakeActiveWindowAsync(double intensityMultiplier = 1.0)
        {
            var hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero) return;

            if (GetWindowRect(hWnd, out var rect))
            {
                var width = rect.Right - rect.Left;
                var height = rect.Bottom - rect.Top;
                var originalLeft = rect.Left;
                var originalTop = rect.Top;

                var shakeAmount = (int)(10 * intensityMultiplier);
                var delay = 30;
                
                // Base 5 iterations (approx 300ms total). Intensity increases this.
                int iterations = (int)(5 * intensityMultiplier);

                // Wiggle Logic
                for (int i = 0; i < iterations; i++)
                {
                    SetWindowPos(hWnd, IntPtr.Zero, originalLeft + shakeAmount, originalTop, 0, 0, SWP_NOSIZE | SWP_NOZORDER);
                    await Task.Delay(delay);
                    SetWindowPos(hWnd, IntPtr.Zero, originalLeft - shakeAmount, originalTop, 0, 0, SWP_NOSIZE | SWP_NOZORDER);
                    await Task.Delay(delay);
                }

                // Restore
                SetWindowPos(hWnd, IntPtr.Zero, originalLeft, originalTop, 0, 0, SWP_NOSIZE | SWP_NOZORDER);
            }
        }

        public static void BringProcessToFront(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return;

            // Remove .exe extension if present roughly
            if (processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                processName = processName.Substring(0, processName.Length - 4);
            }

            var processes = Process.GetProcessesByName(processName);
            var process = processes.FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);

            if (process != null)
            {
                var hWnd = process.MainWindowHandle;
                ShowWindow(hWnd, SW_RESTORE);
                SetForegroundWindow(hWnd);
            }
        }
    }
}
