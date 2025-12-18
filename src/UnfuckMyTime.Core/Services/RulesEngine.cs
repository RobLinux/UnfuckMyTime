using System;
using System.Linq;
using System.Text.RegularExpressions;
using UnfuckMyTime.Core.Models;

namespace UnfuckMyTime.Core.Services
{
    public enum ActivityClassification
    {
        OnTrack,
        Distraction,
        Exempt
    }

    public class RulesEngine
    {
        public ActivityClassification Evaluate(ActivitySnapshot activity, SessionPlan plan, IEnumerable<ExceptionRule> exceptions)
        {
            // 0. Check System Exemptions (Always Allowed)
            if (IsSystemExempt(activity))
            {
                return ActivityClassification.Exempt;
            }

            // 1. Check Exceptions (Highest Priority)
            if (IsException(activity, exceptions))
            {
                    return ActivityClassification.Exempt;
            }

            // 2. Check Session Allowlist
            if (IsAllowed(activity, plan))
            {
                return ActivityClassification.OnTrack;
            }

            // 3. Default Result
            return ActivityClassification.Distraction;
        }

        private bool IsSystemExempt(ActivitySnapshot activity)
        {
            // Always allow basic system navigation and the app itself
            return string.Equals(activity.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(activity.ProcessName, "UnfuckMyTime.UI", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(activity.ProcessName, "ShellHost", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsException(ActivitySnapshot activity, IEnumerable<ExceptionRule> exceptions)
        {
            var activeExceptions = exceptions.Where(e => !e.IsExpired(activity.Timestamp));

            foreach (var rule in activeExceptions)
            {
                bool match = rule.MatchType switch
                {
                    RuleMatchType.Process => string.Equals(activity.ProcessName, rule.Value, StringComparison.OrdinalIgnoreCase),
                    RuleMatchType.TitleContains => activity.WindowTitle.Contains(rule.Value, StringComparison.OrdinalIgnoreCase),
                    RuleMatchType.Domain => IsDomainMatch(activity.Url, rule.Value),
                    RuleMatchType.UrlRegex => !string.IsNullOrEmpty(activity.Url) && Regex.IsMatch(activity.Url, rule.Value),
                    _ => false
                };

                if (match) return true;
            }
            return false;
        }

        private static readonly string[] Browsers = { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi" };

        private bool IsAllowed(ActivitySnapshot activity, SessionPlan plan)
        {
            // Special handling for Browsers:
            // If the process is a browser, AND we have specific domain/title constraints,
            // we should NOT blindly allow the process. We must verify the content.
            bool isBrowser = Browsers.Any(b => string.Equals(activity.ProcessName, b, StringComparison.OrdinalIgnoreCase));
            bool hasContentConstraints = plan.AllowedDomains.Any() || plan.AllowedTitleKeywords.Any();

            if (isBrowser && hasContentConstraints)
            {
                // FALL THROUGH to check URL/Title below.
                // Do NOT return true just because "chrome" might be in AllowedApps (unless constraints are empty).
            }
            else
            {
                // Standard App Check
                if (plan.AllowedApps.Any(app => string.Equals(activity.ProcessName, app, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            // Check URL Domains
            if (plan.AllowedDomains.Any(d => IsDomainMatch(activity.Url, d)))
            {
                return true;
            }

            // Check Title Keywords
            if (!string.IsNullOrEmpty(activity.WindowTitle) && plan.AllowedTitleKeywords.Any(k => activity.WindowTitle.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return false;
        }

        private static bool IsDomainMatch(string? url, string domain)
        {
            if (string.IsNullOrEmpty(url)) return false;
            
            // Try explicit URI first
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return uri.Host.EndsWith(domain, StringComparison.OrdinalIgnoreCase);
            }
            
            // Fallback for partials or weird formats
            return url.Contains(domain, StringComparison.OrdinalIgnoreCase);
        }
    }
}
