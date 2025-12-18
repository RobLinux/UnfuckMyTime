using System;
using System.Collections.Generic;

namespace UnfuckMyTime.Core.Models
{
    public enum InterventionLevel
    {
        Notification,
        WindowWiggle,
        FullBlock
    }

    public class DistractionEvent
    {
        public string Message { get; set; } = string.Empty;
        public InterventionLevel Level { get; set; }
        public double Intensity { get; set; } = 1.0;
    }

    public class SessionPlan
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string GoalText { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public List<string> AllowedApps { get; set; } = new();
        public List<string> AllowedDomains { get; set; } = new();
        public List<string> AllowedTitleKeywords { get; set; } = new();
        public int SlackBudgetSeconds { get; set; } = 15;
        public int NotificationIntervalSeconds { get; set; } = 30;
        public int Phase2IntervalSeconds { get; set; } = 10;
        public int Phase2Threshold { get; set; } = 3;
        public int Phase3Threshold { get; set; } = 5; // Max wiggles before Phase 3
        public int Phase3DelaySeconds { get; set; } = 10;
        public int SlackWindowSeconds { get; set; }
        public string? MainAppProcessName { get; set; }

        public TimeSpan Duration => EndTime - StartTime;
    }

    public class ActivitySnapshot
    {
        public DateTime Timestamp { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public string WindowTitle { get; set; } = string.Empty;
        public string? Url { get; set; }
        public bool IsIdle { get; set; }
    }

    public enum RuleScope
    {
        SessionOnly,
        Permanent
    }

    public enum RuleMatchType
    {
        Domain,
        UrlRegex,
        Process,
        TitleContains
    }

    public class ExceptionRule
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public RuleScope Scope { get; set; }
        public RuleMatchType MatchType { get; set; }
        public string Value { get; set; } = string.Empty; // The domain, regex, or process name
        public string? Rationale { get; set; }
        public DateTime? ExpiresAt { get; set; }

        public bool IsExpired(DateTime now) => ExpiresAt.HasValue && now > ExpiresAt.Value;
    }

    public class SessionStatusSnapshot
    {
        public TimeSpan SlackRemaining { get; set; }
        public TimeSpan TotalSlackBudget { get; set; }
        public TimeSpan? TimeUntilReset { get; set; }
        public bool IsDistracted { get; set; }
        public bool IsPaused { get; set; }
        public bool IsCurrentAppAllowed { get; set; } = true;
    }
}
