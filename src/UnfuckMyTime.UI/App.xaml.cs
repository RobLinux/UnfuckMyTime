using System;
using System.Drawing;
using System.IO;
using System.Windows;
using UnfuckMyTime.Core.Models;
using UnfuckMyTime.Core.Services;
using UnfuckMyTime.Infrastructure.Helpers;
using UnfuckMyTime.Infrastructure.Services;
using Forms = global::System.Windows.Forms;

namespace UnfuckMyTime.UI
{
    public partial class App : System.Windows.Application
    {
        private Forms.NotifyIcon? _notifyIcon;
        private WindowsActivityCollector? _collector;
        private MainWindow? _mainWindow;
        private readonly SessionManager _sessionManager = new(new WindowsMediaController());

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _sessionManager.DistractionDetected += OnDistractionDetected;

            _mainWindow = new MainWindow(_sessionManager);

            // Setup Tray Icon
            _notifyIcon = new Forms.NotifyIcon();
            _notifyIcon.Icon = SystemIcons.Application; // placeholder icon
            _notifyIcon.Visible = true;
            _notifyIcon.Text = "UnfuckMyTime: Idle";
            _notifyIcon.DoubleClick += (s, args) => ShowMainWindow();

            var contextMenu = new Forms.ContextMenuStrip();
            contextMenu.Items.Add("Open Dashboard", null, (s, args) => ShowMainWindow());
            contextMenu.Items.Add("Exit", null, (s, args) => ExitApplication());
            _notifyIcon.ContextMenuStrip = contextMenu;

            // Start Activity Collector
            _collector = new WindowsActivityCollector();
            _collector.ActivityChanged += OnActivityCaptured;

            ShowMainWindow(); // Show immediately on startup
        }

        private void OnActivityCaptured(object? sender, ActivitySnapshot e)
        {
            // 1. Evaluate Logic
            _sessionManager.EvaluateActivity(e);

            // 2. Update UI
            Dispatcher.Invoke(() =>
            {
                if (_mainWindow != null && _mainWindow.IsVisible)
                {
                    _mainWindow.UpdateActivityLog(e);
                }
            });
        }

        private void OnDistractionDetected(object? sender, DistractionEvent e)
        {
            Dispatcher.Invoke(() =>
            {
                if (e.Level == InterventionLevel.WindowWiggle)
                {
                    _ = WindowHelper.ShakeActiveWindowAsync(e.Intensity);
                    _notifyIcon?.ShowBalloonTip(3000, "Focus Alert", e.Message, Forms.ToolTipIcon.Warning);
                }
                else
                {
                    _notifyIcon?.ShowBalloonTip(3000, "Focus Alert", e.Message, Forms.ToolTipIcon.Warning);
                }
            });
        }

        private void ShowMainWindow()
        {
            if (_mainWindow == null) return;

            if (_mainWindow.IsVisible)
            {
                if (_mainWindow.WindowState == WindowState.Minimized)
                    _mainWindow.WindowState = WindowState.Normal;
                _mainWindow.Activate();
            }
            else
            {
                _mainWindow.Show();
            }
        }

        private void ExitApplication()
        {
            if (_notifyIcon != null) _notifyIcon.Visible = false;
            _collector?.Dispose();
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _notifyIcon?.Dispose();
            _collector?.Dispose();
            base.OnExit(e);
        }
    }
}
