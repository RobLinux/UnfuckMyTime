using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using OpenAI;
using OpenAI.Chat;
using UnfuckMyTime.Core.Models;
using UnfuckMyTime.Core.Services;

namespace UnfuckMyTime.Infrastructure.Services
{
    public class OpenAIGoalService : IAIGoalService
    {
        public async Task<GeneratedPlan> GeneratePlanFromPromptAsync(string prompt, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentNullException(nameof(apiKey), "OpenAI API Key is required");

            var client = new ChatClient("gpt-4o", new ApiKeyCredential(apiKey));

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(@"You are a strict but helpful Focus Coach. 
                Your job is to parse the user's plain text goal into a strict JSON plan.
                
                Analyze the goal for:
                1. Duration (default 30m if unspecified)
                2. Allowed Apps (Process names like 'devenv', 'code', 'spotify')
                   - NOTE: Browsers (chrome, msedge) in this list will be restricted to AllowedDomains/TitleKeywords if any are specified.
                3. Allowed Domains (e.g. 'stackoverflow.com', 'microsoft.com')
                4. Allowed Title Keywords (e.g. 'ChatGPT', 'Jira', 'Pull Request', 'Documentation')
                   - Use this for websites that redirect or have dynamic URLs (like ChatGPT).
                5. Distraction Limits:
                   - SlackBudgetSeconds: How many seconds of distraction allowed before users get punished.
                   - SlackWindowSeconds: How many SECONDS of continuous focus required to FULLY RESET the SlackBudgetSeconds.

                Return ONLY raw JSON matching this schema:
                {
                    ""DurationMinutes"": 90,
                    ""AllowedApps"": [""devenv"", ""chrome""],
                    ""AllowedDomains"": [""github.com""],
                    ""AllowedTitleKeywords"": [""ChatGPT"", ""Jira""],
                    ""SlackBudgetSeconds"": 20,
                    ""SlackWindowSeconds"": 300,
                    ""Reasoning"": ""Brief explanation of the plan""
                }"),
                new UserChatMessage(prompt)
            };

            ChatCompletion completion = await client.CompleteChatAsync(messages);

            var responseText = completion.Content[0].Text;

            // Basic cleanup if the model creates markdown blocks
            responseText = responseText.Replace("```json", "").Replace("```", "").Trim();

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var plan = JsonSerializer.Deserialize<GeneratedPlan>(responseText, options);

            return plan ?? new GeneratedPlan { Reasoning = "Failed to parse plan." };
        }
    }
}
