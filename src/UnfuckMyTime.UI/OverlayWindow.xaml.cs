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
                    ResetText.Text = "";
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
    }
}
