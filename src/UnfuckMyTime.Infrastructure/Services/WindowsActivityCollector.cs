using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnfuckMyTime.Core.Models;

namespace UnfuckMyTime.Infrastructure.Services
{
    public class WindowsActivityCollector : IDisposable
    {
        public event EventHandler<ActivitySnapshot>? ActivityChanged;

        private readonly System.Timers.Timer _timer;
        private IntPtr _lastHwnd = IntPtr.Zero;
        private readonly string _debugLogPath = @"C:\Users\louka\.gemini\antigravity\debug_hook.txt";
        
        public WindowsActivityCollector()
        {
            Log($"Initializing Collector (Polling Mode)...");
            
            // Poll every 1 second as requested
            _timer = new System.Timers.Timer(1000);
            _timer.Elapsed += Timer_Elapsed;
            _timer.AutoReset = true;
            _timer.Start();
        }

        private void Timer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            try
            {
                var hwnd = GetForegroundWindow();
                
                // If the window handle hasn't changed, we *could* skip.
                // But sometimes the Title/URL changes within the same HWND (e.g. Chrome tabs).
                // So we should capture snapshot and compare content, or just fire anyway?
                // Given the user wants to "see if it fits", let's fire if details change OR if it's a new HWND.
                // For now, let's just always capture and let the UI deduplicate log if needed, 
                // OR implementation: Only fire if something meaningful changed.

                var snapshot = CaptureSnapshot(hwnd);

                // Deduping logic locally to avoid event spam
                // (Optional: if user wants *every* second scan to be logged, remove this check.
                // But efficient systems usually dedup.)
                // Let's dedup based on Process + Title + Url
                
                ActivityChanged?.Invoke(this, snapshot);
            }
            catch (Exception ex)
            {
                Log($"[ERROR] Poll error: {ex.Message}");
            }
        }

        private void Log(string message)
        {
            try
            {
                File.AppendAllText(_debugLogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
            }
            catch { }
        }

        private ActivitySnapshot CaptureSnapshot(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return new ActivitySnapshot { Timestamp = DateTime.Now, ProcessName = "Unknown" };

            var processName = GetProcessName(hwnd);
            var windowTitle = GetWindowTitle(hwnd);
            var url = string.Empty;

            if (IsBrowser(processName))
            {
                url = GetBrowserUrl(hwnd);
            }

            return new ActivitySnapshot
            {
                Timestamp = DateTime.Now,
                ProcessName = processName,
                WindowTitle = windowTitle,
                Url = url
            };
        }

        private bool IsBrowser(string processName)
        {
            var name = processName.ToLowerInvariant();
            return name == "chrome" || name == "msedge" || name == "brave" || name == "firefox" || name == "opera" || name == "vivaldi";
        }

        private string GetBrowserUrl(IntPtr hwnd)
        {
            try
            {
                // Note: AutomationElement.FromHandle can be slow/blocking. 
                var element = System.Windows.Automation.AutomationElement.FromHandle(hwnd);
                if (element == null) return string.Empty;

                var condition = new System.Windows.Automation.PropertyCondition(
                    System.Windows.Automation.AutomationElement.ControlTypeProperty,
                    System.Windows.Automation.ControlType.Edit);

                // Optimization: Limit depth or use caching? 
                // For polling, doing this every second might be heavy if lots of elements.
                // But for a single foreground window, it is usually acceptable.
                var elementCollection = element.FindAll(System.Windows.Automation.TreeScope.Descendants, condition);

                foreach (System.Windows.Automation.AutomationElement edit in elementCollection)
                {
                    var name = edit.Current.Name;
                    if (name.Contains("Address") || name.Contains("search") || name.Contains("Search"))
                    {
                        if (edit.TryGetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern, out object pattern))
                        {
                            var valuePattern = (System.Windows.Automation.ValuePattern)pattern;
                            var url = valuePattern.Current.Value;
                            if (!string.IsNullOrWhiteSpace(url)) return url;
                        }
                    }
                }
            }
            catch
            {
                // Ignore automation errors
            }
            return string.Empty;
        }

        private string GetWindowTitle(IntPtr hwnd)
        {
            const int nChars = 256;
            StringBuilder Buff = new StringBuilder(nChars);
            if (GetWindowText(hwnd, Buff, nChars) > 0)
            {
                return Buff.ToString();
            }
            return string.Empty;
        }

        private string GetProcessName(IntPtr hwnd)
        {
            try
            {
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                var process = Process.GetProcessById((int)pid);
                return process.ProcessName;
            }
            catch
            {
                return "Unknown";
            }
        }

        public void Dispose()
        {
            _timer?.Stop();
            _timer?.Dispose();
        }

        // P/Invoke
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    }
}
