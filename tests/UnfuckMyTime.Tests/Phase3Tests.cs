using System;
using System.Collections.Generic;
using UnfuckMyTime.Core.Models;
using UnfuckMyTime.Core.Services;
using Xunit;

namespace UnfuckMyTime.Tests
{
    public class Phase3Tests
    {
        private readonly SessionManager _sessionManager;
        private readonly SessionPlan _plan;
        private DateTime _currentTime;

        public Phase3Tests()
        {
            _sessionManager = new SessionManager();
            _currentTime = new DateTime(2025, 1, 1, 12, 0, 0);
            _sessionManager.TimeProvider = () => _currentTime;

            _plan = new SessionPlan
            {
                AllowedApps = new List<string> { "WorkApp" },
                SlackBudgetSeconds = 15, // 15s slack
                NotificationIntervalSeconds = 10, // Phase 1
                Phase2Threshold = 2, // After 2 notifications, start Phase 2
                Phase3Threshold = 2, // After 2 more (Total 4), start Phase 3
                Phase2IntervalSeconds = 5, // Phase 2 speed
                Phase3DelaySeconds = 10 // Phase 3 penalty delay
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
        public void Phase3_ShouldTriggerFullBlock_AfterThreshold()
        {
            var events = new List<DistractionEvent>();
            _sessionManager.DistractionDetected += (_, e) => events.Add(e);

            // Burn Slack (15s)
            _sessionManager.EvaluateActivity(CreateSnapshot("Game"));
            _currentTime = _currentTime.AddSeconds(_plan.SlackBudgetSeconds + 1);
            _sessionManager.EvaluateActivity(CreateSnapshot("Game"));

            // #1 Notify (Phase 1)
            Assert.Single(events);
            Assert.Equal(InterventionLevel.Notification, events[0].Level);

            // Wait Phase 1 Interval (10s) -> #2 Notify (Phase 1)
            // (Count=1, Threshold=2. 1 < 2 => Phase 1)
            _currentTime = _currentTime.AddSeconds(_plan.NotificationIntervalSeconds + 1);
            _sessionManager.EvaluateActivity(CreateSnapshot("Game"));
            Assert.Equal(2, events.Count);
            Assert.Equal(InterventionLevel.Notification, events[1].Level);

            // Wait Phase 1 Interval (10s) again?
            // Count=2, Threshold=2. 2 is NOT < 2.
            // Check Phase 2: Count=2 < Phase3Start (2+2=4). Yes.
            // So Phase 2 Interval (5s).
            _currentTime = _currentTime.AddSeconds(_plan.Phase2IntervalSeconds + 1);
            _sessionManager.EvaluateActivity(CreateSnapshot("Game"));

            // #3 Wiggle (Phase 2)
            Assert.Equal(3, events.Count);
            Assert.Equal(InterventionLevel.WindowWiggle, events[2].Level);

            // Wait Phase 2 Interval (5s)
            _currentTime = _currentTime.AddSeconds(_plan.Phase2IntervalSeconds + 1);
            _sessionManager.EvaluateActivity(CreateSnapshot("Game"));

            // #4 Wiggle (Phase 2)
            Assert.Equal(4, events.Count);
            Assert.Equal(InterventionLevel.WindowWiggle, events[3].Level);

            // Wait Phase 2 Interval (5s)
            // Count=4. Phase3Start=4. 4 < 4 is False.
            // So Phase 3 Logic. Interval = Phase3DelaySeconds (10s).
            _currentTime = _currentTime.AddSeconds(_plan.Phase3DelaySeconds + 1);
            _sessionManager.EvaluateActivity(CreateSnapshot("Game"));

            // #5 Full Block (Phase 3)
            Assert.Equal(5, events.Count);
            Assert.Equal(InterventionLevel.FullBlock, events[4].Level);
        }

        [Fact]
        public void Phase3_ShouldWaitPenaltyDelay_BetweenBlocks()
        {
            var events = new List<DistractionEvent>();
            _sessionManager.DistractionDetected += (_, e) => events.Add(e);

            // Fast forward to Phase 3
            // Trigger 4 events first
            _plan.Phase2Threshold = 0; // Immediate Phase 2
            _plan.Phase3Threshold = 0; // Immediate Phase 3
                                       // Correct logic: if Threshold is 0, Start is 0. If Count >= 0?
                                       // Let's stick to manual loop to be safe.
            _plan.Phase2Threshold = 1;
            _plan.Phase3Threshold = 1;
            // Phase 2 starts after 1. Phase 3 starts after 1+1=2.

            // 1. Burn Slack
            _sessionManager.EvaluateActivity(CreateSnapshot("Game"));
            _currentTime = _currentTime.AddSeconds(20);
            _sessionManager.EvaluateActivity(CreateSnapshot("Game"));
            // #1 Notify
            Assert.Equal(InterventionLevel.Notification, events[0].Level);

            // 2. Wait Phase 1 Interval (default 10s set in constructor)
            // Count=1. P2Thresh=1. 1 < 1 False.
            // P3Start=2. 1 < 2 True. -> Phase 2.
            _currentTime = _currentTime.AddSeconds(11);
            _sessionManager.EvaluateActivity(CreateSnapshot("Game"));
            // #2 Wiggle
            Assert.Equal(InterventionLevel.WindowWiggle, events[1].Level);

            // 3. Wait Phase 3 Delay (10s)
            // Count=2. P3Start=2. 2 < 2 False. -> Phase 3 Logic applies for interval.
            // So we must wait 10s (Phase3DelaySeconds)
            _currentTime = _currentTime.AddSeconds(11);
            _sessionManager.EvaluateActivity(CreateSnapshot("Game"));
            // #3 Full Block
            Assert.Equal(InterventionLevel.FullBlock, events[2].Level);

            // 4. Verify Interval is now Phase3Delay (10s)

            // Try 5s (Phase 2 interval) -> Should NOT fire
            _currentTime = _currentTime.AddSeconds(5);
            _sessionManager.EvaluateActivity(CreateSnapshot("Game"));
            Assert.Equal(3, events.Count); // Still 3

            // Try another 6s (Total 11s) -> Should fire
            _currentTime = _currentTime.AddSeconds(6);
            _sessionManager.EvaluateActivity(CreateSnapshot("Game"));
            Assert.Equal(4, events.Count);
            Assert.Equal(InterventionLevel.FullBlock, events[3].Level);
        }
    }
}
