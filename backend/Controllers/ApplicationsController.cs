using ApplicationAssistant.Api.Data;
using ApplicationAssistant.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace ApplicationAssistant.Api.Controllers;

public record CreateApplicationRequest(string Company, string RoleTitle, string? Notes, Guid? CvId);
public record UpdateApplicationRequest(string? Company, string? RoleTitle, string? Status, string? Notes, Guid? CvId);

[ApiController]
[Authorize]
[Route("api/applications")]
public class ApplicationsController(MongoDbContext db) : ControllerBase
{
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
