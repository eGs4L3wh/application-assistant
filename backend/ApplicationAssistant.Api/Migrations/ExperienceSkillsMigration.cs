using System.Text;
using ApplicationAssistant.AI;
using ApplicationAssistant.Api.Models;
using MongoDB.Driver;

namespace ApplicationAssistant.Api;

/// <summary>
/// Retroactively extracts per-role skill tags from experience descriptions via Gemini.
/// Only fills experiences that have a description and an empty Skills list.
/// </summary>
public static class ExperienceSkillsMigration
{
    private const string SystemPrompt = """
        You extract skill tags from a single job experience.
        Return short concrete tags evidenced in the text: programming languages, frameworks,
        cloud providers, databases, tools, platforms, and methods.
        Prefer canonical names (e.g. "C#", "AWS", "Kubernetes", "PostgreSQL").
        Never invent skills that are not grounded in the provided title, company, or description.
        Typically return 4-12 tags; return an empty list when nothing is clear.
        Return JSON matching the sample shape exactly.
        """;

    public static async Task<ExperienceSkillsMigrationResult> RunAsync(
        IMongoCollection<AppUser> users,
        ILLMService llm,
        CancellationToken ct = default)
    {
        var allUsers = await users.Find(FilterDefinition<AppUser>.Empty).ToListAsync(ct);
        var usersTouched = 0;
        var experiencesFilled = 0;
        var experiencesSkipped = 0;
        var experiencesFailed = 0;

        foreach (var user in allUsers)
        {
            var changed = false;

            foreach (var exp in user.Experience)
            {
                if (exp.Skills is { Count: > 0 })
                {
                    experiencesSkipped++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(exp.Description)
                    && string.IsNullOrWhiteSpace(exp.Title)
                    && string.IsNullOrWhiteSpace(exp.Company))
                {
                    experiencesSkipped++;
                    continue;
                }

                try
                {
                    var skills = await ExtractSkillsAsync(llm, exp, ct);
                    if (skills.Count == 0)
                    {
                        Console.WriteLine(
                            $"  skip (no skills) {user.Email} · {exp.Title} @ {exp.Company}");
                        experiencesSkipped++;
                        continue;
                    }

                    exp.Skills = skills;
                    changed = true;
                    experiencesFilled++;
                    Console.WriteLine(
                        $"  filled {user.Email} · {exp.Title} @ {exp.Company}: {string.Join(", ", skills)}");
                }
                catch (Exception ex)
                {
                    experiencesFailed++;
                    Console.WriteLine(
                        $"  FAILED {user.Email} · {exp.Title} @ {exp.Company}: {ex.Message}");
                }
            }

            if (!changed)
            {
                continue;
            }

            await users.ReplaceOneAsync(u => u.Id == user.Id, user, cancellationToken: ct);
            usersTouched++;
        }

        return new ExperienceSkillsMigrationResult(
            usersTouched,
            experiencesFilled,
            experiencesSkipped,
            experiencesFailed);
    }

    private static async Task<List<string>> ExtractSkillsAsync(
        ILLMService llm,
        WorkExperience exp,
        CancellationToken ct)
    {
        _ = ct;

        var sample = new ExperienceSkillsExtraction
        {
            Skills = ["C#", "Azure", "Kubernetes"]
        };

        var payload = new StringBuilder();
        payload.AppendLine($"Company: {exp.Company}");
        payload.AppendLine($"Title: {exp.Title}");
        if (!string.IsNullOrWhiteSpace(exp.Location))
        {
            payload.AppendLine($"Location: {exp.Location}");
        }

        payload.AppendLine();
        payload.AppendLine("DESCRIPTION:");
        payload.AppendLine(string.IsNullOrWhiteSpace(exp.Description) ? "(none)" : exp.Description.Trim());

        var history = new[]
        {
            new InputItem
            {
                FromUser = true,
                Text = payload.ToString()
            }
        };

        var result = await llm.GetResponseAsync(sample, SystemPrompt, history, temperature: 0.1m, maxTokens: 1024);
        return NormalizeSkills(result.Skills);
    }

    private static List<string> NormalizeSkills(IEnumerable<string>? skills) =>
        (skills ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

    private sealed class ExperienceSkillsExtraction
    {
        public List<string> Skills { get; set; } = [];
    }
}

public readonly record struct ExperienceSkillsMigrationResult(
    int UsersTouched,
    int ExperiencesFilled,
    int ExperiencesSkipped,
    int ExperiencesFailed);
