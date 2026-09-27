using ApplicationAssistant.AI;
using ApplicationAssistant.Api.Data;
using ApplicationAssistant.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace ApplicationAssistant.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/applications")]
public class ApplicationsController(MongoDbContext db, ICvGenerator cvGenerator, CvPdfRenderer pdfRenderer) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<object>> Create([FromBody] CreateApplicationRequest request, CancellationToken ct)
    {
        var userId = User.GetAppUserId();
        if (userId is null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.JobAd))
        {
            return BadRequest(new { error = "Job ad is required." });
        }

        var user = await db.Users.Find(u => u.Id == userId).FirstOrDefaultAsync(ct);
        if (user is null) return Unauthorized();

        if (user.Experience.Count == 0 && user.Education.Count == 0)
        {
            return BadRequest(new { error = "Add experience or education to your profile before generating a CV." });
        }

        var fullName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email : user.DisplayName;
        var draft = await cvGenerator.GenerateAsync(new CvGenerationRequest
        {
            JobAd = request.JobAd.Trim(),
            FullName = fullName,
            Email = user.GetCvEmail(),
            Phone = string.IsNullOrWhiteSpace(user.Phone) ? null : user.Phone.Trim(),
            Experiences = user.Experience.Select(ToSourceExperience).ToList(),
            Education = user.Education.Select(ToSourceEducation).ToList()
        }, ct);

        var now = DateTimeOffset.UtcNow;
        var company = string.IsNullOrWhiteSpace(draft.TargetCompany) ? "Unknown company" : draft.TargetCompany.Trim();
        var roleTitle = string.IsNullOrWhiteSpace(draft.TargetRoleTitle) ? "Unknown role" : draft.TargetRoleTitle.Trim();

        var application = new JobApplication
        {
            Id = Guid.NewGuid(),
            UserId = userId.Value,
            Company = company,
            RoleTitle = roleTitle,
            JobAd = request.JobAd.Trim(),
            Summary = draft.Summary,
            Skills = draft.Skills,
            DraftExperiences = draft.Experiences.Select(ToStoredExperience).ToList(),
            DraftEducation = draft.Education.Select(ToStoredEducation).ToList(),
            Status = "Draft",
            CreatedAt = now,
            UpdatedAt = now
        };

        await db.Applications.InsertOneAsync(application, cancellationToken: ct);
        return CreatedAtAction(nameof(Get), new { id = application.Id }, ToDetailDto(application));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<object>> Get(Guid id, CancellationToken ct)
    {
        var userId = User.GetAppUserId();
        if (userId is null) return Unauthorized();

        var application = await db.Applications
            .Find(a => a.Id == id && a.UserId == userId)
            .FirstOrDefaultAsync(ct);

        if (application is null) return NotFound();

        var user = await db.Users.Find(u => u.Id == userId).FirstOrDefaultAsync(ct);
        if (user is not null)
        {
            BackfillDraftDates(application, user);
        }

        return Ok(ToDetailDto(application));
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<object>> Update(Guid id, [FromBody] UpdateApplicationRequest request, CancellationToken ct)
    {
        var userId = User.GetAppUserId();
        if (userId is null) return Unauthorized();

        var application = await db.Applications
            .Find(a => a.Id == id && a.UserId == userId)
            .FirstOrDefaultAsync(ct);

        if (application is null) return NotFound();

        if (request.Company is not null)
        {
            application.Company = string.IsNullOrWhiteSpace(request.Company)
                ? application.Company
                : request.Company.Trim();
        }

        if (request.RoleTitle is not null)
        {
            application.RoleTitle = string.IsNullOrWhiteSpace(request.RoleTitle)
                ? application.RoleTitle
                : request.RoleTitle.Trim();
        }

        if (request.Status is not null) application.Status = request.Status.Trim();
        if (request.Notes is not null) application.Notes = request.Notes.Trim();
        if (request.CvId is not null)
        {
            if (request.CvId == Guid.Empty)
            {
                application.CvId = null;
                application.CvFileName = null;
            }
            else
            {
                var cv = await db.Cvs
                    .Find(c => c.Id == request.CvId && c.UserId == userId)
                    .FirstOrDefaultAsync(ct);
                if (cv is null) return BadRequest(new { error = "CV not found." });
                application.CvId = request.CvId;
                application.CvFileName = cv.FileName;
            }
        }

        if (request.Summary is not null)
        {
            application.Summary = request.Summary.Trim();
        }

        if (request.Skills is not null)
        {
            application.Skills = request.Skills
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList();
        }

        if (request.Experiences is not null)
        {
            var byId = application.DraftExperiences.ToDictionary(e => e.Id);
            foreach (var update in request.Experiences)
            {
                if (!byId.TryGetValue(update.Id, out var existing))
                {
                    continue;
                }

                existing.Include = update.Include;
                existing.RelevanceScore = Math.Clamp(update.RelevanceScore, 0, 100);
                existing.Company = string.IsNullOrWhiteSpace(update.Company) ? existing.Company : update.Company.Trim();
                existing.Title = string.IsNullOrWhiteSpace(update.Title) ? existing.Title : update.Title.Trim();
                existing.Location = string.IsNullOrWhiteSpace(update.Location) ? null : update.Location.Trim();
                existing.StartDate = update.StartDate;
                existing.EndDate = update.IsCurrent ? null : update.EndDate;
                existing.IsCurrent = update.IsCurrent;
                existing.EngagementType = EngagementType.Normalize(update.EngagementType);
                existing.Description = string.IsNullOrWhiteSpace(update.Description) ? null : update.Description.Trim();
                if (update.Skills is not null)
                {
                    existing.Skills = NormalizeSkills(update.Skills);
                }
            }
        }

        if (request.Education is not null)
        {
            var byId = application.DraftEducation.ToDictionary(e => e.Id);
            foreach (var update in request.Education)
            {
                if (!byId.TryGetValue(update.Id, out var existing))
                {
                    continue;
                }

                existing.Include = update.Include;
                existing.Institution = string.IsNullOrWhiteSpace(update.Institution)
                    ? existing.Institution
                    : update.Institution.Trim();
                existing.Degree = string.IsNullOrWhiteSpace(update.Degree) ? null : update.Degree.Trim();
                existing.FieldOfStudy = string.IsNullOrWhiteSpace(update.FieldOfStudy) ? null : update.FieldOfStudy.Trim();
                existing.StartDate = update.StartDate;
                existing.EndDate = update.EndDate;
                existing.Description = string.IsNullOrWhiteSpace(update.Description) ? null : update.Description.Trim();
            }
        }

        application.UpdatedAt = DateTimeOffset.UtcNow;
        await db.Applications.ReplaceOneAsync(
            a => a.Id == application.Id,
            application,
            cancellationToken: ct);

        return Ok(ToDetailDto(application));
    }

    [HttpPost("{id:guid}/experiences/{experienceId:guid}")]
    public async Task<ActionResult<object>> RefineExperience(
        Guid id,
        Guid experienceId,
        [FromBody] RefineExperienceRequestBody request,
        CancellationToken ct)
    {
        var userId = User.GetAppUserId();
        if (userId is null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return BadRequest(new { error = "Prompt is required." });
        }

        var application = await db.Applications
            .Find(a => a.Id == id && a.UserId == userId)
            .FirstOrDefaultAsync(ct);

        if (application is null) return NotFound();

        var experience = application.DraftExperiences.FirstOrDefault(e => e.Id == experienceId);
        if (experience is null) return NotFound(new { error = "Experience not found on this application." });

        var user = await db.Users.Find(u => u.Id == userId).FirstOrDefaultAsync(ct);
        if (user is null) return Unauthorized();

        var source = user.Experience.FirstOrDefault(e => e.Id == experienceId);

        var refined = await cvGenerator.RefineExperienceAsync(new ExperienceRefineRequest
        {
            JobAd = application.JobAd ?? string.Empty,
            TargetCompany = application.Company,
            TargetRoleTitle = application.RoleTitle,
            UserPrompt = request.Prompt.Trim(),
            Company = experience.Company,
            Title = experience.Title,
            Location = experience.Location,
            CurrentDescription = experience.Description,
            SourceDescription = source?.Description
        }, ct);

        experience.Description = refined.Description;
        application.UpdatedAt = DateTimeOffset.UtcNow;
        await db.Applications.ReplaceOneAsync(
            a => a.Id == application.Id,
            application,
            cancellationToken: ct);

        return Ok(new
        {
            experience.Id,
            experience.Include,
            experience.RelevanceScore,
            experience.Company,
            experience.Title,
            experience.Location,
            experience.Description,
            Skills = experience.Skills ?? []
        });
    }

    [HttpPost("{id:guid}")]
    public async Task<IActionResult> Finalize(Guid id, CancellationToken ct)
    {
        var userId = User.GetAppUserId();
        if (userId is null) return Unauthorized();

        var application = await db.Applications
            .Find(a => a.Id == id && a.UserId == userId)
            .FirstOrDefaultAsync(ct);

        if (application is null) return NotFound();

        if (application.DraftExperiences.Count == 0 && application.DraftEducation.Count == 0)
        {
            return BadRequest(new { error = "Application has no draft to finalize." });
        }

        var user = await db.Users.Find(u => u.Id == userId).FirstOrDefaultAsync(ct);
        if (user is null) return Unauthorized();

        var draft = ToCvDraft(application);
        var experiencesForPdf = draft.Experiences
            .Where(e => e.Include)
            .OrderByDescending(e => e.StartDate ?? DateTime.MinValue)
            .ToList();
        var educationForPdf = draft.Education
            .Where(e => e.Include)
            .OrderByDescending(e => e.EndDate ?? e.StartDate ?? DateTime.MinValue)
            .ToList();

        var fullName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email : user.DisplayName;
        var pdfBytes = pdfRenderer.Render(
            fullName,
            user.GetCvEmail(),
            string.IsNullOrWhiteSpace(user.Phone) ? null : user.Phone.Trim(),
            draft,
            experiencesForPdf,
            educationForPdf);

        var fileName = $"cv_{DateTime.UtcNow:yyyyMMddHHmmss}.pdf";

        application.GeneratedCvContent = pdfBytes;
        application.CvFileName = fileName;
        application.Status = "Generated";
        application.UpdatedAt = DateTimeOffset.UtcNow;
        await db.Applications.ReplaceOneAsync(
            a => a.Id == application.Id,
            application,
            cancellationToken: ct);

        return File(pdfBytes, "application/pdf", fileName);
    }

    [HttpGet("{id:guid}/cv")]
    public async Task<IActionResult> DownloadCv(Guid id, CancellationToken ct)
    {
        var userId = User.GetAppUserId();
        if (userId is null) return Unauthorized();

        var application = await db.Applications
            .Find(a => a.Id == id && a.UserId == userId)
            .FirstOrDefaultAsync(ct);

        if (application is null) return NotFound();

        if (application.GeneratedCvContent is not { Length: > 0 })
        {
            return NotFound(new { error = "No generated CV stored for this application yet." });
        }

        var fileName = string.IsNullOrWhiteSpace(application.CvFileName)
            ? $"cv_{DateTime.UtcNow:yyyyMMddHHmmss}.pdf"
            : application.CvFileName;
        return File(application.GeneratedCvContent, "application/pdf", fileName);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var userId = User.GetAppUserId();
        if (userId is null) return Unauthorized();

        var result = await db.Applications.DeleteOneAsync(
            a => a.Id == id && a.UserId == userId,
            cancellationToken: ct);

        if (result.DeletedCount == 0) return NotFound();
        return NoContent();
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> List(CancellationToken ct)
    {
        var userId = User.GetAppUserId();
        if (userId is null) return Unauthorized();

        var items = await db.Applications
            .Find(a => a.UserId == userId)
            .SortByDescending(a => a.UpdatedAt)
            .ToListAsync(ct);

        return Ok(items.Select(a => new
        {
            a.Id,
            a.Company,
            a.RoleTitle,
            a.Status,
            a.Notes,
            a.CvId,
            a.CvFileName,
            HasGeneratedCv = a.GeneratedCvContent is { Length: > 0 },
            a.CreatedAt,
            a.UpdatedAt
        }));
    }

    private static void BackfillDraftDates(JobApplication application, AppUser user)
    {
        var experienceById = user.Experience.ToDictionary(e => e.Id);
        foreach (var exp in application.DraftExperiences)
        {
            if (exp.StartDate is not null || exp.EndDate is not null || exp.IsCurrent)
            {
                continue;
            }

            if (!experienceById.TryGetValue(exp.Id, out var source))
            {
                continue;
            }

            exp.StartDate = source.StartDate;
            exp.EndDate = source.EndDate;
            exp.IsCurrent = source.IsCurrent;
            if (string.IsNullOrWhiteSpace(exp.EngagementType))
            {
                exp.EngagementType = EngagementType.Normalize(source.EngagementType);
            }

            if (exp.Skills is not { Count: > 0 } && source.Skills is { Count: > 0 })
            {
                exp.Skills = source.Skills.ToList();
            }
        }

        var educationById = user.Education.ToDictionary(e => e.Id);
        foreach (var edu in application.DraftEducation)
        {
            if (edu.StartDate is not null || edu.EndDate is not null)
            {
                continue;
            }

            if (!educationById.TryGetValue(edu.Id, out var source))
            {
                continue;
            }

            edu.StartDate = source.StartDate;
            edu.EndDate = source.EndDate;
        }
    }

    private static CvSourceExperience ToSourceExperience(WorkExperience e) => new()
    {
        Id = e.Id,
        Company = e.Company,
        Title = e.Title,
        Location = e.Location,
        StartDate = e.StartDate,
        EndDate = e.EndDate,
        IsCurrent = e.IsCurrent,
        EngagementType = EngagementType.Normalize(e.EngagementType),
        Description = e.Description,
        Skills = e.Skills ?? []
    };

    private static CvSourceEducation ToSourceEducation(EducationRecord e) => new()
    {
        Id = e.Id,
        Institution = e.Institution,
        Degree = e.Degree,
        FieldOfStudy = e.FieldOfStudy,
        StartDate = e.StartDate,
        EndDate = e.EndDate,
        Description = e.Description
    };

    private static ApplicationDraftExperience ToStoredExperience(CvDraftExperience e) => new()
    {
        Id = e.Id,
        Include = e.Include,
        RelevanceScore = e.RelevanceScore,
        Company = e.Company,
        Title = e.Title,
        Location = e.Location,
        StartDate = e.StartDate,
        EndDate = e.EndDate,
        IsCurrent = e.IsCurrent,
        EngagementType = EngagementType.Normalize(e.EngagementType),
        Description = e.Description,
        Skills = NormalizeSkills(e.Skills)
    };

    private static ApplicationDraftEducation ToStoredEducation(CvDraftEducation e) => new()
    {
        Id = e.Id,
        Include = e.Include,
        Institution = e.Institution,
        Degree = e.Degree,
        FieldOfStudy = e.FieldOfStudy,
        StartDate = e.StartDate,
        EndDate = e.EndDate,
        Description = e.Description
    };

    private static CvDraft ToCvDraft(JobApplication application) => new()
    {
        TargetCompany = application.Company,
        TargetRoleTitle = application.RoleTitle,
        Summary = application.Summary ?? string.Empty,
        Skills = application.Skills,
        Experiences = application.DraftExperiences.Select(e => new CvDraftExperience
        {
            Id = e.Id,
            Include = e.Include,
            RelevanceScore = e.RelevanceScore,
            Company = e.Company,
            Title = e.Title,
            Location = e.Location,
            StartDate = e.StartDate,
            EndDate = e.EndDate,
            IsCurrent = e.IsCurrent,
            EngagementType = EngagementType.Normalize(e.EngagementType),
            Description = e.Description,
            Skills = e.Skills ?? []
        }).ToList(),
        Education = application.DraftEducation.Select(e => new CvDraftEducation
        {
            Id = e.Id,
            Include = e.Include,
            Institution = e.Institution,
            Degree = e.Degree,
            FieldOfStudy = e.FieldOfStudy,
            StartDate = e.StartDate,
            EndDate = e.EndDate,
            Description = e.Description
        }).ToList()
    };

    private static object ToDetailDto(JobApplication application) => new
    {
        application.Id,
        application.Company,
        application.RoleTitle,
        application.Status,
        application.Notes,
        application.JobAd,
        application.Summary,
        application.Skills,
        Experiences = application.DraftExperiences.Select(e => new
        {
            e.Id,
            e.Include,
            e.RelevanceScore,
            e.Company,
            e.Title,
            e.Location,
            e.StartDate,
            e.EndDate,
            e.IsCurrent,
            EngagementType = EngagementType.Normalize(e.EngagementType),
            e.Description,
            Skills = e.Skills ?? []
        }),
        Education = application.DraftEducation.Select(e => new
        {
            e.Id,
            e.Include,
            e.Institution,
            e.Degree,
            e.FieldOfStudy,
            e.StartDate,
            e.EndDate,
            e.Description
        }),
        application.CvId,
        application.CvFileName,
        HasGeneratedCv = application.GeneratedCvContent is { Length: > 0 },
        application.CreatedAt,
        application.UpdatedAt
    };

    private static List<string> NormalizeSkills(IEnumerable<string>? skills) =>
        (skills ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();
}
