namespace ApplicationAssistant.Api.Models;

public record UpdateApplicationRequest(
    string? Company,
    string? RoleTitle,
    string? Status,
    string? Notes,
    Guid? CvId,
    string? Summary,
    List<string>? Skills,
    List<DraftExperienceUpdate>? Experiences,
    List<DraftEducationUpdate>? Education);
