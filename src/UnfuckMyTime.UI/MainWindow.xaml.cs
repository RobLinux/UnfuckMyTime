using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using UnfuckMyTime.Core.Models;
using UnfuckMyTime.Core.Services;
using UnfuckMyTime.Infrastructure.Services;

namespace UnfuckMyTime.UI
{
    public partial class MainWindow : Window
    {
        private GeneratedPlan? _currentGeneratedPlan;
        private readonly IAIGoalService _aiService;
        private readonly SessionManager _sessionManager;
        private DispatcherTimer _sessionTimer;
        private DateTime _sessionStartTime;
        private DateTime _pauseStartTime;

        public MainWindow(SessionManager sessionManager)
        {
            InitializeComponent();
            _sessionManager = sessionManager;
            _aiService = new OpenAIGoalService();

            _sessionTimer = new DispatcherTimer();
            _sessionTimer.Interval = TimeSpan.FromSeconds(1);
            _sessionTimer.Tick += SessionTimer_Tick;

            LoadConfiguration();
        }

        // Fallback constructor if needed by designer (though usually fails with DI)
        public MainWindow() : this(new SessionManager()) { }

        private void LoadConfiguration()
        {
            try
            {
                // 1. Try reading from C:\temp\key.txt as per user request (User Secret)
                string tempKeyFile = @"C:\temp\key.txt";
                if (System.IO.File.Exists(tempKeyFile))
                {
                    var fileKey = System.IO.File.ReadAllText(tempKeyFile).Trim();
                    if (!string.IsNullOrEmpty(fileKey))
                    {
                        ApiKeyInput.Password = fileKey;
                        return; // Found it, stop here.
                    }
                }

                // 2. Fallback to appsettings
                var basePath = AppDomain.CurrentDomain.BaseDirectory;

                var builder = new ConfigurationBuilder()
                    .SetBasePath(basePath)
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

                var config = builder.Build();
                var key = config["OpenAI:ApiKey"];

                if (!string.IsNullOrEmpty(key))
                {
                    ApiKeyInput.Password = key;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Config load error: {ex.Message}");
            }
        }

        private async void GenerateBtn_Click(object sender, RoutedEventArgs e)
        {
            var prompt = PromptInput.Text;
            var apiKey = ApiKeyInput.Password;

            if (string.IsNullOrWhiteSpace(prompt))
            {
                MessageBox.Show("Please enter a goal.");
                return;
            }
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                MessageBox.Show("Please enter your OpenAI API Key.");
                return;
            }

            try
            {
                LoadingOverlay.Visibility = Visibility.Visible;
                GenerateBtn.IsEnabled = false;

                // Reload config to pick up any changes
                var builder = new ConfigurationBuilder()
                    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                var config = builder.Build();
                var model = config["OpenAI:Model"] ?? "gpt-4o";

                _currentGeneratedPlan = await _aiService.GeneratePlanFromPromptAsync(prompt, apiKey, model);

                var jsonOption = new JsonSerializerOptions { WriteIndented = true };
                PlanPreview.Text = JsonSerializer.Serialize(_currentGeneratedPlan, jsonOption);

                StartSessionBtn.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generating plan: {ex.Message}");
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
                GenerateBtn.IsEnabled = true;
            }
        }

        private OverlayWindow? _overlay;

        private void StartSessionBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentGeneratedPlan == null) return;

            // Transition to Dashboard
            SetupView.Visibility = Visibility.Collapsed;
            DashboardView.Visibility = Visibility.Visible;

            CurrentGoalText.Text = "Goal: " + (PromptInput.Text.Length > 30 ? PromptInput.Text.Substring(0, 30) + "..." : PromptInput.Text);

            _sessionStartTime = DateTime.Now;
            _sessionTimer.Start();

            // Create and Show Overlay
            _overlay = new OverlayWindow();
            _overlay.Show();
            _overlay.PauseRequested += (s, args) =>
            {
                // Toggle Pause
                if (_sessionManager.IsPaused)
                {
                    // RESUME
                    var pauseDuration = DateTime.Now - _pauseStartTime;
                    _sessionStartTime = _sessionStartTime.Add(pauseDuration);

                    _sessionManager.SetPaused(false);
                    _overlay.SetPauseState(false);
                    _sessionTimer.Start();
                }
                else
                {
                    // PAUSE
                    _pauseStartTime = DateTime.Now;

                    _sessionManager.SetPaused(true);
                    _overlay.SetPauseState(true);
                    _sessionTimer.Stop();
                }
            };
            _overlay.StopRequested += (s, args) => EndSessionBtn_Click(null, null);

            var sessionPlan = new SessionPlan
            {
                GoalText = PromptInput.Text,
                AllowedApps = _currentGeneratedPlan.AllowedApps,
                AllowedDomains = _currentGeneratedPlan.AllowedDomains,
                AllowedTitleKeywords = _currentGeneratedPlan.AllowedTitleKeywords ?? new List<string>(),
                SlackBudgetSeconds = _currentGeneratedPlan.SlackBudgetSeconds,
                SlackWindowSeconds = _currentGeneratedPlan.SlackWindowSeconds,
                StartTime = _sessionStartTime,
                EndTime = _sessionStartTime.AddMinutes(_currentGeneratedPlan.DurationMinutes),
            };

            _sessionManager.StateChanged += SessionManager_StateChanged;
            _sessionManager.StatusUpdated += SessionManager_StatusUpdated;
            _sessionManager.StartSession(sessionPlan);

            LogOutput.AppendText($"--- Session Started at {_sessionStartTime} ---\n");
            LogOutput.AppendText($"Plan: {_currentGeneratedPlan.Reasoning}\n");
        }

        private SessionStatusSnapshot? _lastStatusSnapshot;

        private DateTime? _distractionStartTime;

        private void SessionManager_StatusUpdated(object? sender, SessionStatusSnapshot e)
        {
            // Handle timer pausing logic on UI thread to avoid races with Tick
            Dispatcher.Invoke(() =>
            {
                bool wasDistracted = _lastStatusSnapshot?.IsDistracted ?? false;
                bool isDistracted = e.IsDistracted;

                if (!wasDistracted && isDistracted)
                {
                    // Distraction Started -> Freeze timer
                    _distractionStartTime = DateTime.Now;
                }
                else if (wasDistracted && !isDistracted)
                {
                    // Distraction Ended -> Refund time
                    if (_distractionStartTime.HasValue)
                    {
                        var duration = DateTime.Now - _distractionStartTime.Value;
                        _sessionStartTime = _sessionStartTime.Add(duration);
                        _distractionStartTime = null;

                        // Optional: Log the refund?
                        // LogOutput.AppendText($"[System] Distraction ended. Refunded {duration.TotalSeconds:F1}s to session timer.\n");
                    }
                }

                _lastStatusSnapshot = e;
            });
        }

        private void SessionManager_StateChanged(object? sender, string message)
        {
            Dispatcher.Invoke(() =>
            {
                LogOutput.AppendText($"{DateTime.Now:HH:mm:ss} {message}\n");
                LogOutput.ScrollToEnd();
            });
        }

        private void EndSessionBtn_Click(object? sender, RoutedEventArgs? e)
        {
            _sessionTimer.Stop();
            _sessionManager.StopSession();
            _sessionManager.StateChanged -= SessionManager_StateChanged;
            _sessionManager.StatusUpdated -= SessionManager_StatusUpdated;

            _overlay?.Close();
            _overlay = null;
            _lastStatusSnapshot = null;
            _distractionStartTime = null;

            SetupView.Visibility = Visibility.Visible;
            DashboardView.Visibility = Visibility.Collapsed;
            StartSessionBtn.IsEnabled = false;

            LogOutput.AppendText($"--- Session Ends ---\n");
        }

        private void SessionTimer_Tick(object? sender, EventArgs e)
        {
            if (_currentGeneratedPlan == null) return;

            var now = DateTime.Now;
            var sessionDuration = TimeSpan.FromMinutes(_currentGeneratedPlan.DurationMinutes);

            // Calculate elapsed time.
            // If we are currently distracted, we freeze it at the start of distraction.
            // If explicit pause is active, _sessionTimer is stopped, so this Tick doesn't run anyway (usually).
            // But if Overlay calls Pause, we stop this timer.

            TimeSpan elapsed;
            if (_distractionStartTime.HasValue)
            {
                elapsed = _distractionStartTime.Value - _sessionStartTime;
            }
            else
            {
                elapsed = now - _sessionStartTime;
            }

            var remaining = sessionDuration - elapsed;

            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

            var text = remaining.ToString(@"hh\:mm\:ss");
            TimerText.Text = text; // Update Main Window
            _overlay?.UpdateStatus(text, _lastStatusSnapshot); // Update Overlay
        }

        public void UpdateActivityLog(ActivitySnapshot activity)
        {
            // Only log if session is running (Dashboard visible)
            if (DashboardView.Visibility == Visibility.Visible)
            {
                var log = $"[{activity.Timestamp:HH:mm:ss}] {activity.ProcessName} - {activity.WindowTitle}";
                if (!string.IsNullOrEmpty(activity.Url))
                {
                    log += $" ({activity.Url})";
                }
                log += "\n";

                LogOutput.AppendText(log);
                LogOutput.ScrollToEnd();
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
        }

        private void PromptInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {

        }
    }
}