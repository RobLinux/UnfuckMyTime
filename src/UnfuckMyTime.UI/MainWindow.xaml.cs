using System;
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

                _currentGeneratedPlan = await _aiService.GeneratePlanFromPromptAsync(prompt, apiKey);

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

        private void StartSessionBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentGeneratedPlan == null) return;

            // Transition to Dashboard
            SetupView.Visibility = Visibility.Collapsed;
            DashboardView.Visibility = Visibility.Visible;

            CurrentGoalText.Text = "Goal: " + (PromptInput.Text.Length > 30 ? PromptInput.Text.Substring(0, 30) + "..." : PromptInput.Text);

            _sessionStartTime = DateTime.Now;
            _sessionTimer.Start();

            var sessionPlan = new SessionPlan
            {
                GoalText = PromptInput.Text,
                AllowedApps = _currentGeneratedPlan.AllowedApps,
                AllowedDomains = _currentGeneratedPlan.AllowedDomains,
                StartTime = _sessionStartTime,
                EndTime = _sessionStartTime.AddMinutes(_currentGeneratedPlan.DurationMinutes),
            };

            _sessionManager.StateChanged += SessionManager_StateChanged;
            _sessionManager.StartSession(sessionPlan);

            LogOutput.AppendText($"--- Session Started at {_sessionStartTime} ---\n");
            LogOutput.AppendText($"Plan: {_currentGeneratedPlan.Reasoning}\n");
        }

        private void SessionManager_StateChanged(object? sender, string message)
        {
            Dispatcher.Invoke(() =>
            {
                LogOutput.AppendText($"{DateTime.Now:HH:mm:ss} {message}\n");
                LogOutput.ScrollToEnd();
            });
        }

        private void EndSessionBtn_Click(object sender, RoutedEventArgs e)
        {
            _sessionTimer.Stop();
            _sessionManager.StopSession();
            _sessionManager.StateChanged -= SessionManager_StateChanged;

            SetupView.Visibility = Visibility.Visible;
            DashboardView.Visibility = Visibility.Collapsed;
            StartSessionBtn.IsEnabled = false;

            LogOutput.AppendText($"--- Session Ends ---\n");
        }

        private void SessionTimer_Tick(object? sender, EventArgs e)
        {
            var elapsed = DateTime.Now - _sessionStartTime;
            TimerText.Text = elapsed.ToString(@"hh\:mm\:ss");
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