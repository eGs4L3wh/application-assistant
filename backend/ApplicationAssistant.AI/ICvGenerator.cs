namespace ApplicationAssistant.AI;

public interface ICvGenerator
{
    Task<CvDraft> GenerateAsync(CvGenerationRequest request, CancellationToken cancellationToken = default);
    Task<ExperienceRefineResult> RefineExperienceAsync(
        ExperienceRefineRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class ExperienceRefineRequest
{
    public required string JobAd { get; init; }
    public required string TargetCompany { get; init; }
    public required string TargetRoleTitle { get; init; }
    public required string UserPrompt { get; init; }
    public required string Company { get; init; }
    public required string Title { get; init; }
    public string? Location { get; init; }
    public string? CurrentDescription { get; init; }
    public string? SourceDescription { get; init; }
}

public sealed class ExperienceRefineResult
{
    public string Description { get; set; } = string.Empty;
}

public sealed class CvGenerationRequest
{
    public required string JobAd { get; init; }
    public string? TargetCompany { get; init; }
    public string? TargetRoleTitle { get; init; }
    public required string FullName { get; init; }
    public required string Email { get; init; }
    public string? Phone { get; init; }
    public required IReadOnlyList<CvSourceExperience> Experiences { get; init; }
    public required IReadOnlyList<CvSourceEducation> Education { get; init; }
}

public sealed class CvSourceExperience
{
    public Guid Id { get; init; }
    public string Company { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Location { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public bool IsCurrent { get; init; }
    public string EngagementType { get; init; } = "Permanent";
    public string? Description { get; init; }
}

public sealed class CvSourceEducation
{
    public Guid Id { get; init; }
    public string Institution { get; init; } = string.Empty;
    public string? Degree { get; init; }
    public string? FieldOfStudy { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public string? Description { get; init; }
}

public sealed class CvDraft
{
    public string? TargetCompany { get; set; }
    public string? TargetRoleTitle { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<string> Skills { get; set; } = [];
    public List<CvDraftExperience> Experiences { get; set; } = [];
    public List<CvDraftEducation> Education { get; set; } = [];
}

public sealed class CvDraftExperience
{
    public Guid Id { get; set; }
    public bool Include { get; set; } = true;
    public double RelevanceScore { get; set; }
    public string Company { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Location { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsCurrent { get; set; }
    public string EngagementType { get; set; } = "Permanent";
    public string? Description { get; set; }
}

public sealed class CvDraftEducation
{
    public Guid Id { get; set; }
    public bool Include { get; set; } = true;
    public string Institution { get; set; } = string.Empty;
    public string? Degree { get; set; }
    public string? FieldOfStudy { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? Description { get; set; }
}
