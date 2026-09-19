using System.Text.RegularExpressions;
using ApplicationAssistant.AI;
using ApplicationAssistant.Api.Data;
using ApplicationAssistant.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace ApplicationAssistant.Api.Controllers;

public record CreateApplicationRequest(string Company, string RoleTitle, string? Notes, Guid? CvId);
public record UpdateApplicationRequest(string? Company, string? RoleTitle, string? Status, string? Notes, Guid? CvId);
public record GenerateCvRequest(string JobAd, string? Company, string? RoleTitle);

[ApiController]
[Authorize]
[Route("api/applications")]
public class ApplicationsController(MongoDbContext db, ICvGenerator cvGenerator, CvPdfRenderer pdfRenderer) : ControllerBase
{
    [HttpPost("generate-cv")]
    public async Task<IActionResult> GenerateCv([FromBody] GenerateCvRequest request, CancellationToken ct)
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
            TargetCompany = string.IsNullOrWhiteSpace(request.Company) ? null : request.Company.Trim(),
            TargetRoleTitle = string.IsNullOrWhiteSpace(request.RoleTitle) ? null : request.RoleTitle.Trim(),
            FullName = fullName,
            Email = user.Email,
            Experiences = user.Experience.Select(e => new CvSourceExperience
            {
                Id = e.Id,
                Company = e.Company,
                Title = e.Title,
                Location = e.Location,
                StartDate = e.StartDate,
                EndDate = e.EndDate,
                IsCurrent = e.IsCurrent,
                Description = e.Description
            }).ToList(),
            Education = user.Education.Select(e => new CvSourceEducation
            {
                Id = e.Id,
                Institution = e.Institution,
                Degree = e.Degree,
                FieldOfStudy = e.FieldOfStudy,
                StartDate = e.StartDate,
                EndDate = e.EndDate,
                Description = e.Description
            }).ToList()
        }, ct);

        var aligned = ExperienceAligner.Align(user.Experience, draft.Experiences);
        var educationById = user.Education.ToDictionary(e => e.Id);
        var educationForPdf = draft.Education
            .Where(e => e.Include && educationById.ContainsKey(e.Id))
            .Select(e => (educationById[e.Id], e))
            .OrderByDescending(pair => pair.Item1.EndDate ?? pair.Item1.StartDate ?? DateTime.MinValue)
            .ToList();

        var pdfBytes = pdfRenderer.Render(
            fullName,
            user.Email,
            request.RoleTitle,
            request.Company,
            draft,
            aligned,
            educationForPdf);

        var fileLabel = SanitizeFilePart(request.Company)
            ?? SanitizeFilePart(request.RoleTitle)
            ?? "tailored";
        return File(pdfBytes, "application/pdf", $"cv-{fileLabel}.pdf");
    }

    private static string? SanitizeFilePart(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var cleaned = Regex.Replace(value.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "-");
        cleaned = cleaned.Trim('-');
        return string.IsNullOrEmpty(cleaned) ? null : cleaned[..Math.Min(cleaned.Length, 40)];
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
            a.CreatedAt,
            a.UpdatedAt
        }));
    }

    [HttpPost]
    public async Task<ActionResult<object>> Create([FromBody] CreateApplicationRequest request, CancellationToken ct)
    {
        var userId = User.GetAppUserId();
        if (userId is null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Company) || string.IsNullOrWhiteSpace(request.RoleTitle))
        {
            return BadRequest(new { error = "Company and role title are required." });
        }

        string? cvFileName = null;
        if (request.CvId is Guid cvId)
        {
            var cv = await db.Cvs.Find(c => c.Id == cvId && c.UserId == userId).FirstOrDefaultAsync(ct);
            if (cv is null) return BadRequest(new { error = "CV not found." });
            cvFileName = cv.FileName;
        }

        var now = DateTimeOffset.UtcNow;
        var application = new JobApplication
        {
            Id = Guid.NewGuid(),
            UserId = userId.Value,
            Company = request.Company.Trim(),
            RoleTitle = request.RoleTitle.Trim(),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            CvId = request.CvId,
            CvFileName = cvFileName,
            Status = "Draft",
            CreatedAt = now,
            UpdatedAt = now
        };

        await db.Applications.InsertOneAsync(application, cancellationToken: ct);

        return CreatedAtAction(nameof(List), new
        {
            application.Id,
            application.Company,
            application.RoleTitle,
            application.Status,
            application.Notes,
            application.CvId,
            application.CvFileName,
            application.CreatedAt,
            application.UpdatedAt
        });
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

        if (request.Company is not null) application.Company = request.Company.Trim();
        if (request.RoleTitle is not null) application.RoleTitle = request.RoleTitle.Trim();
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

        application.UpdatedAt = DateTimeOffset.UtcNow;
        await db.Applications.ReplaceOneAsync(
            a => a.Id == application.Id,
            application,
            cancellationToken: ct);

        return Ok(new
        {
            application.Id,
            application.Company,
            application.RoleTitle,
            application.Status,
            application.Notes,
            application.CvId,
            application.CvFileName,
            application.CreatedAt,
            application.UpdatedAt
        });
    }
}
