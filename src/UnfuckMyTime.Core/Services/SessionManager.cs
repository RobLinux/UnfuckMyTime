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

        private System.Threading.Timer? _checkTimer;
        private readonly object _lock = new object();
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
        }

        public void StopSession()
        {
            _currentPlan = null;
            _checkTimer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
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
            if (_currentPlan == null) return;

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
                if (_accumulatedDistractionDuration.TotalSeconds < _currentPlan.SlackBudgetSeconds)
                {
                    return; // Within budget
                }

                // 3. Check Notification Interval
                if (_lastNotificationTime.HasValue)
                {
                    var timeSinceLast = now - _lastNotificationTime.Value;
                    
                    // Determine which interval to use based on how many notifications we've ALREADY sent.
                    // If we have sent 'Threshold' notifications (e.g. 3), the NEXT one will be Phase 2.
                    // So we generally want to accelerate immediately.
                    var currentInterval = _notificationCount >= _currentPlan.Phase2Threshold 
                        ? _currentPlan.Phase2IntervalSeconds 
                        : _currentPlan.NotificationIntervalSeconds;

                    if (timeSinceLast.TotalSeconds < currentInterval)
                    {
                        return; // Rate limited
                    }
                }

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

                // Stop active monitoring timer since we are safe
                _checkTimer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            }
        }
    }
}
