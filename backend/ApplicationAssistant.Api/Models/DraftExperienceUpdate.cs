namespace ApplicationAssistant.Api.Models;

public record DraftExperienceUpdate(
    Guid Id,
    bool Include,
    double RelevanceScore,
    string Company,
    string Title,
    string? Location,
    DateTime? StartDate,
    DateTime? EndDate,
    bool IsCurrent,
    string? EngagementType,
    string? Description,
    List<string>? Skills);
