namespace ApplicationAssistant.Api.Models;

public record UpsertEducationRequest(
    string Institution,
    string? Degree,
    string? FieldOfStudy,
    string? StartDate,
    string? EndDate,
    string? Description);
