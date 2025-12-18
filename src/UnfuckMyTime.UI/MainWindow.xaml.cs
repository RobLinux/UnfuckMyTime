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

            AllowedAppsList.ItemsSource = _allowedApps;
            AllowedSitesList.ItemsSource = _allowedDomains;
            AllowedKeywordsList.ItemsSource = _allowedKeywords;
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

        private System.Collections.ObjectModel.ObservableCollection<string> _allowedApps = new();
        private System.Collections.ObjectModel.ObservableCollection<string> _allowedDomains = new();
        private System.Collections.ObjectModel.ObservableCollection<string> _allowedKeywords = new();

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

                ApplyPlanToUI(_currentGeneratedPlan);
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

        private void ApplyPlanToUI(GeneratedPlan plan)
        {
            if (plan == null) return;

            PlanReasoning.Text = plan.Reasoning;
            PlanDuration.Text = $"{plan.DurationMinutes}m";

            // 1. Duration
            _selectedDurationMinutes = plan.DurationMinutes;
            int closestDurationIndex = 0;
            int minDiff = int.MaxValue;
            for(int i=0; i<_durationOptions.Length; i++) {
                int diff = Math.Abs(_durationOptions[i] - _selectedDurationMinutes);
                if(diff < minDiff) { minDiff = diff; closestDurationIndex = i; }
            }
            DurationSlider.Value = closestDurationIndex;
            DurationSliderValue.Text = $"{_selectedDurationMinutes}m";
            PlanDuration.Text = $"{_selectedDurationMinutes}m";

            // 2. Slack (Use Seconds directly)
            _selectedSlackSeconds = plan.SlackBudgetSeconds;
            
            int closestSlackIndex = 0;
            minDiff = int.MaxValue;
            for(int i=0; i<_slackOptions.Length; i++) {
                int diff = Math.Abs(_slackOptions[i] - _selectedSlackSeconds);
                if(diff < minDiff) { minDiff = diff; closestSlackIndex = i; }
            }
            // Update selection to match the snapped slider option to stay consistent
            _selectedSlackSeconds = _slackOptions[closestSlackIndex];
            SlackSlider.Value = closestSlackIndex;
            
            string slackDisplay = FormatTime(_selectedSlackSeconds);
            SlackSliderValue.Text = slackDisplay;
            PlanSlack.Text = $"Slack: {slackDisplay}";

            // 3. Reset (Use Seconds directly)
            _selectedResetSeconds = plan.SlackWindowSeconds;
            if (_selectedResetSeconds == 0) _selectedResetSeconds = 60; // Default fallback if missing

            int closestResetIndex = 0;
            minDiff = int.MaxValue;
            for(int i=0; i<_resetOptions.Length; i++) {
                int diff = Math.Abs(_resetOptions[i] - _selectedResetSeconds);
                if(diff < minDiff) { minDiff = diff; closestResetIndex = i; }
            }
            // Update selection to match the snapped slider option to stay consistent
            _selectedResetSeconds = _resetOptions[closestResetIndex];
            ResetSlider.Value = closestResetIndex;

            string resetDisplay = FormatTime(_selectedResetSeconds);
            ResetSliderValue.Text = resetDisplay;
            PlanReset.Text = $"Reset: {resetDisplay}";


            _allowedApps.Clear();
            foreach (var app in plan.AllowedApps) _allowedApps.Add(app);

            _allowedDomains.Clear();
            foreach (var domain in plan.AllowedDomains) _allowedDomains.Add(domain);

            _allowedKeywords.Clear();
            foreach (var kw in plan.AllowedTitleKeywords ?? new List<string>()) _allowedKeywords.Add(kw);

            // 4. Advanced Settings
            if (plan.NotificationIntervalSeconds.HasValue) 
                NotificationIntervalInput.Text = plan.NotificationIntervalSeconds.Value.ToString();
            
            if (plan.Phase2Threshold.HasValue) 
                Phase2ThresholdInput.Text = plan.Phase2Threshold.Value.ToString();
            
            if (plan.Phase2IntervalSeconds.HasValue) 
                Phase2IntervalInput.Text = plan.Phase2IntervalSeconds.Value.ToString();

            // 5. Main App
            PlanMainAppInput.Text = plan.MainAppProcessName ?? "";

            StartSessionBtn.IsEnabled = true;
        }

        private void SettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            SettingsOverlay.Visibility = Visibility.Visible;
        }

        private void SettingsCloseBtn_Click(object sender, RoutedEventArgs e)
        {
            SettingsOverlay.Visibility = Visibility.Collapsed;
            SaveUserSettings();
        }

        private class UserSettings
        {
            public int NotificationInterval { get; set; } = 30;
            public int Phase2Threshold { get; set; } = 3;
            public int Phase2Interval { get; set; } = 10;
            public int Phase3Threshold { get; set; } = 5;
            public int Phase3Delay { get; set; } = 10;
        }

        private string GetSettingsPath()
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UnfuckMyTime");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            return Path.Combine(folder, "user_settings.json");
        }

        private void SaveUserSettings()
        {
            try
            {
                var settings = new UserSettings();
                if (int.TryParse(NotificationIntervalInput.Text, out int n)) settings.NotificationInterval = n;
                if (int.TryParse(Phase2ThresholdInput.Text, out int t)) settings.Phase2Threshold = t;
                if (int.TryParse(Phase2IntervalInput.Text, out int i)) settings.Phase2Interval = i;
                if (int.TryParse(Phase3ThresholdInput.Text, out int p3t)) settings.Phase3Threshold = p3t;
                if (int.TryParse(Phase3DelayInput.Text, out int p3d)) settings.Phase3Delay = p3d;

                File.WriteAllText(GetSettingsPath(), JsonSerializer.Serialize(settings));
            }
            catch { /* Ignore save errors */ }
        }

        private void LoadUserSettings()
        {
            try
            {
                var path = GetSettingsPath();
                if (File.Exists(path))
                {
                    var settings = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path));
                    if (settings != null)
                    {
                        NotificationIntervalInput.Text = settings.NotificationInterval.ToString();
                        Phase2ThresholdInput.Text = settings.Phase2Threshold.ToString();
                        Phase2IntervalInput.Text = settings.Phase2Interval.ToString();
                        Phase3ThresholdInput.Text = settings.Phase3Threshold.ToString();
                        Phase3DelayInput.Text = settings.Phase3Delay.ToString();
                    }
                }
            }
            catch { /* Ignore load errors */ }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadUserSettings();
        }

        private void SavePlan_Click(object sender, RoutedEventArgs e)
        {
             // Sync settings before save
             if (_currentGeneratedPlan != null)
             {
                 if (int.TryParse(NotificationIntervalInput.Text, out int notif)) _currentGeneratedPlan.NotificationIntervalSeconds = notif;
                 if (int.TryParse(Phase2ThresholdInput.Text, out int thresh)) _currentGeneratedPlan.Phase2Threshold = thresh;
                 if (int.TryParse(Phase2IntervalInput.Text, out int inter)) _currentGeneratedPlan.Phase2IntervalSeconds = inter;
                 
                 _currentGeneratedPlan.SlackBudgetSeconds = _selectedSlackSeconds;
                 _currentGeneratedPlan.SlackWindowSeconds = _selectedResetSeconds;
                 _currentGeneratedPlan.DurationMinutes = _selectedDurationMinutes;
                 _currentGeneratedPlan.MainAppProcessName = PlanMainAppInput.Text;
             }

            if (_currentGeneratedPlan == null)
            {
                MessageBox.Show("No plan generated to save.");
                return;
            }

            var dlg = new Microsoft.Win32.SaveFileDialog();
            dlg.FileName = "plan"; 
            dlg.DefaultExt = ".json"; 
            dlg.Filter = "JSON documents (.json)|*.json"; 

            if (dlg.ShowDialog() == true)
            {
                try 
                {
                    string json = JsonSerializer.Serialize(_currentGeneratedPlan, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(dlg.FileName, json);
                }
                catch(Exception ex)
                {
                    MessageBox.Show($"Error saving plan: {ex.Message}");
                }
            }
        }

        private void LoadPlan_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.DefaultExt = ".json"; 
            dlg.Filter = "JSON documents (.json)|*.json"; 

            if (dlg.ShowDialog() == true)
            {
                try 
                {
                    string json = File.ReadAllText(dlg.FileName);
                    var plan = JsonSerializer.Deserialize<GeneratedPlan>(json);
                    if (plan != null)
                    {
                        _currentGeneratedPlan = plan;
                        ApplyPlanToUI(_currentGeneratedPlan);
                    }
                }
                catch(Exception ex)
                {
                    MessageBox.Show($"Error loading plan: {ex.Message}");
                }
            }
        }

        private readonly int[] _durationOptions = { 15, 30, 45, 60, 120 };
        private readonly int[] _slackOptions = { 0, 5, 10, 30, 60, 180, 300, 900 }; // Seconds: 0s, 5s, 10s, 30s, 1m, 3m, 5m, 15m
        private readonly int[] _resetOptions = { 15, 30, 60, 180, 300, 600, 900, 1800 }; // Seconds: 15s, 30s, 1m, 3m, 5m, 10m, 15m, 30m

        private int _selectedDurationMinutes = 30;
        private int _selectedSlackSeconds = 60;
        private int _selectedResetSeconds = 300;

        private void DurationChip_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            DurationPopup.IsOpen = !DurationPopup.IsOpen;
        }

        private void DurationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (DurationSliderValue == null) return; // UI not ready
            int index = (int)Math.Round(e.NewValue);
            // Clamp
            if (index < 0) index = 0;
            if (index >= _durationOptions.Length) index = _durationOptions.Length - 1;

            _selectedDurationMinutes = _durationOptions[index];
            DurationSliderValue.Text = $"{_selectedDurationMinutes}m";
            PlanDuration.Text = $"{_selectedDurationMinutes}m";
        }

        private void SlackChip_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            SlackPopup.IsOpen = !SlackPopup.IsOpen;
        }

        private void SlackSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
             if (SlackSliderValue == null) return; // UI not ready
            int index = (int)Math.Round(e.NewValue);
            // Clamp
            if (index < 0) index = 0;
            if (index >= _slackOptions.Length) index = _slackOptions.Length - 1;

            _selectedSlackSeconds = _slackOptions[index];
            string display = FormatTime(_selectedSlackSeconds);
            
            SlackSliderValue.Text = display;
            PlanSlack.Text = $"Slack: {display}";
        }
        
        private void ResetChip_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ResetPopup.IsOpen = !ResetPopup.IsOpen;
        }

        private void ResetSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
             if (ResetSliderValue == null) return; // UI not ready
            int index = (int)Math.Round(e.NewValue);
            // Clamp
            if (index < 0) index = 0;
            if (index >= _resetOptions.Length) index = _resetOptions.Length - 1;

            _selectedResetSeconds = _resetOptions[index];
            string display = FormatTime(_selectedResetSeconds);

            ResetSliderValue.Text = display;
            PlanReset.Text = $"Reset: {display}";
        }

        private string FormatTime(int seconds)
        {
            if (seconds == 0) return "0s";
            if (seconds % 60 == 0) return $"{seconds / 60}m";
            return $"{seconds}s";
        }

        private void AddApp_Click(object sender, RoutedEventArgs e)
        {
            var text = NewAppInput.Text.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                _allowedApps.Add(text);
                NewAppInput.Clear();
            }
        }

        private void NewAppInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                 AddApp_Click(sender, new RoutedEventArgs());
            }
        }

        private void AddSite_Click(object sender, RoutedEventArgs e)
        {
             var text = NewSiteInput.Text.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                _allowedDomains.Add(text);
                NewSiteInput.Clear();
            }
        }

        private void NewSiteInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
             if (e.Key == System.Windows.Input.Key.Enter)
            {
                 AddSite_Click(sender, new RoutedEventArgs());
            }
        }

        private void AddKeyword_Click(object sender, RoutedEventArgs e)
        {
             var text = NewKeywordInput.Text.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                _allowedKeywords.Add(text);
                NewKeywordInput.Clear();
            }
        }

        private void RemoveApp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is string app)
            {
                _allowedApps.Remove(app);
            }
        }

        private void RemoveSite_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is string site)
            {
                _allowedDomains.Remove(site);
            }
        }

        private void RemoveKeyword_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is string keyword)
            {
                _allowedKeywords.Remove(keyword);
            }
        }

        private void NewKeywordInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
             if (e.Key == System.Windows.Input.Key.Enter)
            {
                 AddKeyword_Click(sender, new RoutedEventArgs());
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

            int.TryParse(NotificationIntervalInput.Text, out int notifInterval);
            int.TryParse(Phase2ThresholdInput.Text, out int phase2Thresh);
            int.TryParse(Phase2IntervalInput.Text, out int phase2Interval);
            int.TryParse(Phase3ThresholdInput.Text, out int phase3Thresh);
            int.TryParse(Phase3DelayInput.Text, out int phase3Delay);

            var sessionPlan = new SessionPlan
            {
                GoalText = PromptInput.Text,
                AllowedApps = new List<string>(_allowedApps),
                AllowedDomains = new List<string>(_allowedDomains),
                AllowedTitleKeywords = new List<string>(_allowedKeywords),
                SlackBudgetSeconds = _selectedSlackSeconds,
                SlackWindowSeconds = _selectedResetSeconds,
                StartTime = _sessionStartTime,
                EndTime = _sessionStartTime.AddMinutes(_selectedDurationMinutes),
                NotificationIntervalSeconds = notifInterval > 0 ? notifInterval : 30,
                Phase2Threshold = phase2Thresh > 0 ? phase2Thresh : 3,
                Phase2IntervalSeconds = phase2Interval > 0 ? phase2Interval : 10,
                Phase3Threshold = phase3Thresh > 0 ? phase3Thresh : 5,
                Phase3DelaySeconds = phase3Delay > 0 ? phase3Delay : 10,
                MainAppProcessName = PlanMainAppInput.Text
            };

            _overlay.MainAppProcessName = PlanMainAppInput.Text;

            _sessionManager.StateChanged += SessionManager_StateChanged;
            _sessionManager.StatusUpdated += SessionManager_StatusUpdated;
            _sessionManager.DistractionDetected += SessionManager_DistractionDetected;
            _sessionManager.StartSession(sessionPlan);

            LogOutput.AppendText($"--- Session Started at {_sessionStartTime} ---\n");
            LogOutput.AppendText($"Plan: {_currentGeneratedPlan.Reasoning}\n");
        }

        private void SessionManager_DistractionDetected(object? sender, DistractionEvent e)
        {
             if (e.Level == InterventionLevel.FullBlock)
             {
                 Dispatcher.Invoke(() => _overlay?.SetFullBlock(true));
             }
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
                
                // Overlay Full Block Reset Logic
                // If not distracted (meaning Exempt or OnTrack), check if we should release the block.
                // We do NOT release the block if the current activity IS the overlay itself (UnfuckMyTime.UI),
                // because that means the block is successfully holding the user's focus.
                if (!isDistracted && _lastActivity?.ProcessName != "UnfuckMyTime.UI")
                {
                    _overlay?.SetFullBlock(false);
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
            _sessionManager.DistractionDetected -= SessionManager_DistractionDetected;

            _overlay?.Close();
            _overlay = null;
            _lastStatusSnapshot = null;
            _distractionStartTime = null;

            SetupView.Visibility = Visibility.Visible;
            DashboardView.Visibility = Visibility.Collapsed;
            StartSessionBtn.IsEnabled = _currentGeneratedPlan != null;

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

        private ActivitySnapshot? _lastActivity; // Deduplication

        public void UpdateActivityLog(ActivitySnapshot activity)
        {
            // Only log if session is running (Dashboard visible)
            if (DashboardView.Visibility == Visibility.Visible)
            {
                // Deduplicate: same process, title, and URL?
                if (_lastActivity != null &&
                    _lastActivity.ProcessName == activity.ProcessName &&
                    _lastActivity.WindowTitle == activity.WindowTitle &&
                    _lastActivity.Url == activity.Url)
                {
                    return;
                }

                _lastActivity = activity;

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

        private void Window_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                this.DragMove();
        }

        private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void APIKeyHelp_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://platform.openai.com/account/api-keys",
                UseShellExecute = true
            });
        }

        private void Window_Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void PromptInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {

        }
    }
}