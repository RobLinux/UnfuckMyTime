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

        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        private const uint EVENT_SYSTEM_FOREGROUND = 3;
        private const uint WINEVENT_OUTOFCONTEXT = 0;

        private readonly WinEventDelegate _delegate;
        private readonly IntPtr _hookData;
        private readonly string _debugLogPath = @"C:\Users\louka\.gemini\antigravity\debug_hook.txt";
        private bool _disposed;

        public WindowsActivityCollector()
        {
            Log($"Initializing Collector...");
            _delegate = new WinEventDelegate(WinEventProc);
            _hookData = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _delegate, 0, 0, WINEVENT_OUTOFCONTEXT);
            Log($"Hook setup. Handle: {_hookData}");

            if (_hookData == IntPtr.Zero)
            {
                Log($"[ERROR] Failed to set hook! Error: {Marshal.GetLastWin32Error()}");
            }
        }

        private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (eventType == EVENT_SYSTEM_FOREGROUND)
            {
                // Offload to background thread to avoid blocking the UI thread with UI Automation calls
                Task.Run(() =>
                {
                    try
                    {
                        var snapshot = CaptureSnapshot(hwnd);
                        Log($"Captured: {snapshot.ProcessName} - {snapshot.WindowTitle} - {snapshot.Url}");
                        ActivityChanged?.Invoke(this, snapshot);
                    }
                    catch (Exception ex)
                    {
                        Log($"[ERROR] Error processing event: {ex.Message}");
                    }
                });
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
            return name == "chrome" || name == "msedge" || name == "brave" || name == "firefox" || name == "opera";
        }

        private string GetBrowserUrl(IntPtr hwnd)
        {
            try
            {
                // Note: AutomationElement.FromHandle can be slow/blocking. 
                // In production, might want to run this on a separate thread or with a timeout.
                var element = System.Windows.Automation.AutomationElement.FromHandle(hwnd);
                if (element == null) return string.Empty;

                // Broad search for "Edit" control. 
                // Optimization: Chrome/Edge usually have specific AutomationId or Name.
                // "Address and search bar" is common for Chromium.

                var condition = new System.Windows.Automation.PropertyCondition(
                    System.Windows.Automation.AutomationElement.ControlTypeProperty,
                    System.Windows.Automation.ControlType.Edit);

                var elementCollection = element.FindAll(System.Windows.Automation.TreeScope.Descendants, condition);

                foreach (System.Windows.Automation.AutomationElement edit in elementCollection)
                {
                    // Check for common address bar names or values
                    // Chromium: "Address and search bar"
                    // Firefox: "Navigation Toolbar" -> "Search with Google or enter address" (hierarchy varies)

                    var name = edit.Current.Name;

                    // Simple heuristic for now: if it looks like a url (contains '.') or match specific names
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
            if (!_disposed)
            {
                UnhookWinEvent(_hookData);
                _disposed = true;
            }
        }

        // P/Invoke
        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    }
}
