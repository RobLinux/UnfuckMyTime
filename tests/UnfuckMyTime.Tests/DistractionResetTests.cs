using System;
using System.Collections.Generic;
using UnfuckMyTime.Core.Models;
using UnfuckMyTime.Core.Services;
using Xunit;

namespace UnfuckMyTime.Tests
{
    public class DistractionResetTests
    {
        private readonly SessionManager _manager;
        private DateTime _virtualTime;

        public DistractionResetTests()
        {
            _manager = new SessionManager();
            _virtualTime = new DateTime(2025, 1, 1, 12, 0, 0);
            _manager.TimeProvider = () => _virtualTime;
        }

        [Fact]
        public void SlackBudget_Resets_After_Continuous_Focus_In_SlackWindow()
        {
            // Arrange
            var plan = new SessionPlan
            {
                SlackBudgetSeconds = 10,
                SlackWindowSeconds = 300,
                AllowedApps = new List<string> { "work" }
            };
            _manager.StartSession(plan);

            bool distractionDetected = false;
            _manager.DistractionDetected += (_, _) => distractionDetected = true;

            // Act 1: Distract for 5 seconds (50% of budget)
            var distractedActivity = new ActivitySnapshot { ProcessName = "reddit", Timestamp = _virtualTime };
            _manager.EvaluateActivity(distractedActivity); // Start

            _virtualTime = _virtualTime.AddSeconds(5);
            _manager.EvaluateActivity(distractedActivity); // 5s elapsed

            // Falling edge (back to work)
            _virtualTime = _virtualTime.AddSeconds(1);
            var focusedActivity = new ActivitySnapshot { ProcessName = "work", Timestamp = _virtualTime };
            _manager.EvaluateActivity(focusedActivity);

            // Act 2: Stay focused for 6 minutes (> 5 min Window)
            _virtualTime = _virtualTime.AddMinutes(6);
            _manager.EvaluateActivity(focusedActivity); // Still focused

            // Act 3: Distract again. Should be fresh budget.
            // If reset worked, we have 0s accumulated. Limit is 10s.
            // We distract for 6s. Total if NO reset = 5+6 = 11s (Detection!).
            // Total IF reset = 0+6 = 6s (No detection).

            _virtualTime = _virtualTime.AddSeconds(1);
            _manager.EvaluateActivity(distractedActivity); // Start new distraction block

            _virtualTime = _virtualTime.AddSeconds(6);
            _manager.EvaluateActivity(distractedActivity); // 6s elapsed in this block

            // Assert
            Assert.False(distractionDetected, "Distraction should NOT be detected because budget should have reset.");
        }

        [Fact]
        public void SlackBudget_Does_NOT_Reset_If_Focus_Is_Too_Short()
        {
            // Arrange
            var plan = new SessionPlan
            {
                SlackBudgetSeconds = 10,
                SlackWindowSeconds = 300,
                AllowedApps = new List<string> { "work" }
            };
            _manager.StartSession(plan);

            bool distractionDetected = false;
            _manager.DistractionDetected += (_, _) => distractionDetected = true;

            // Act 1: Distract for 5 seconds
            var distractedActivity = new ActivitySnapshot { ProcessName = "reddit", Timestamp = _virtualTime };
            _manager.EvaluateActivity(distractedActivity);
            _virtualTime = _virtualTime.AddSeconds(5);
            _manager.EvaluateActivity(distractedActivity);

            // Falling edge (back to work)
            _virtualTime = _virtualTime.AddSeconds(1);
            var focusedActivity = new ActivitySnapshot { ProcessName = "work", Timestamp = _virtualTime };
            _manager.EvaluateActivity(focusedActivity);

            // Act 2: Focused for only 2 minutes (< 5 min Window)
            _virtualTime = _virtualTime.AddMinutes(2);
            _manager.EvaluateActivity(focusedActivity);

            // Act 3: Distract again for 6s.
            // Total = 5 + 6 = 11s > 10s Budget. Should fire.
            _virtualTime = _virtualTime.AddSeconds(1);
            _manager.EvaluateActivity(distractedActivity);

            _virtualTime = _virtualTime.AddSeconds(6);
            _manager.EvaluateActivity(distractedActivity);

            // Assert
            Assert.True(distractionDetected, "Distraction MUST be detected because focus time was too short to reset.");
        }
    }
}
