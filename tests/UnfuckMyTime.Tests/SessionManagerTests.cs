using System;
using System.Collections.Generic;
using UnfuckMyTime.Core.Models;
using UnfuckMyTime.Core.Services;
using Xunit;

namespace UnfuckMyTime.Tests
{
    public class SessionManagerTests
    {
        private readonly SessionManager _sessionManager;
        private readonly SessionPlan _plan;
        private DateTime _currentTime;

        public SessionManagerTests()
        {
            _sessionManager = new SessionManager();
            _currentTime = new DateTime(2025, 1, 1, 12, 0, 0);
            _sessionManager.TimeProvider = () => _currentTime;

            _plan = new SessionPlan
            {
                AllowedApps = new List<string> { "WorkApp" },
                SlackBudgetSeconds = 15,
                SlackWindowSeconds = 60000,
                NotificationIntervalSeconds = 30
            };
            _sessionManager.StartSession(_plan);
        }

        private ActivitySnapshot CreateSnapshot(string processName)
        {
            return new ActivitySnapshot
            {
                ProcessName = processName,
                Timestamp = _currentTime,
                WindowTitle = "Test Window",
                IsIdle = false
            };
        }

        [Fact]
        public void Distraction_ShouldRespectSlackTime()
        {
            bool notificationReceived = false;
            _sessionManager.DistractionDetected += (_, _) => notificationReceived = true;

            // 1. Start Distraction
            _sessionManager.EvaluateActivity(CreateSnapshot("Game"));
            Assert.False(notificationReceived, "Should not notify immediately (Slack Time)");

            // 2. Advance time within slack - Simulate Timer Ticks or Updates
            _currentTime = _currentTime.AddSeconds(5);
            _sessionManager.EvaluateActivity(CreateSnapshot("Game"));
            Assert.False(notificationReceived, "Should not notify within slack time");

            // 3. Advance time past slack
            _currentTime = _currentTime.AddSeconds(11); // Total 5+11=16s > 15s
            _sessionManager.EvaluateActivity(CreateSnapshot("Game"));
            Assert.True(notificationReceived, "Should notify after slack time");
        }

        [Fact]
        public void Distraction_ShouldRespectNotificationInterval()
        {
            int notificationCount = 0;
            _sessionManager.DistractionDetected += (_, _) => notificationCount++;

            // 0. Start Distraction
            _sessionManager.EvaluateActivity(CreateSnapshot("Game"));

            // 1. Trigger first notification
            _currentTime = _currentTime.AddSeconds(15); // Past slack (10s)
            _sessionManager.EvaluateActivity(CreateSnapshot("Game"));
            Assert.Equal(1, notificationCount);

            // 2. Check too soon
            _currentTime = _currentTime.AddSeconds(10); // +10s (Total since notify: 10s < 30s)
            _sessionManager.EvaluateActivity(CreateSnapshot("Game"));
            Assert.Equal(1, notificationCount); // No new notification

            // 3. Check after interval
            _currentTime = _currentTime.AddSeconds(21); // +21s (Total since notify: 31s > 30s)
            _sessionManager.EvaluateActivity(CreateSnapshot("Game"));
            Assert.Equal(2, notificationCount);
        }

        [Fact]
        public void SwitchingDistraction_ShouldNOTResetSlackTime()
        {
            int notificationCount = 0;
            _sessionManager.DistractionDetected += (_, _) => notificationCount++;

            // 1. Distraction A for 5 seconds
            _sessionManager.EvaluateActivity(CreateSnapshot("GameA"));
            _currentTime = _currentTime.AddSeconds(5);
            _sessionManager.EvaluateActivity(CreateSnapshot("GameA"));
            Assert.Equal(0, notificationCount); // 5s < 15s

            // 2. Switch to Distraction B immediately
            // Should CONTINUE accumulating
            _sessionManager.EvaluateActivity(CreateSnapshot("GameB"));

            // 3. Advance another 11 seconds (Total 5 + 11 = 16s > 15s)
            _currentTime = _currentTime.AddSeconds(11);
            _sessionManager.EvaluateActivity(CreateSnapshot("GameB"));

            Assert.Equal(1, notificationCount); // Should fire because it SUMMED the times
        }

        [Fact]
        public void Allowed_ShouldPauseAccumulation()
        {
            int notificationCount = 0;
            _sessionManager.DistractionDetected += (_, _) => notificationCount++;

            // 1. Distraction 10s
            _sessionManager.EvaluateActivity(CreateSnapshot("Game"));
            _currentTime = _currentTime.AddSeconds(10);
            _sessionManager.EvaluateActivity(CreateSnapshot("Game"));
            Assert.Equal(0, notificationCount); // 10s < 15s

            // 2. Work for 1 hour (Allowed)
            _sessionManager.EvaluateActivity(CreateSnapshot("WorkApp"));
            _currentTime = _currentTime.AddHours(1);
            _sessionManager.EvaluateActivity(CreateSnapshot("WorkApp"));

            // 3. Back to Distraction
            // Should resume at 10s.
            _sessionManager.EvaluateActivity(CreateSnapshot("Game"));

            // 4. Advance 6s (Total 10 + 6 = 16s > 15s)
            _currentTime = _currentTime.AddSeconds(6);
            _sessionManager.EvaluateActivity(CreateSnapshot("Game"));
            Assert.Equal(1, notificationCount);
        }
        [Fact]
        public void SystemExemptions_ShouldBeIgnored()
        {
            int notificationCount = 0;
            _sessionManager.DistractionDetected += (_, _) => notificationCount++;

            // 1. Explorer (System Exempt)
            _sessionManager.EvaluateActivity(CreateSnapshot("explorer"));
            // 2. UnfuckMyTime.UI (System Exempt)
            _sessionManager.EvaluateActivity(CreateSnapshot("UnfuckMyTime.UI"));
            // 3. Random App (Distraction)
            _sessionManager.EvaluateActivity(CreateSnapshot("RandomApp"));

            // Advance time past slack
            _currentTime = _currentTime.AddSeconds(20);
            _sessionManager.EvaluateActivity(CreateSnapshot("RandomApp"));

            // Only RandomApp should trigger (1 notification)
            Assert.Equal(1, notificationCount);
        }
        [Fact]
        public void StateChanged_ShouldFireEvents()
        {
            var messages = new List<string>();
            _sessionManager.StateChanged += (_, msg) => messages.Add(msg);

            // 1. Start Distraction
            _sessionManager.EvaluateActivity(CreateSnapshot("Game"));
            Assert.Contains(messages, m => m.Contains("STARTED"));

            // 1b. Accumulate some time
            _currentTime = _currentTime.AddSeconds(5);
            _sessionManager.EvaluateActivity(CreateSnapshot("Game"));

            // 2. Pause (Switch to Work)
            _sessionManager.EvaluateActivity(CreateSnapshot("WorkApp"));
            Assert.Contains(messages, m => m.Contains("PAUSED"));

            // 3. Resume
            _sessionManager.EvaluateActivity(CreateSnapshot("Game"));
            Assert.Contains(messages, m => m.Contains("RESUMED"));
        }

        [Fact]
        public void Phase2_ShouldTriggerWiggle()
        {
            var events = new List<DistractionEvent>();
            _sessionManager.DistractionDetected += (_, e) => events.Add(e);

            // Configure Phase 2 Threshold = 2
            _plan.Phase2Threshold = 2;
            _sessionManager.StartSession(_plan);

            // 1. Start Distraction & Pass Slack
            _sessionManager.EvaluateActivity(CreateSnapshot("Game"));
            _currentTime = _currentTime.AddSeconds(_plan.SlackBudgetSeconds + 1);
            _sessionManager.EvaluateActivity(CreateSnapshot("Game"));
            
            // #1 Notification (Phase 1)
            Assert.Single(events);
            Assert.Equal(InterventionLevel.Notification, events[0].Level);

            // 2. Wait Interval
            _currentTime = _currentTime.AddSeconds(_plan.NotificationIntervalSeconds + 1);
            _sessionManager.EvaluateActivity(CreateSnapshot("Game"));

            // #2 Notification (Phase 1) - Threshold is 2, so count=2 is still Phase 1?
            // "If _notificationCount > Phase2Threshold". 2 > 2 is False. So still notification.
            Assert.Equal(2, events.Count);
            Assert.Equal(InterventionLevel.Notification, events[1].Level);

            // 3. Wait Interval
            _currentTime = _currentTime.AddSeconds(_plan.NotificationIntervalSeconds + 1);
            _sessionManager.EvaluateActivity(CreateSnapshot("Game"));

            // #3 Notification (Phase 2) - Count=3 > 2. So Wiggle.
            Assert.Equal(3, events.Count);
            Assert.Equal(InterventionLevel.WindowWiggle, events[2].Level);
        }
        [Fact]
        public void Phase2_ShouldUseFasterInterval()
        {
            var events = new List<DistractionEvent>();
            _sessionManager.DistractionDetected += (_, e) => events.Add(e);

            // Configure Phase 2 Threshold = 1 for quick test
            // Phase 1 Interval = 30s
            // Phase 2 Interval = 10s
            _plan.Phase2Threshold = 1; 
            _plan.Phase2IntervalSeconds = 10;
            _sessionManager.StartSession(_plan);

            // 1. Start Distraction -> Fire #1 (Phase 1)
            _sessionManager.EvaluateActivity(CreateSnapshot("Game"));
            _currentTime = _currentTime.AddSeconds(_plan.SlackBudgetSeconds + 1);
            _sessionManager.EvaluateActivity(CreateSnapshot("Game"));
            Assert.Single(events);

            // 2. Wait 15s (Less than Phase 1 (30s), but more than Phase 2 (10s))
            // Since count=1 and Threshold=1, we should be using Phase 2 interval.
            // So 15s should be enough to trigger #2 (Wiggle).
            _currentTime = _currentTime.AddSeconds(15);
            _sessionManager.EvaluateActivity(CreateSnapshot("Game"));

            Assert.Equal(2, events.Count);
            Assert.Equal(InterventionLevel.WindowWiggle, events[1].Level);
        }
    }
}
