namespace ApplicationAssistant.AI;

public interface ICvGenerator
{
    Task<CvDraft> GenerateAsync(CvGenerationRequest request, CancellationToken cancellationToken = default);
}

public sealed class CvGenerationRequest
{
    public required string JobAd { get; init; }
    public string? TargetCompany { get; init; }
    public string? TargetRoleTitle { get; init; }
    public required string FullName { get; init; }
    public required string Email { get; init; }
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
    public List<string> Bullets { get; set; } = [];
}

public sealed class CvDraftEducation
{
    public Guid Id { get; set; }
    public bool Include { get; set; } = true;
    public string Institution { get; set; } = string.Empty;
    public string? Degree { get; set; }
    public string? FieldOfStudy { get; set; }
    public string? Description { get; set; }
}
