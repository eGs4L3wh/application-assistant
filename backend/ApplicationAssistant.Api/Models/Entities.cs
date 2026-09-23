using MongoDB.Bson.Serialization.Attributes;

namespace ApplicationAssistant.Api.Models;

public class AppUser
{
    [BsonId]
    public Guid Id { get; set; }

    public string GoogleId { get; set; } = string.Empty;
    /// <summary>Google SSO login email (identity). Not used as the editable CV contact email.</summary>
    public string Email { get; set; } = string.Empty;
    /// <summary>Email shown on generated CVs. Falls back to <see cref="Email"/> when empty.</summary>
    public string ContactEmail { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public List<WorkExperience> Experience { get; set; } = [];
    public List<EducationRecord> Education { get; set; } = [];

    /// <summary>Email to put on generated CVs.</summary>
    public string GetCvEmail() =>
        string.IsNullOrWhiteSpace(ContactEmail) ? Email : ContactEmail.Trim();
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
    /// <summary>e.g. Permanent, Contract. See <see cref="EngagementType"/>.</summary>
    public string EngagementType { get; set; } = Models.EngagementType.Permanent;
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
    /// <summary>PDF bytes of the last generated tailored CV for this application.</summary>
    public byte[]? GeneratedCvContent { get; set; }
    public string Company { get; set; } = string.Empty;
    public string RoleTitle { get; set; } = string.Empty;
    public string Status { get; set; } = "Draft";
    public string? Notes { get; set; }
    public string? JobAd { get; set; }
    public string? Summary { get; set; }
    public List<string> Skills { get; set; } = [];
    public List<ApplicationDraftExperience> DraftExperiences { get; set; } = [];
    public List<ApplicationDraftEducation> DraftEducation { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class ApplicationDraftExperience
{
    public Guid Id { get; set; }
    public bool Include { get; set; } = true;
    public double RelevanceScore { get; set; }
    public string Company { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Location { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsCurrent { get; set; }
    public string EngagementType { get; set; } = Models.EngagementType.Permanent;
    public string? Description { get; set; }
}

public class ApplicationDraftEducation
{
    public Guid Id { get; set; }
    public bool Include { get; set; } = true;
    public string Institution { get; set; } = string.Empty;
    public string? Degree { get; set; }
    public string? FieldOfStudy { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? Description { get; set; }
}
