using System.Threading.Tasks;
using UnfuckMyTime.Core.Models;

namespace UnfuckMyTime.Core.Services
{
    public class GeneratedPlan
    {
        public int DurationMinutes { get; set; }
        public List<string> AllowedApps { get; set; } = new();
        public List<string> AllowedDomains { get; set; } = new();
        public List<string> AllowedTitleKeywords { get; set; } = new();
        public int SlackBudgetSeconds { get; set; } = 15;
        public int SlackWindowSeconds { get; set; } = 60;
        public string Reasoning { get; set; } = string.Empty;
    }

    public interface IAIGoalService
    {
        Task<GeneratedPlan> GeneratePlanFromPromptAsync(string prompt, string apiKey, string model);
    }
}
