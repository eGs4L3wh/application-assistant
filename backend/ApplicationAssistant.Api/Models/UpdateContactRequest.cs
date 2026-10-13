namespace ApplicationAssistant.Api.Models;

public record UpdateContactRequest(string? Email, string? Phone);

public record UpdateLanguagesRequest(List<string>? Languages);
