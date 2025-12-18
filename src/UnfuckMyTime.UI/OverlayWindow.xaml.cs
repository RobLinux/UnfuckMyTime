using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using UnfuckMyTime.Core.Models;

namespace UnfuckMyTime.UI
{
    public partial class OverlayWindow : Window
    {
        public event EventHandler? PauseRequested;
        public event EventHandler? StopRequested;
        public string? MainAppProcessName { get; set; }

        private int _stopClickCount = 0;
        private DispatcherTimer _confirmationResetTimer;

        public OverlayWindow()
        {
            InitializeComponent();
            _confirmationResetTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _confirmationResetTimer.Tick += (s, e) => ResetStopButton();
        }

        public void UpdateStatus(string timerText, SessionStatusSnapshot? status)
        {
            TimerText.Text = timerText;

            if (status != null)
            {
                if (status.IsPaused)
                {
                    SlackText.Text = "PAUSED";
                    SlackProgressBar.Value = 100;
                    SlackProgressBar.Foreground = System.Windows.Media.Brushes.Gray;
                    SlackProgressBar.Foreground = System.Windows.Media.Brushes.Gray;
                    ResetText.Text = "";
                    BackToWorkBtn.Visibility = Visibility.Collapsed;
                }
                else
                {
                    // Update Slack Progress
                    double total = status.TotalSlackBudget.TotalSeconds;
                    double remaining = status.SlackRemaining.TotalSeconds;
                    double percent = total > 0 ? (remaining / total) : 0;

                    SlackProgressBar.Value = percent * 100;

                    // Color coding
                    if (percent > 0.5) SlackProgressBar.Foreground = System.Windows.Media.Brushes.LightGreen;
                    else if (percent > 0.2) SlackProgressBar.Foreground = System.Windows.Media.Brushes.Orange;
                    else SlackProgressBar.Foreground = System.Windows.Media.Brushes.Red;

                    SlackText.Text = $"{remaining:F0}s";

                    // Update Reset Text
                    if (status.TimeUntilReset.HasValue)
                    {
                        ResetText.Text = $"Reset in {status.TimeUntilReset.Value.TotalSeconds:F0}s...";
                        ResetText.Foreground = System.Windows.Media.Brushes.Orange;
                    }
                    else if (status.IsDistracted)
                    {
                        ResetText.Text = "DISTRACTED!";
                        ResetText.Foreground = System.Windows.Media.Brushes.Red;
                    }
                    else
                    {
                        ResetText.Text = ""; // Clean
                    }

                    // Back To Work Visibility
                    // Show if we are on a WRONG app (IsCurrentAppAllowed == false) [meaning we are burning slack or distracted]
                    // provided we have a main app to go back to.
                    if (!string.IsNullOrEmpty(MainAppProcessName) && !status.IsCurrentAppAllowed)
                    {
                        BackToWorkBtn.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        BackToWorkBtn.Visibility = Visibility.Collapsed;
                    }
                }
            }
        }

        public void SetPauseState(bool isPaused)
        {
            PauseBtn.Content = isPaused ? "▶" : "⏸";
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void PauseBtn_Click(object sender, RoutedEventArgs e)
        {
            PauseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void StopBtn_Click(object sender, RoutedEventArgs e)
        {
            _stopClickCount++;

            if (_stopClickCount == 1)
            {
                // Confirmation State
                StopBtn.Content = "❌ Confirm?";
                _confirmationResetTimer.Start();
            }
            else if (_stopClickCount >= 2)
            {
                // Action
                StopRequested?.Invoke(this, EventArgs.Empty);
                ResetStopButton();
            }
        }

        private void ResetStopButton()
        {
            _stopClickCount = 0;
            StopBtn.Content = "⏹";
            _confirmationResetTimer.Stop();
        }

        public void UpdateTimer(string text)
        {
            Dispatcher.Invoke(() =>
            {
                TimerText.Text = text;
                // Keep blocked text in sync
                if (BlockedTimerText != null) BlockedTimerText.Text = text;
            });
        }
        public void SetFullBlock(bool active)
        {
            Dispatcher.Invoke(() =>
            {
                if (active)
                {
                    this.SizeToContent = SizeToContent.Manual;
                    this.WindowStyle = WindowStyle.None;
                    this.ResizeMode = ResizeMode.NoResize;

                    // Remove Corner Radius for full screen
                    if (MainBorder != null) MainBorder.CornerRadius = new CornerRadius(0);

                    this.Topmost = true;

                    // Force Cover
                    this.Left = SystemParameters.VirtualScreenLeft;
                    this.Top = SystemParameters.VirtualScreenTop;
                    this.Width = SystemParameters.VirtualScreenWidth;
                    this.Height = SystemParameters.VirtualScreenHeight;

                    FullBlockLayer.Visibility = Visibility.Visible;
                    NormalContent.Visibility = Visibility.Collapsed;

                    // Sync the timers immediately
                    BlockedTimerText.Text = TimerText.Text;
                }
                else
                {
                    FullBlockLayer.Visibility = Visibility.Collapsed;
                    NormalContent.Visibility = Visibility.Visible;

                    // Restore Radius
                    if (MainBorder != null) MainBorder.CornerRadius = new CornerRadius(12);

                    this.WindowState = WindowState.Normal;
                    this.ResizeMode = ResizeMode.CanResize;
                    this.SizeToContent = SizeToContent.WidthAndHeight;

                    this.Topmost = true;
                }
            });
        }

        private void BackToWorkBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(MainAppProcessName))
            {
                UnfuckMyTime.Infrastructure.Helpers.WindowHelper.BringProcessToFront(MainAppProcessName);
            }
        }
    }
}
