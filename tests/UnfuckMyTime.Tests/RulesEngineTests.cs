using System;
using System.Collections.Generic;
using UnfuckMyTime.Core.Models;
using UnfuckMyTime.Core.Services;
using Xunit;

namespace UnfuckMyTime.Tests
{
    public class RulesEngineTests
    {
        private readonly RulesEngine _engine;
        private readonly List<ExceptionRule> _exceptions;

        public RulesEngineTests()
        {
            _engine = new RulesEngine();
            _exceptions = new List<ExceptionRule>();
        }

        [Fact]
        public void Browser_ShouldBeAllowed_IfNoConstraintsExist()
        {
            // Plan allows Chrome, no specific domains.
            var plan = new SessionPlan { AllowedApps = { "chrome" } };
            var activity = new ActivitySnapshot { ProcessName = "chrome", Url = "any.com" };

            var result = _engine.Evaluate(activity, plan, _exceptions);

            Assert.Equal(ActivityClassification.OnTrack, result);
        }

        [Fact]
        public void Browser_ShouldBeBlocked_IfConstraintsExist_AndContentMismatch()
        {
            // Plan allows Chrome, BUT restricts to google.com
            // This tests the "Browser Trap" logic.
            var plan = new SessionPlan
            {
                AllowedApps = { "chrome" },
                AllowedDomains = { "google.com" }
            };

            // User visits Reddit on Chrome
            var activity = new ActivitySnapshot { ProcessName = "chrome", Url = "https://reddit.com" };

            var result = _engine.Evaluate(activity, plan, _exceptions);

            Assert.Equal(ActivityClassification.Distraction, result);
        }

        [Fact]
        public void Browser_ShouldBeAllowed_IfConstraintsExist_AndContentMatch()
        {
            var plan = new SessionPlan
            {
                AllowedApps = { "chrome" },
                AllowedDomains = { "google.com" }
            };

            var activity = new ActivitySnapshot { ProcessName = "chrome", Url = "https://mail.google.com" };

            var result = _engine.Evaluate(activity, plan, _exceptions);

            Assert.Equal(ActivityClassification.OnTrack, result);
        }

        [Fact]
        public void Browser_ShouldBeAllowed_ByTitleKeyword()
        {
            // Plan allows ChatGPT via title
            var plan = new SessionPlan
            {
                AllowedApps = { "chrome" },
                AllowedTitleKeywords = { "ChatGPT" }
            };

            // URL might be weird, but Title matches
            var activity = new ActivitySnapshot
            {
                ProcessName = "chrome",
                Url = "https://chatgpt.com/c/123",
                WindowTitle = "ChatGPT - OpenAI"
            };

            var result = _engine.Evaluate(activity, plan, _exceptions);

            Assert.Equal(ActivityClassification.OnTrack, result);
        }

        [Fact]
        public void NonBrowser_ShouldBeAllowed_ByAppName_EvenWithConstraints()
        {
            // VS Code allowed. We also have domain constraints for Chrome.
            // VS Code should NOT be subject to domain constraints.
            var plan = new SessionPlan
            {
                AllowedApps = { "Code", "chrome" },
                AllowedDomains = { "google.com" }
            };

            var activity = new ActivitySnapshot { ProcessName = "Code", WindowTitle = "Project1" };

            var result = _engine.Evaluate(activity, plan, _exceptions);

            Assert.Equal(ActivityClassification.OnTrack, result);
        }
    }
}
