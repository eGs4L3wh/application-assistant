namespace ApplicationAssistant.Api.Models;

public record DraftEducationUpdate(
    Guid Id,
    bool Include,
    string Institution,
    string? Degree,
    string? FieldOfStudy,
    DateTime? StartDate,
    DateTime? EndDate,
    string? Description);
