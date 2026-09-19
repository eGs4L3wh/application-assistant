namespace ApplicationAssistant.AI;

public interface ICvParser
{
    Task<CvParseResult> ParseAsync(
        byte[] content,
        string contentType,
        string fileName,
        CancellationToken cancellationToken = default);
}

public sealed class CvParseResult
{
    public List<ParsedWorkExperience> Experiences { get; set; } = [];
    public List<ParsedEducation> Education { get; set; } = [];
}

public sealed class ParsedWorkExperience
{
    public string Company { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Location { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public bool IsCurrent { get; set; }
    public string? Description { get; set; }
}

public sealed class ParsedEducation
{
    public string Institution { get; set; } = string.Empty;
    public string? Degree { get; set; }
    public string? FieldOfStudy { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public string? Description { get; set; }
}
