using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ApplicationAssistant.AI;

public sealed class CvGenerator(ILLMService llm) : ICvGenerator
{
    private const string SystemPrompt = """
        You write tailored CVs optimized to score highly against the given job advertisement in ATS and AI screening tools.
        Use ONLY the candidate's provided experience and education. Never invent employers, degrees, dates, tools, or achievements that are not grounded in the source text.

        Job metadata (required):
        - Extract targetCompany and targetRoleTitle from the job advertisement.
        - Prefer the official employer name and the posted role title as written in the ad.
        - If either cannot be determined confidently, leave it as an empty string.

        Identity (critical):
        - Preserve each experience and education Id EXACTLY as given in the source JSON. Do not invent or omit Ids.

        Alignment (critical):
        - Extract the job ad's must-have skills, tools, domain terms, responsibilities, and seniority language.
        - Mirror those exact phrases and keywords in the summary, skills list, titles, and descriptions wherever the candidate's experience truthfully supports them (same meaning; prefer the ad's wording over synonyms).
        - Lead with the strongest matches: put the most ad-relevant achievements first in each role description.
        - In the summary, explicitly connect the candidate to the target role using the ad's key requirements (4-6 sentences, dense with job-ad keywords and concrete evidence from the career).
        - Build skills[] primarily from terms that appear in the job ad and are evidenced in the candidate's experience; list the highest-priority matches first. Prefer a fuller skills list (typically 12-20 items) over a short one when the source supports it.
        - Prefer roles that match the ad; assign higher relevanceScore (0-100) to closer matches.
        - Skip short stints: if a role lasted under 3 months (from StartDate to EndDate, or to today if IsCurrent), set include=false unless it is very relevant to the job ad (roughly relevanceScore >= 80).
        - Mark clearly irrelevant roles with include=false, but when unsure prefer include=true so the CV keeps career breadth and length (except short stints under 3 months that are not very relevant).

        Titles / roles:
        - Keep Company as the real employer name from the source (do not replace with the target company).
        - Amend Title for each included role so it emphasizes the specialty most relevant to the job ad, when that framing is truthful to the source. Prefer the ad's role vocabulary over a generic source title when both fit.
        - Do not invent promotions, seniority the candidate did not hold, or fake job titles.

        Descriptions:
        - For include=true roles: rewrite Description as substantial free text tailored to the job ad, grounded in the source Description. Expand and rephrase — never paste the source Description verbatim unchanged, and never strip the source down to a thin summary.
        - Target length per included role: typically 4-8 sentences or a short multi-paragraph block (use line breaks between ideas). Cover scope, responsibilities, tech/stack, methods, stakeholders, and concrete outcomes wherever the source supports them.
        - Prioritize the most ad-relevant achievements first, then retain other truthful detail from the source so the role still reads full and credible.
        - For include=false roles: you may leave Description empty or briefly unchanged.
        - Prefer concrete outcomes (scope, impact, tech/stack, methods) phrased with the ad's vocabulary.
        - Tense: for roles with IsCurrent=true or EndDate=Present, write in present tense. For past roles, use past tense.

        Output length (critical):
        - The finished CV should land around two A4 pages when rendered — not a sparse one-page CV. Err on the side of richer descriptions and more included roles when the source supports it.
        - Do not omit experience entries; always return every source Id.
        - Still return valid complete JSON; do not truncate mid-field.

        Return JSON matching the sample shape exactly (same fields and Ids), with rewritten summary, skills, titles, and descriptions.
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

        var draft = await llm.GetResponseAsync(sample, SystemPrompt, history, temperature: 0.35m, maxTokens: 16384);
        return NormalizeDraft(draft, request);
    }

    private const string RefineSystemPrompt = """
        You edit a single CV job description based on the user's instruction.
        Stay truthful to the source experience — never invent employers, tools, achievements, or seniority.
        Keep the result as free-text Description (short paragraph or a few line breaks).
        Prefer the job ad's vocabulary when it truthfully fits.
        Return JSON matching the sample shape exactly.
        """;

    public async Task<ExperienceRefineResult> RefineExperienceAsync(
        ExperienceRefineRequest request,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        var sample = new ExperienceRefineResult
        {
            Description = request.CurrentDescription ?? "Revised role description."
        };

        var history = new[]
        {
            new InputItem
            {
                FromUser = true,
                Text = BuildRefinePayload(request)
            }
        };

        var result = await llm.GetResponseAsync(sample, RefineSystemPrompt, history, temperature: 0.4m, maxTokens: 2048);
        result.Description = string.IsNullOrWhiteSpace(result.Description)
            ? (request.CurrentDescription ?? string.Empty)
            : result.Description.Trim();
        return result;
    }

    private static string BuildRefinePayload(ExperienceRefineRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Target company: {request.TargetCompany}");
        sb.AppendLine($"Target role: {request.TargetRoleTitle}");
        sb.AppendLine();
        sb.AppendLine("JOB AD:");
        sb.AppendLine(request.JobAd.Trim());
        sb.AppendLine();
        sb.AppendLine("ROLE BEING EDITED:");
        sb.AppendLine(JsonSerializer.Serialize(new
        {
            request.Company,
            request.Title,
            request.Location,
            CurrentDescription = request.CurrentDescription,
            SourceDescription = request.SourceDescription
        }));
        sb.AppendLine();
        sb.AppendLine("USER EDIT INSTRUCTION:");
        sb.AppendLine(request.UserPrompt.Trim());
        return sb.ToString();
    }

    private static CvDraft BuildSample(CvGenerationRequest request) => new()
    {
        TargetCompany = request.TargetCompany ?? "Acme Corp",
        TargetRoleTitle = request.TargetRoleTitle ?? "Software Engineer",
        Summary = "Short tailored summary.",
        Skills = ["Skill A", "Skill B"],
            Experiences = request.Experiences.Select(e => new CvDraftExperience
            {
                Id = e.Id,
                Include = true,
                RelevanceScore = 70,
                Company = e.Company,
                Title = e.Title,
                Location = e.Location,
                StartDate = e.StartDate,
                EndDate = e.EndDate,
                IsCurrent = e.IsCurrent,
                EngagementType = e.EngagementType,
                Description = e.Description
            }).ToList(),
        Education = request.Education.Select(e => new CvDraftEducation
        {
            Id = e.Id,
            Include = true,
            Institution = e.Institution,
            Degree = e.Degree,
            FieldOfStudy = e.FieldOfStudy,
            StartDate = e.StartDate,
            EndDate = e.EndDate,
            Description = e.Description
        }).ToList()
    };

    private static string BuildUserPayload(CvGenerationRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Candidate: {request.FullName} <{request.Email}>");
        if (!string.IsNullOrWhiteSpace(request.Phone))
        {
            sb.AppendLine($"Phone: {request.Phone}");
        }
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
            e.EngagementType,
            Description = Truncate(e.Description, 700)
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
            Description = Truncate(e.Description, 400)
        })));
        return sb.ToString();
    }

    private static string? Truncate(string? value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        var trimmed = value.Trim();
        return trimmed.Length <= maxChars ? trimmed : trimmed[..maxChars] + "…";
    }

    private static string? FormatDate(DateTime? value) =>
        value?.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static CvDraft NormalizeDraft(CvDraft draft, CvGenerationRequest request)
    {
        var experienceById = request.Experiences.ToDictionary(e => e.Id);
        var educationById = request.Education.ToDictionary(e => e.Id);

        draft.TargetCompany = string.IsNullOrWhiteSpace(draft.TargetCompany)
            ? (string.IsNullOrWhiteSpace(request.TargetCompany) ? null : request.TargetCompany.Trim())
            : draft.TargetCompany.Trim();
        draft.TargetRoleTitle = string.IsNullOrWhiteSpace(draft.TargetRoleTitle)
            ? (string.IsNullOrWhiteSpace(request.TargetRoleTitle) ? null : request.TargetRoleTitle.Trim())
            : draft.TargetRoleTitle.Trim();

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
        var claimedSourceIds = new HashSet<Guid>();
        var unmatchedDraft = new List<CvDraftExperience>();

        foreach (var item in draft.Experiences ?? [])
        {
            if (item.Id != Guid.Empty && experienceById.TryGetValue(item.Id, out var sourceById) && claimedSourceIds.Add(sourceById.Id))
            {
                experiences.Add(MergeExperience(sourceById, item));
                continue;
            }

            unmatchedDraft.Add(item);
        }

        var remainingSources = request.Experiences.Where(s => !claimedSourceIds.Contains(s.Id)).ToList();
        foreach (var item in unmatchedDraft.ToList())
        {
            var match = remainingSources.FirstOrDefault(s =>
                string.Equals(s.Company, item.Company, StringComparison.OrdinalIgnoreCase)
                && string.Equals(s.Title, item.Title, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                var companyMatches = remainingSources
                    .Where(s => string.Equals(s.Company, item.Company, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (companyMatches.Count == 1)
                {
                    match = companyMatches[0];
                }
            }

            if (match is null)
            {
                continue;
            }

            remainingSources.Remove(match);
            unmatchedDraft.Remove(item);
            claimedSourceIds.Add(match.Id);
            experiences.Add(MergeExperience(match, item));
        }

        for (var i = 0; i < unmatchedDraft.Count && i < remainingSources.Count; i++)
        {
            var source = remainingSources[i];
            claimedSourceIds.Add(source.Id);
            experiences.Add(MergeExperience(source, unmatchedDraft[i]));
        }

        foreach (var source in request.Experiences)
        {
            if (claimedSourceIds.Contains(source.Id))
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
                StartDate = source.StartDate,
                EndDate = source.EndDate,
                IsCurrent = source.IsCurrent,
                EngagementType = source.EngagementType,
                Description = source.Description
            });
        }

        foreach (var exp in experiences.Where(e => e.Include && string.IsNullOrWhiteSpace(e.Description)))
        {
            if (experienceById.TryGetValue(exp.Id, out var source))
            {
                exp.Description = source.Description ?? "Contributed to team delivery and project outcomes.";
            }
        }

        foreach (var exp in experiences.Where(e => IsPlaceholderTitle(e.Title)))
        {
            if (experienceById.TryGetValue(exp.Id, out var source))
            {
                exp.Title = source.Title;
            }
        }

        draft.Experiences = experiences;

        if (IsPlaceholderSummary(draft.Summary))
        {
            draft.Summary = "Experienced professional seeking the target role.";
        }

        draft.Skills = draft.Skills
            .Where(s => !IsPlaceholderSkill(s))
            .ToList();

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
                StartDate = item.StartDate ?? source.StartDate,
                EndDate = item.EndDate ?? source.EndDate,
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
                StartDate = source.StartDate,
                EndDate = source.EndDate,
                Description = source.Description
            });
        }

        draft.Education = education;
        return draft;
    }

    private static CvDraftExperience MergeExperience(CvSourceExperience source, CvDraftExperience item)
    {
        var title = string.IsNullOrWhiteSpace(item.Title) || IsPlaceholderTitle(item.Title)
            ? source.Title
            : item.Title.Trim();
        var description = string.IsNullOrWhiteSpace(item.Description)
            ? source.Description
            : item.Description.Trim();

        return new CvDraftExperience
        {
            Id = source.Id,
            Include = item.Include,
            RelevanceScore = Math.Clamp(item.RelevanceScore, 0, 100),
            Company = string.IsNullOrWhiteSpace(item.Company) ? source.Company : item.Company.Trim(),
            Title = title,
            Location = string.IsNullOrWhiteSpace(item.Location) ? source.Location : item.Location.Trim(),
            StartDate = source.StartDate,
            EndDate = source.EndDate,
            IsCurrent = source.IsCurrent,
            EngagementType = string.IsNullOrWhiteSpace(source.EngagementType) ? "Permanent" : source.EngagementType,
            Description = description
        };
    }

    private static bool IsPlaceholderSummary(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Contains("REPLACE", StringComparison.OrdinalIgnoreCase);

    private static bool IsPlaceholderSkill(string value) =>
        value.Contains("REPLACE", StringComparison.OrdinalIgnoreCase);

    private static bool IsPlaceholderTitle(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Contains("REPLACE", StringComparison.OrdinalIgnoreCase);
}
