using MongoDB.Bson.Serialization.Attributes;

namespace ApplicationAssistant.Api.Models;

public class AppUser
{
    [BsonId]
    public Guid Id { get; set; }

    public string GoogleId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class CvDocument
{
    [BsonId]
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
}

public class JobApplication
{
    [BsonId]
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public Guid? CvId { get; set; }
    public string? CvFileName { get; set; }
    public string Company { get; set; } = string.Empty;
    public string RoleTitle { get; set; } = string.Empty;
    public string Status { get; set; } = "Draft";
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
