using System;
using System.Collections.Generic;
using UnfuckMyTime.Core.Models;

namespace UnfuckMyTime.Core.Services
{
    public class SessionManager
    {
        private readonly RulesEngine _rulesEngine;
        private SessionPlan? _currentPlan;
        private readonly List<ExceptionRule> _exceptions = new();

        public event EventHandler<DistractionEvent>? DistractionDetected;
        public event EventHandler<string>? StateChanged;
        public event EventHandler<SessionStatusSnapshot>? StatusUpdated;

        private void CalculateAndFireStatus(ActivityClassification? currentClassification = null)
        {
            if (_currentPlan == null) return;

            var snapshot = new SessionStatusSnapshot
            {
                IsPaused = IsPaused,
                IsDistracted = currentClassification == ActivityClassification.Distraction,
                IsCurrentAppAllowed = currentClassification != ActivityClassification.Distraction
            };

            // Slack Remaining
            snapshot.SlackRemaining = TimeSpan.FromSeconds(Math.Max(0, _currentPlan.SlackBudgetSeconds - _accumulatedDistractionDuration.TotalSeconds));
            snapshot.TotalSlackBudget = TimeSpan.FromSeconds(_currentPlan.SlackBudgetSeconds);

            // Time Until Reset
            // Logic: If we have debt (>0) AND we are NOT currently distracted (Clean), 
            // then we are working towards a reset.
            // If we are clean, TimeUntilReset = Window - (Now - LastEnds).

            // Note: If we are paused, time effectively stops, but IsPaused handles display.

            if (_accumulatedDistractionDuration > TimeSpan.Zero
                && snapshot.SlackRemaining.TotalSeconds > 0 // If budget blown, can we reset? Yes usually.
                && !snapshot.IsDistracted
                && _lastDistractionEndTime.HasValue)
            {
                var timeSinceLast = TimeProvider() - _lastDistractionEndTime.Value;
                var remainingForReset = TimeSpan.FromSeconds(_currentPlan.SlackWindowSeconds) - timeSinceLast;
                if (remainingForReset > TimeSpan.Zero)
                {
                    snapshot.TimeUntilReset = remainingForReset;
                }
            }

            StatusUpdated?.Invoke(this, snapshot);
        }

        private readonly object _lock = new object();
        private System.Threading.Timer? _checkTimer;
        private ActivitySnapshot? _lastActivity; // To support re-evaluation

        // For testing purposes
        public Func<DateTime> TimeProvider { get; set; } = () => DateTime.Now;

        private readonly IMediaController? _mediaController;

        public SessionManager(IMediaController? mediaController = null)
        {
            _mediaController = mediaController;
            _rulesEngine = new RulesEngine();
            // Initialize timer but don't start it yet
            _checkTimer = new System.Threading.Timer(OnTimerTick, null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        }

        public void StartSession(SessionPlan plan)
        {
            _currentPlan = plan;
            _notificationCount = 0; // Reset for new session
            _accumulatedDistractionDuration = TimeSpan.Zero;
            _lastDistractionEvalTime = null;
            CalculateAndFireStatus(); // Ensure initial state is published
        }

        public void StopSession()
        {
            _currentPlan = null;
            IsPaused = false;
            _checkTimer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        }

        public bool IsPaused { get; private set; }
        private DateTime? _pauseStartTime;

        public void SetPaused(bool paused)
        {
            if (IsPaused == paused) return;
            IsPaused = paused;

            if (paused)
            {
                // Starting Pause
                _pauseStartTime = DateTime.Now;
                // Stop the internal check timer to save resources/avoid ticks
                _checkTimer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
                StateChanged?.Invoke(this, "[System] Session Paused.");
            }
            else
            {
                // Resuming
                if (_pauseStartTime.HasValue)
                {
                    var pauseDuration = DateTime.Now - _pauseStartTime.Value;

                    // Shift all reference times forward
                    if (_lastDistractionEvalTime.HasValue) _lastDistractionEvalTime = _lastDistractionEvalTime.Value.Add(pauseDuration);
                    if (_lastDistractionEndTime.HasValue) _lastDistractionEndTime = _lastDistractionEndTime.Value.Add(pauseDuration);
                    if (_lastNotificationTime.HasValue) _lastNotificationTime = _lastNotificationTime.Value.Add(pauseDuration);

                    _pauseStartTime = null;
                }

                // Restart timer logic (if active)
                if (_lastDistractionEvalTime.HasValue)
                {
                    // If we were tracking, resume immediately
                    _checkTimer?.Change(1000, 1000);
                }
                else
                {
                    // Otherwise just let the next activity trigger things or set low poll
                    _checkTimer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
                }
                StateChanged?.Invoke(this, "[System] Session Resumed.");
            }
            CalculateAndFireStatus();
        }


        public void EvaluateActivity(ActivitySnapshot activity)
        {
            if (_currentPlan == null) return;

            lock (_lock)
            {
                _lastActivity = activity;
                EvaluateInternal(activity);
            }
        }

        private void OnTimerTick(object? state)
        {
            if (_currentPlan == null) return;

            lock (_lock)
            {
                if (_lastActivity != null)
                {
                    EvaluateInternal(_lastActivity);
                }
            }
        }

        private TimeSpan _accumulatedDistractionDuration;
        private int _notificationCount;
        private DateTime? _lastDistractionEvalTime;
        private DateTime? _lastNotificationTime;

        private DateTime? _lastDistractionEndTime;

        // No longer tracking specific process for reset, as we treat all distractions as one block.

        private void EvaluateInternal(ActivitySnapshot activity)
        {
            if (_currentPlan == null || IsPaused) return;

            var classification = _rulesEngine.Evaluate(activity, _currentPlan, _exceptions, _notificationCount);
            var now = TimeProvider();

            if (classification == ActivityClassification.Distraction)
            {
                // Rising Edge: We were not tracking, now we are.
                if (_lastDistractionEvalTime == null)
                {
                    // CHECK FOR REDEMPTION/RESET
                    if (_lastDistractionEndTime.HasValue)
                    {
                        var timeSinceLastDistraction = now - _lastDistractionEndTime.Value;
                        if (timeSinceLastDistraction.TotalSeconds >= _currentPlan.SlackWindowSeconds)
                        {
                            _accumulatedDistractionDuration = TimeSpan.Zero;
                            _notificationCount = 0; // Reset escalation level
                            StateChanged?.Invoke(this, $"[Focus Bonus] You stayed focused for {_currentPlan.SlackWindowSeconds}s. Distraction budget RESET.");
                        }
                    }

                    double remaining = Math.Max(0, _currentPlan.SlackBudgetSeconds - _accumulatedDistractionDuration.TotalSeconds);
                    if (_accumulatedDistractionDuration == TimeSpan.Zero)
                    {
                        StateChanged?.Invoke(this, $"[Distraction Start] Slack timer STARTED. Budget: {_currentPlan.SlackBudgetSeconds}s");
                    }
                    else
                    {
                        StateChanged?.Invoke(this, $"[Distraction Resume] Slack timer RESUMED. Left: {remaining:F1}s");
                    }

                    // Enforce Phase 3 Delay on Re-entry
                    if (_currentPlan != null && _notificationCount > (_currentPlan.Phase2Threshold + _currentPlan.Phase3Threshold))
                    {
                        // Reset the timer so they get the full 'Phase3DelaySeconds' grace/warning period
                        // before the Full Block slams down again.
                        _lastNotificationTime = now;
                    }

                    // Clear the end time because we are now distracted again
                    _lastDistractionEndTime = null;
                }

                // Attempt to pause media if we are distracted
                if (_mediaController != null)
                {
                    _ = _mediaController.TryPausePlaybackAsync();
                }

                // 1. Accumulate Distraction Time
                if (_lastDistractionEvalTime.HasValue)
                {
                    _accumulatedDistractionDuration += (now - _lastDistractionEvalTime.Value);
                }

                // Track that we are currently evaluating a distraction
                _lastDistractionEvalTime = now;

                // Start/Ensure Timer
                _checkTimer?.Change(1000, 1000);

                // 2. Check Slack Time
                if (_accumulatedDistractionDuration.TotalSeconds >= _currentPlan.SlackBudgetSeconds)
                {
                    bool shouldFire = true;

                    // 3. Check Notification Interval
                    if (_lastNotificationTime.HasValue)
                    {
                        var timeSinceLast = now - _lastNotificationTime.Value;

                        // Determine which interval to use based on how many notifications we've ALREADY sent.
                        // If we have sent 'Threshold' notifications (e.g. 3), the NEXT one will be Phase 2.
                        // So we generally want to accelerate immediately.
                        double currentInterval;

                        // Determine level logic
                        int phase2Start = _currentPlan.Phase2Threshold; // e.g., 3
                        int phase3Start = phase2Start + _currentPlan.Phase3Threshold; // e.g., 3 + 5 = 8

                        if (_notificationCount < phase2Start)
                        {
                            // Phase 1: Adaptive reduction
                            double baseInterval = _currentPlan.NotificationIntervalSeconds;
                            double reductionStep = baseInterval / (double)Math.Max(1, phase2Start);
                            currentInterval = baseInterval - (_notificationCount * reductionStep);
                        }
                        else if (_notificationCount < phase3Start)
                        {
                            // Phase 2: Static fast interval (Wiggle Phase)
                            currentInterval = _currentPlan.Phase2IntervalSeconds;
                        }
                        else
                        {
                            // Phase 3: Penalty Box (Full Block)
                            // "If we switch back to another unauthorized app ... after X seconds ... switch back"
                            currentInterval = _currentPlan.Phase3DelaySeconds;
                        }

                        // User requested minimum of 5s (was 10s, but "then 5, then every 5" implies 5s min)
                        // However, Phase 3 delay might be specific (e.g. 10s), so we only clamp if it's NOT Phase 3 or we want a global min.
                        // User default for Phase 3 is 10s. If they set it to 2s, we should probably honor it or clamp to reasonable min.
                        if (currentInterval < 2) currentInterval = 2; // Lower clamp to 2s for extreme cases

                        if (timeSinceLast.TotalSeconds < currentInterval)
                        {
                            shouldFire = false; // Rate limited
                        }
                    }

                    if (shouldFire)
                    {
                        // 4. Fire Intervention
                        _notificationCount++;

                        InterventionLevel level;
                        int phase2Start = _currentPlan.Phase2Threshold;
                        int phase3Start = phase2Start + _currentPlan.Phase3Threshold;

                        if (_notificationCount > phase3Start)
                        {
                            level = InterventionLevel.FullBlock;
                        }
                        else if (_notificationCount > phase2Start)
                        {
                            level = InterventionLevel.WindowWiggle;
                        }
                        else
                        {
                            level = InterventionLevel.Notification;
                        }

                        double intensity = 1.0;
                        if (level == InterventionLevel.WindowWiggle)
                        {
                            int wiggleIndex = _notificationCount - phase2Start;
                            // Augment 30% each time, max 3 augmentations (so 4th wiggle is max strength)
                            int augmentations = Math.Clamp(wiggleIndex - 1, 0, 3);
                            intensity = Math.Pow(1.3, augmentations);
                        }

                        string msg;
                        switch (level)
                        {
                            case InterventionLevel.FullBlock:
                                msg = $"[Alert Phase 3] FULL BLOCK Triggered! (#{_notificationCount})";
                                break;
                            case InterventionLevel.WindowWiggle:
                                msg = $"[Alert Phase 2] WIGGLE Triggered! (#{_notificationCount}, x{intensity:F1})";
                                break;
                            default:
                                msg = $"[Alert Phase 1] Notification Triggered! (#{_notificationCount})";
                                break;
                        }

                        DistractionDetected?.Invoke(this, new DistractionEvent
                        {
                            Message = $"Distraction detected: {activity.ProcessName}",
                            Level = level,
                            Intensity = intensity
                        });

                        StateChanged?.Invoke(this, msg);

                        _lastNotificationTime = now;
                    }
                }
            }
            else
            {
                // Falling Edge: We were tracking, now we stop.
                if (_lastDistractionEvalTime != null)
                {
                    // Calculate remaining before pausing
                    double remaining = Math.Max(0, _currentPlan.SlackBudgetSeconds - _accumulatedDistractionDuration.TotalSeconds);
                    StateChanged?.Invoke(this, $"[Distraction Pause] Slack timer PAUSED. Left: {remaining:F1}s");

                    // Mark the end of this distraction block
                    _lastDistractionEndTime = now;
                }

                // Not distracted (OnTrack/Exempt) -> Pause
                // We stop accumulating. We set _lastDistractionEvalTime to null so that 
                // when we resume, we don't count the gap as distraction.
                _lastDistractionEvalTime = null;

                // We DO NOT reset _accumulatedDistractionDuration (User requested Pause).
                // We DO NOT reset _lastNotificationTime (Rate limit persists).

                // We DO NOT reset _accumulatedDistractionDuration (User requested Pause).
                // We DO NOT reset _lastNotificationTime (Rate limit persists).

                // Check if we can reset the debt actively while sitting in a Clean state
                if (_accumulatedDistractionDuration > TimeSpan.Zero)
                {
                    bool reset = false;
                    if (_lastDistractionEndTime.HasValue)
                    {
                        var timeSinceLast = now - _lastDistractionEndTime.Value;
                        if (timeSinceLast.TotalSeconds >= _currentPlan.SlackWindowSeconds)
                        {
                            _accumulatedDistractionDuration = TimeSpan.Zero;
                            _notificationCount = 0;
                            // Do not clear _lastDistractionEndTime yet if we want to track "last distraction" for other reasons,
                            // but clearing it prevents double-reset logic in Rising Edge. 
                            // Actually, clearing it is safer to state "we have fully recovered".
                            _lastDistractionEndTime = null;

                            StateChanged?.Invoke(this, $"[Focus Bonus] You stayed focused for {_currentPlan.SlackWindowSeconds}s. Distraction budget RESET.");
                            reset = true;
                        }
                    }

                    if (reset)
                    {
                        _checkTimer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
                    }
                    else
                    {
                        // Keep timer running to update "Reset In" counter vs UI
                        _checkTimer?.Change(1000, 1000);
                    }
                }
                else
                {
                    // Stop active monitoring timer since we are safe and clean
                    _checkTimer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
                }
            }

            CalculateAndFireStatus(classification);
        }
    }
}
