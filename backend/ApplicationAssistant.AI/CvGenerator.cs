using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ApplicationAssistant.AI;

public sealed class CvGenerator(ILLMService llm) : ICvGenerator
{
    private const string SystemPrompt = """
        Tailor the candidate CV to the target role using ONLY the source experience and education. Never invent facts.

        Preserve every experience and education Id exactly. Keep real employer names. Amend titles only when the source truthfully supports the ad's wording.

        Drop generic filler, soft-skill padding, and job-description language. Focus on concrete achievements, ownership, scale, technical decisions, and measurable outcomes that match the must-have and nice-to-have requirements.

        For each role, write Description as bullet points: one achievement or significant aspect per bullet, each on its own line starting with "- ".
        Example:
        - Built and managed a team of 5 engineers
        - Implemented payment backend integration using Kafka and event sourcing
        Put the strongest requirement matches first. Use present tense for current roles, past tense for past roles. No first person. No superlatives.

        skills[] should be short tags evidenced in the source, preferring requirement matches. Score each role 0-100 for relevance to the requirements; set include=true for every role (a later step hides scores below 70).

        Return JSON matching the sample shape exactly.
        """;

    private const string MetadataSystemPrompt = """
        Extract hiring metadata from the job advertisement.
        Prefer the official employer name and the role title as written in the ad.
        Infer mustHave and niceToHave from the whole ad, even without those headings.
        mustHave: required, essential, minimum, or expected capabilities.
        niceToHave: preferred, desirable, a plus, ideally, or useful but not required.
        Keep each item short and specific. If either company or role cannot be determined, leave it empty.
        Return JSON matching the sample shape exactly.
        """;

    public async Task<JobAdMetadata> ExtractJobMetadataAsync(string jobAd, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        var sample = new JobAdMetadata
        {
            Company = "Acme Corp",
            RoleTitle = "Software Engineer",
            MustHave = ["5+ years of C#", "Experience designing APIs"],
            NiceToHave = ["Kubernetes"]
        };
        var history = new[]
        {
            new InputItem
            {
                FromUser = true,
                Text = jobAd.Trim()
            }
        };

        var metadata = await llm.GetResponseAsync(sample, MetadataSystemPrompt, history, temperature: 0.1m, maxTokens: 2048, thinkingBudget: 0);
        metadata.Company = metadata.Company?.Trim() ?? string.Empty;
        metadata.RoleTitle = metadata.RoleTitle?.Trim() ?? string.Empty;
        metadata.MustHave = NormalizeRequirementList(metadata.MustHave);
        metadata.NiceToHave = NormalizeRequirementList(metadata.NiceToHave)
            .Where(item => !metadata.MustHave.Contains(item, StringComparer.OrdinalIgnoreCase))
            .ToList();
        return metadata;
    }

    private const string CoverageSystemPrompt = """
        Judge how well each must-have is evidenced by the included experiences and education only.
        Use covered when a record clearly shows the requirement, partial when the evidence is related but incomplete, and missing when nothing supports it.
        evidence is one short sentence naming the supporting role or education, or why it is missing. Never invent facts.
        Return one item per requirement, in the same order, copying each requirement exactly.
        coverage must be covered, partial, or missing.
        Return JSON matching the sample shape exactly.
        """;

    public async Task<RequirementCoverageResult> AssessMustHaveCoverageAsync(
        RequirementCoverageRequest request,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        var requirements = NormalizeRequirementList(request.MustHave);
        if (requirements.Count == 0)
        {
            return new RequirementCoverageResult();
        }

        var sample = new RequirementCoverageResult
        {
            Items =
            [
                new RequirementCoverageItem
                {
                    Requirement = "5+ years of C#",
                    Coverage = "partial",
                    Evidence = "Senior Engineer at Acme used C#, but the records do not show five years."
                }
            ]
        };
        var history = new[]
        {
            new InputItem
            {
                FromUser = true,
                Text = BuildCoveragePayload(requirements, request)
            }
        };

        var result = await llm.GetResponseAsync(sample, CoverageSystemPrompt, history, temperature: 0.1m, maxTokens: 4096, thinkingBudget: 0);
        result.Items = AlignCoverage(requirements, result.Items);
        return result;
    }

    private static string BuildCoveragePayload(
        IReadOnlyList<string> requirements,
        RequirementCoverageRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("MUST-HAVE REQUIREMENTS:");
        sb.AppendLine(JsonSerializer.Serialize(requirements));
        sb.AppendLine();
        sb.AppendLine("INCLUDED EXPERIENCES:");
        sb.AppendLine(JsonSerializer.Serialize(request.Experiences.Select(e => new
        {
            e.Title,
            e.Company,
            e.Description,
            e.Skills
        })));
        sb.AppendLine();
        sb.AppendLine("INCLUDED EDUCATION:");
        sb.AppendLine(JsonSerializer.Serialize(request.Education.Select(e => new
        {
            e.Institution,
            e.Degree,
            e.FieldOfStudy,
            e.Description
        })));
        return sb.ToString();
    }

    private static List<RequirementCoverageItem> AlignCoverage(
        IReadOnlyList<string> requirements,
        IEnumerable<RequirementCoverageItem>? items)
    {
        var returned = (items ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.Requirement))
            .ToList();
        var unused = new List<RequirementCoverageItem>(returned);

        return requirements.Select(requirement =>
        {
            var match = unused.FirstOrDefault(item =>
                string.Equals(item.Requirement.Trim(), requirement, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                unused.Remove(match);
            }
            else if (unused.Count > 0)
            {
                match = unused[0];
                unused.RemoveAt(0);
            }

            return new RequirementCoverageItem
            {
                Requirement = requirement,
                Coverage = NormalizeCoverage(match?.Coverage),
                Evidence = string.IsNullOrWhiteSpace(match?.Evidence)
                    ? "No supporting experience or education was identified."
                    : match.Evidence.Trim()
            };
        }).ToList();
    }

    private static string NormalizeCoverage(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is "covered" or "partial" or "missing" ? normalized : "missing";
    }

    public async Task<CvDraft> GenerateAsync(CvGenerationRequest request, CancellationToken cancellationToken = default)
    {
        var metadata = await ExtractJobMetadataAsync(request.JobAd, cancellationToken);
        var company = string.IsNullOrWhiteSpace(request.TargetCompany) ? metadata.Company : request.TargetCompany;
        var roleTitle = string.IsNullOrWhiteSpace(request.TargetRoleTitle) ? metadata.RoleTitle : request.TargetRoleTitle;
        var mustHave = metadata.MustHave;
        var niceToHave = metadata.NiceToHave;

        var sample = BuildSample(request);
        var history = new[]
        {
            new InputItem
            {
                FromUser = true,
                Text = BuildUserPayload(request, company, roleTitle, mustHave, niceToHave)
            }
        };

        var draft = await llm.GetResponseAsync(sample, SystemPrompt, history, temperature: 0.35m, maxTokens: 16384);
        draft = NormalizeDraft(draft, request);
        draft.TargetCompany = string.IsNullOrWhiteSpace(company) ? null : company.Trim();
        draft.TargetRoleTitle = string.IsNullOrWhiteSpace(roleTitle) ? null : roleTitle.Trim();
        draft.MustHave = mustHave;
        draft.NiceToHave = niceToHave;
        draft.Summary = await WriteSummaryAsync(request, draft, company, roleTitle, mustHave, niceToHave, cancellationToken);
        return draft;
    }

    private const string SummarySystemPrompt = """
        Write a short professional summary from the included roles, skills, and requirements only. Never invent facts.
        Prefer concrete strengths that match must-have and nice-to-have requirements. No generic filler or superlatives.
        Return JSON matching the sample shape exactly.
        """;

    private async Task<string> WriteSummaryAsync(
        CvGenerationRequest request,
        CvDraft draft,
        string? targetCompany,
        string? targetRoleTitle,
        IReadOnlyList<string> mustHave,
        IReadOnlyList<string> niceToHave,
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
                Text = BuildSummaryPayload(request, draft, targetCompany, targetRoleTitle, mustHave, niceToHave)
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
        string? targetRoleTitle,
        IReadOnlyList<string> mustHave,
        IReadOnlyList<string> niceToHave)
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
        sb.AppendLine("MUST-HAVE REQUIREMENTS:");
        sb.AppendLine(JsonSerializer.Serialize(mustHave));
        sb.AppendLine();
        sb.AppendLine("NICE-TO-HAVE REQUIREMENTS:");
        sb.AppendLine(JsonSerializer.Serialize(niceToHave));
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
        Edit a single CV role description from the user's instruction.
        Stay truthful to the source — never invent employers, tools, achievements, or seniority.
        Keep Description as bullet points: one achievement or significant aspect per line, each starting with "- ".
        Drop generic filler. Prefer the job ad's vocabulary when it truthfully fits.
        Return JSON matching the sample shape exactly.
        """;

    public async Task<ExperienceRefineResult> RefineExperienceAsync(
        ExperienceRefineRequest request,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        var sample = new ExperienceRefineResult
        {
            Description = request.CurrentDescription
                ?? """
                    - Built and managed a team of 5 engineers
                    - Implemented payment backend integration using Kafka and event sourcing
                    """
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
            : NormalizeBulletDescription(result.Description) ?? result.Description.Trim();
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
                Description = """
                    - Built and managed a team of 5 engineers
                    - Implemented payment backend integration using Kafka and event sourcing
                    """,
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

    private static string BuildUserPayload(
        CvGenerationRequest request,
        string? targetCompany,
        string? targetRoleTitle,
        IReadOnlyList<string> mustHave,
        IReadOnlyList<string> niceToHave)
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
        sb.AppendLine("MUST-HAVE REQUIREMENTS:");
        sb.AppendLine(JsonSerializer.Serialize(mustHave));
        sb.AppendLine();
        sb.AppendLine("NICE-TO-HAVE REQUIREMENTS:");
        sb.AppendLine(JsonSerializer.Serialize(niceToHave));
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
                exp.Description = NormalizeBulletDescription(source.Description)
                    ?? "- Contributed to team delivery and project outcomes.";
            }
        }

        foreach (var exp in experiences)
        {
            exp.Description = NormalizeBulletDescription(exp.Description);
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
            Description = NormalizeBulletDescription(description),
            Skills = PreferSkills(item.Skills, source.Skills)
        };
    }

    private static List<string> NormalizeRequirementList(IEnumerable<string>? items) =>
        (items ?? [])
            .Select(item => item?.Trim() ?? string.Empty)
            .Where(item => item.Length > 0)
            .Select(item => item.TrimStart('-', '•', '*', ' '))
            .Select(item => item.Trim())
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

    private static string? NormalizeBulletDescription(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;

        var lines = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.TrimStart('-', '•', '*', ' ').Trim())
            .Where(line => line.Length > 0)
            .Select(line => $"- {line}")
            .ToList();

        return lines.Count == 0 ? value.Trim() : string.Join('\n', lines);
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
