using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ApplicationAssistant.AI;

public sealed class CvGenerator(ILLMService llm) : ICvGenerator
{
    private const string SystemPrompt = """
        You write tailored CVs optimized to score highly against the given job advertisement in ATS and AI screening tools.
        Use ONLY the candidate's provided experience and education. Never invent employers, degrees, dates, tools, or achievements that are not grounded in the source text.
        The target company and role are already identified in the user message. Do not extract or return them.

        Identity (critical):
        - Preserve each experience and education Id EXACTLY as given in the source JSON. Do not invent or omit Ids.

        Alignment (critical):
        - Extract the job ad's must-have skills, tools, domain terms, responsibilities, and seniority language.
        - Mirror those exact phrases and keywords in the skills list, titles, and descriptions wherever the candidate's experience truthfully supports them (same meaning; prefer the ad's wording over synonyms).
        - Lead with the strongest matches: put the most ad-relevant achievements first in each role description.
        - Build skills[] primarily from terms that appear in the job ad and are evidenced in the candidate's experience; list the highest-priority matches first. Prefer a fuller skills list (typically 12-20 items) over a short one when the source supports it.
        - Score every role with relevanceScore from 0 to 100 for how well it matches the job ad. 70 or above means the role belongs on the CV.
        - Set include=true for every role, including ones that overlap in time. A later step hides only roles scored below 70. Do not drop overlapping roles.

        Per-role skills (critical):
        - For each experience, return skills[] as short tags for that role (languages, frameworks, cloud providers, databases, tools, methods).
        - Ground skills in the source Skills list and Description — never invent tools the candidate did not use.
        - Prefer tags that also match the job ad when truthful; keep other truthful tags that show breadth.
        - Typically 4-12 tags per included role; empty list is fine for include=false or when nothing is evidenced.

        Titles / roles:
        - Keep Company as the real employer name from the source (do not replace with the target company).
        - Amend Title for each included role so it emphasizes the specialty most relevant to the job ad, when that framing is truthful to the source. Prefer the ad's role vocabulary over a generic source title when both fit.
        - Do not invent promotions, seniority the candidate did not hold, or fake job titles.

        Descriptions:
        - For each role, tailor descriptions to the job ad, grounded in the source Description. Expand and rephrase — never paste the source Description verbatim unchanged, and never strip the source down to a thin summary.
        - Target length per included role: typically 4-8 sentences or a short multi-paragraph block (use line breaks between ideas). Cover scope, responsibilities, tech/stack, methods, stakeholders, and concrete outcomes wherever the source supports them.
        - Prioritize the most ad-relevant achievements first, then retain other truthful detail from the source so the role still reads full and credible.
        - Prefer concrete outcomes (scope, impact, tech/stack, methods) phrased with the ad's vocabulary.
        - Tense: for roles with IsCurrent=true or EndDate=Present, write in present tense. For past roles, use past tense.

        Voice (critical):
        - Plain factual statements only. No superlatives, no self-praise, no LinkedIn openers.
        - Banned words and phrases, including close variants: highly, accomplished, extensive, proven, expert, adept, significant, seasoned, passionate, results-driven, track record, world-class, best-in-class, exceptional, outstanding, deeply, robust.
        - This applies to every role description.

        Output length (critical):
        - The finished CV should land around two A4 pages when rendered — not a sparse one-page CV. Err on the side of richer descriptions and more included roles when the source supports it.
        - Still return valid complete JSON; do not truncate mid-field.

        Return JSON matching the sample shape exactly (same fields and Ids), with rewritten skills, titles, and descriptions.
        """;

    private const string MetadataSystemPrompt = """
        Extract the hiring company and the posted job title from the job advertisement.
        Prefer the official employer name and the role title as written in the ad.
        If either cannot be determined confidently, leave it as an empty string.
        Return JSON matching the sample shape exactly.
        """;

    public async Task<JobAdMetadata> ExtractJobMetadataAsync(string jobAd, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        var sample = new JobAdMetadata
        {
            Company = "Acme Corp",
            RoleTitle = "Software Engineer"
        };
        var history = new[]
        {
            new InputItem
            {
                FromUser = true,
                Text = jobAd.Trim()
            }
        };

        var metadata = await llm.GetResponseAsync(sample, MetadataSystemPrompt, history, temperature: 0.1m, maxTokens: 1024, thinkingBudget: 0);
        metadata.Company = metadata.Company?.Trim() ?? string.Empty;
        metadata.RoleTitle = metadata.RoleTitle?.Trim() ?? string.Empty;
        return metadata;
    }

    public async Task<CvDraft> GenerateAsync(CvGenerationRequest request, CancellationToken cancellationToken = default)
    {
        var company = request.TargetCompany;
        var roleTitle = request.TargetRoleTitle;
        if (string.IsNullOrWhiteSpace(company) || string.IsNullOrWhiteSpace(roleTitle))
        {
            var metadata = await ExtractJobMetadataAsync(request.JobAd, cancellationToken);
            if (string.IsNullOrWhiteSpace(company)) company = metadata.Company;
            if (string.IsNullOrWhiteSpace(roleTitle)) roleTitle = metadata.RoleTitle;
        }

        var sample = BuildSample(request);
        var history = new[]
        {
            new InputItem
            {
                FromUser = true,
                Text = BuildUserPayload(request, company, roleTitle)
            }
        };

        var draft = await llm.GetResponseAsync(sample, SystemPrompt, history, temperature: 0.35m, maxTokens: 16384);
        draft = NormalizeDraft(draft, request);
        draft.TargetCompany = string.IsNullOrWhiteSpace(company) ? null : company.Trim();
        draft.TargetRoleTitle = string.IsNullOrWhiteSpace(roleTitle) ? null : roleTitle.Trim();
        draft.Summary = await WriteSummaryAsync(request, draft, company, roleTitle, cancellationToken);
        return draft;
    }

    private const string SummarySystemPrompt = """
        You write the professional summary for a CV that has already been drafted.
        Use ONLY the included roles, their descriptions, the skills list, and the job ad. Do not invent employers, tools, or achievements.
        Write several sentences that connect the candidate to the target role using the job ad's wording where the drafted roles support it.
        Focus on the roles that are relevant to the target role.

        Voice (critical):
        - Plain factual statements only. No superlatives, no self-praise, no LinkedIn openers.
        - Do not copy the sample sentence. Write from the included roles below.

        Return JSON matching the sample shape exactly.
        """;

    private async Task<string> WriteSummaryAsync(
        CvGenerationRequest request,
        CvDraft draft,
        string? targetCompany,
        string? targetRoleTitle,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        var sample = new CvSummaryDraft
        {
            Summary = "Lead backend engineer who designs distributed systems and APIs in C# and TypeScript."
        };
        var history = new[]
        {
            new InputItem
            {
                FromUser = true,
                Text = BuildSummaryPayload(request, draft, targetCompany, targetRoleTitle)
            }
        };

        var result = await llm.GetResponseAsync(sample, SummarySystemPrompt, history, temperature: 0.2m, maxTokens: 2048, thinkingBudget: 0);
        var summary = result.Summary?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(summary) || IsPlaceholderSummary(summary))
        {
            var role = string.IsNullOrWhiteSpace(targetRoleTitle) ? "Engineer" : targetRoleTitle.Trim();
            return $"{role} applying the experience in this CV to the target role.";
        }

        return summary;
    }

    private static string BuildSummaryPayload(
        CvGenerationRequest request,
        CvDraft draft,
        string? targetCompany,
        string? targetRoleTitle)
    {
        var included = draft.Experiences
            .Where(e => e.Include)
            .Select(e => new
            {
                e.Title,
                e.Company,
                e.IsCurrent,
                Description = Truncate(e.Description, 500),
                Skills = e.Skills
            });

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(targetCompany))
        {
            sb.AppendLine($"Target company: {targetCompany.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(targetRoleTitle))
        {
            sb.AppendLine($"Target role: {targetRoleTitle.Trim()}");
        }

        sb.AppendLine();
        sb.AppendLine("JOB AD:");
        sb.AppendLine(request.JobAd.Trim());
        sb.AppendLine();
        sb.AppendLine("CV SKILLS:");
        sb.AppendLine(JsonSerializer.Serialize(draft.Skills));
        sb.AppendLine();
        sb.AppendLine("INCLUDED ROLES (already tailored):");
        sb.AppendLine(JsonSerializer.Serialize(included));
        return sb.ToString();
    }

    private sealed class CvSummaryDraft
    {
        public string Summary { get; set; } = string.Empty;
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
        Summary = string.Empty,
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
                Description = e.Description,
                Skills = e.Skills is { Count: > 0 } ? e.Skills.ToList() : ["C#", "Azure"]
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

    private static string BuildUserPayload(CvGenerationRequest request, string? targetCompany, string? targetRoleTitle)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Candidate: {request.FullName} <{request.Email}>");
        if (!string.IsNullOrWhiteSpace(request.Phone))
        {
            sb.AppendLine($"Phone: {request.Phone}");
        }
        if (!string.IsNullOrWhiteSpace(targetCompany))
        {
            sb.AppendLine($"Target company: {targetCompany.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(targetRoleTitle))
        {
            sb.AppendLine($"Target role: {targetRoleTitle.Trim()}");
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
            Description = Truncate(e.Description, 700),
            Skills = e.Skills
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
                Description = source.Description,
                Skills = NormalizeSkills(source.Skills)
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

        ApplyRelevanceCutoff(experiences);
        draft.Experiences = experiences;

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
            Description = description,
            Skills = PreferSkills(item.Skills, source.Skills)
        };
    }

    private static List<string> PreferSkills(IEnumerable<string>? preferred, IEnumerable<string>? fallback)
    {
        var fromPreferred = NormalizeSkills(preferred);
        return fromPreferred.Count > 0 ? fromPreferred : NormalizeSkills(fallback);
    }

    private static List<string> NormalizeSkills(IEnumerable<string>? skills) =>
        (skills ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

    private const double MinIncludedRelevance = 70;

    /// <summary>
    /// Hides roles the model scored below 70. Overlaps and higher scores stay included for the user to edit.
    /// </summary>
    private static void ApplyRelevanceCutoff(List<CvDraftExperience> experiences)
    {
        foreach (var experience in experiences)
        {
            experience.Include = experience.RelevanceScore >= MinIncludedRelevance;
        }
    }

    private static bool IsPlaceholderSummary(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Contains("REPLACE", StringComparison.OrdinalIgnoreCase);

    private static bool IsPlaceholderSkill(string value) =>
        value.Contains("REPLACE", StringComparison.OrdinalIgnoreCase);

    private static bool IsPlaceholderTitle(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Contains("REPLACE", StringComparison.OrdinalIgnoreCase);
}
