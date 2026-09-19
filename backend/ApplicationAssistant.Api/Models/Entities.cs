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
    public List<WorkExperience> Experience { get; set; } = [];
    public List<EducationRecord> Education { get; set; } = [];
}

public class WorkExperience
{
    public Guid Id { get; set; }
    public Guid? SourceCvId { get; set; }
    public string Company { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Location { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsCurrent { get; set; }
    public string? Description { get; set; }
}

public class EducationRecord
{
    public Guid Id { get; set; }
    public Guid? SourceCvId { get; set; }
    public string Institution { get; set; } = string.Empty;
    public string? Degree { get; set; }
    public string? FieldOfStudy { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? Description { get; set; }
}

public class CvDocument
{
    [BsonId]
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;

    /// <summary>Raw CV bytes stored in MongoDB.</summary>
    public byte[] Content { get; set; } = [];

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
