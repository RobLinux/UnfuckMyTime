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
                IsDistracted = currentClassification == ActivityClassification.Distraction
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

        public SessionManager()
        {
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

            var classification = _rulesEngine.Evaluate(activity, _currentPlan, _exceptions);
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

                    // Clear the end time because we are now distracted again
                    _lastDistractionEndTime = null;
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

                        if (_notificationCount < _currentPlan.Phase2Threshold)
                        {
                            // Phase 1: Adaptive reduction
                            // Reduce by (Interval / Threshold) for each step
                            double baseInterval = _currentPlan.NotificationIntervalSeconds;
                            double reductionStep = baseInterval / (double)_currentPlan.Phase2Threshold;

                            // We use _notificationCount (which is 0-based for the delay check essentially,
                            // since we increment AFTER this check usually? 
                            // Wait, _notificationCount is incremented at line 153.
                            // So currently it represents "how many notifications HAVE been sent".
                            // So if we have sent 0, we wait full interval.
                            // If we have sent 1, we wait reduced interval.

                            currentInterval = baseInterval - (_notificationCount * reductionStep);
                        }
                        else
                        {
                            // Phase 2: Static fast interval
                            currentInterval = _currentPlan.Phase2IntervalSeconds;
                        }

                        // User requested minimum of 5s (was 10s, but "then 5, then every 5" implies 5s min)
                        if (currentInterval < 5) currentInterval = 5;

                        if (timeSinceLast.TotalSeconds < currentInterval)
                        {
                            shouldFire = false; // Rate limited
                        }
                    }

                    if (shouldFire)
                    {
                        // 4. Fire Intervention
                        _notificationCount++;
                        var level = _notificationCount > _currentPlan.Phase2Threshold
                            ? InterventionLevel.WindowWiggle
                            : InterventionLevel.Notification;

                        var msg = level == InterventionLevel.WindowWiggle
                            ? $"[Alert Phase 2] WIGGLE Triggered! (#{_notificationCount})"
                            : $"[Alert Phase 1] Notification Triggered! (#{_notificationCount})";

                        DistractionDetected?.Invoke(this, new DistractionEvent
                        {
                            Message = $"Distraction detected: {activity.ProcessName}",
                            Level = level
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
