using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ApplicationAssistant.AI;

public sealed class CvGenerator(ILLMService llm) : ICvGenerator
{
    private const string SystemPrompt = """
        You write tailored CVs optimized to score highly against the given job advertisement in ATS and AI screening tools.
        Use ONLY the candidate's provided experience and education. Never invent employers, degrees, dates, tools, or achievements that are not grounded in the source text.

        Alignment (critical):
        - Extract the job ad's must-have skills, tools, domain terms, responsibilities, and seniority language.
        - Mirror those exact phrases and keywords in the summary, skills list, and bullets wherever the candidate's experience truthfully supports them (same meaning; prefer the ad's wording over synonyms).
        - Lead with the strongest matches: put the most ad-relevant achievements first in each role.
        - In the summary, explicitly connect the candidate to the target role using the ad's key requirements (3-4 sentences, dense with job-ad keywords).
        - Build skills[] primarily from terms that appear in the job ad and are evidenced in the candidate's experience; list the highest-priority matches first.
        - Prefer roles that match the ad; assign higher relevanceScore (0-100) to closer matches.
        - Skip short stints: if a role lasted under 3 months (from StartDate to EndDate, or to today if IsCurrent), set include=false unless it is very relevant to the job ad (roughly relevanceScore >= 80).
        - Mark clearly irrelevant roles with include=false, but keep roles that fill career timeline when unsure (except short stints under 3 months that are not very relevant).

        Bullets:
        - Rewrite to emphasize evidence for the job ad's requirements while staying truthful to the source description.
        - Prefer concrete outcomes (scope, impact, tech/stack, methods) phrased with the ad's vocabulary.
        - Prefer 2-5 bullets per included role.
        - Tense: for roles with IsCurrent=true or EndDate=Present, write bullets in present tense (e.g. "Lead…", "Own…", "Deliver…"). For all past roles, use past tense (e.g. "Led…", "Owned…", "Delivered…").

        Return JSON matching the sample shape exactly.
        """;

    public async Task<CvDraft> GenerateAsync(CvGenerationRequest request, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        var sample = BuildSample(request);
        var history = new[]
        {
            new InputItem
            {
                FromUser = true,
                Text = BuildUserPayload(request)
            }
        };

        var draft = await llm.GetResponseAsync(sample, SystemPrompt, history, temperature: 0.2m);
        return NormalizeDraft(draft, request);
    }

    private static CvDraft BuildSample(CvGenerationRequest request) => new()
    {
        Summary = "Results-driven professional with experience aligned to the target role.",
        Skills = ["Communication", "Problem solving"],
        Experiences = request.Experiences.Select(e => new CvDraftExperience
        {
            Id = e.Id,
            Include = true,
            RelevanceScore = 70,
            Company = e.Company,
            Title = e.Title,
            Location = e.Location,
            Bullets =
            [
                e.IsCurrent
                    ? "Deliver outcomes relevant to the target role."
                    : "Delivered outcomes relevant to the target role."
            ]
        }).ToList(),
        Education = request.Education.Select(e => new CvDraftEducation
        {
            Id = e.Id,
            Include = true,
            Institution = e.Institution,
            Degree = e.Degree,
            FieldOfStudy = e.FieldOfStudy,
            Description = e.Description
        }).ToList()
    };

    private static string BuildUserPayload(CvGenerationRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Candidate: {request.FullName} <{request.Email}>");
        if (!string.IsNullOrWhiteSpace(request.TargetCompany))
        {
            sb.AppendLine($"Target company: {request.TargetCompany}");
        }

        if (!string.IsNullOrWhiteSpace(request.TargetRoleTitle))
        {
            sb.AppendLine($"Target role: {request.TargetRoleTitle}");
        }

        sb.AppendLine();
        sb.AppendLine("JOB AD:");
        sb.AppendLine(request.JobAd.Trim());
        sb.AppendLine();
        sb.AppendLine("SOURCE EXPERIENCE (JSON):");
        sb.AppendLine(JsonSerializer.Serialize(request.Experiences.Select(e => new
        {
            e.Id,
            e.Company,
            e.Title,
            e.Location,
            StartDate = FormatDate(e.StartDate),
            EndDate = e.IsCurrent ? "Present" : FormatDate(e.EndDate),
            e.IsCurrent,
            e.Description
        })));
        sb.AppendLine();
        sb.AppendLine("SOURCE EDUCATION (JSON):");
        sb.AppendLine(JsonSerializer.Serialize(request.Education.Select(e => new
        {
            e.Id,
            e.Institution,
            e.Degree,
            e.FieldOfStudy,
            StartDate = FormatDate(e.StartDate),
            EndDate = FormatDate(e.EndDate),
            e.Description
        })));
        return sb.ToString();
    }

    private static string? FormatDate(DateTime? value) =>
        value?.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static CvDraft NormalizeDraft(CvDraft draft, CvGenerationRequest request)
    {
        var experienceById = request.Experiences.ToDictionary(e => e.Id);
        var educationById = request.Education.ToDictionary(e => e.Id);

        draft.Summary = string.IsNullOrWhiteSpace(draft.Summary)
            ? "Experienced professional seeking the target role."
            : draft.Summary.Trim();

        draft.Skills = (draft.Skills ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();

        var experiences = new List<CvDraftExperience>();
        foreach (var item in draft.Experiences ?? [])
        {
            if (!experienceById.TryGetValue(item.Id, out var source))
            {
                continue;
            }

            experiences.Add(new CvDraftExperience
            {
                Id = source.Id,
                Include = item.Include,
                RelevanceScore = Math.Clamp(item.RelevanceScore, 0, 100),
                Company = string.IsNullOrWhiteSpace(item.Company) ? source.Company : item.Company.Trim(),
                Title = string.IsNullOrWhiteSpace(item.Title) ? source.Title : item.Title.Trim(),
                Location = string.IsNullOrWhiteSpace(item.Location) ? source.Location : item.Location.Trim(),
                Bullets = (item.Bullets ?? [])
                    .Where(b => !string.IsNullOrWhiteSpace(b))
                    .Select(b => b.Trim())
                    .Take(6)
                    .ToList()
            });
        }

        // Ensure every source role appears so aligner can decide gap fillers.
        foreach (var source in request.Experiences)
        {
            if (experiences.Any(e => e.Id == source.Id))
            {
                continue;
            }

            experiences.Add(new CvDraftExperience
            {
                Id = source.Id,
                Include = true,
                RelevanceScore = 40,
                Company = source.Company,
                Title = source.Title,
                Location = source.Location,
                Bullets = SplitDescription(source.Description)
            });
        }

        foreach (var exp in experiences.Where(e => e.Include && e.Bullets.Count == 0))
        {
            if (experienceById.TryGetValue(exp.Id, out var source))
            {
                exp.Bullets = SplitDescription(source.Description);
            }
        }

        draft.Experiences = experiences;

        var education = new List<CvDraftEducation>();
        foreach (var item in draft.Education ?? [])
        {
            if (!educationById.TryGetValue(item.Id, out var source))
            {
                continue;
            }

            education.Add(new CvDraftEducation
            {
                Id = source.Id,
                Include = item.Include,
                Institution = string.IsNullOrWhiteSpace(item.Institution) ? source.Institution : item.Institution.Trim(),
                Degree = string.IsNullOrWhiteSpace(item.Degree) ? source.Degree : item.Degree.Trim(),
                FieldOfStudy = string.IsNullOrWhiteSpace(item.FieldOfStudy) ? source.FieldOfStudy : item.FieldOfStudy.Trim(),
                Description = string.IsNullOrWhiteSpace(item.Description) ? source.Description : item.Description.Trim()
            });
        }

        foreach (var source in request.Education)
        {
            if (education.Any(e => e.Id == source.Id))
            {
                continue;
            }

            education.Add(new CvDraftEducation
            {
                Id = source.Id,
                Include = true,
                Institution = source.Institution,
                Degree = source.Degree,
                FieldOfStudy = source.FieldOfStudy,
                Description = source.Description
            });
        }

        draft.Education = education;
        return draft;
    }

    private static List<string> SplitDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return ["Contributed to team delivery and project outcomes."];
        }

        return description
            .Split(['\n', ';', '•'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 8)
            .Take(4)
            .ToList();
    }
}
