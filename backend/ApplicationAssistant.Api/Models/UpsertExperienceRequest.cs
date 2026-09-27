namespace ApplicationAssistant.Api.Models;

public record UpsertExperienceRequest(
    string Company,
    string Title,
    string? Location,
    string? StartDate,
    string? EndDate,
    bool IsCurrent,
    string? EngagementType,
    string? Description,
    List<string>? Skills);
