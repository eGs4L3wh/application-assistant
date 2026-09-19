using ApplicationAssistant.Api.Data;
using ApplicationAssistant.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace ApplicationAssistant.Api.Controllers;

public record UpsertExperienceRequest(
    string Company,
    string Title,
    string? Location,
    string? StartDate,
    string? EndDate,
    bool IsCurrent,
    string? Description);

public record UpsertEducationRequest(
    string Institution,
    string? Degree,
    string? FieldOfStudy,
    string? StartDate,
    string? EndDate,
    string? Description);

[ApiController]
[Authorize]
[Route("api/profile")]
public class ProfileController(MongoDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<object>> Get(CancellationToken ct)
    {
        var userId = User.GetAppUserId();
        if (userId is null) return Unauthorized();

        var user = await db.Users.Find(u => u.Id == userId).FirstOrDefaultAsync(ct);
        if (user is null) return NotFound(new { error = "User profile not found." });

        return Ok(new
        {
            experience = ProfileMerge.OrderExperience(user.Experience).Select(ToExperienceResponse),
            education = ProfileMerge.OrderEducation(user.Education).Select(ToEducationResponse)
        });
    }

    [HttpPost("experience")]
    public async Task<ActionResult<object>> CreateExperience(
        [FromBody] UpsertExperienceRequest request,
        CancellationToken ct)
    {
        var userId = User.GetAppUserId();
        if (userId is null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Company) || string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest(new { error = "Company and title are required." });
        }

        var user = await db.Users.Find(u => u.Id == userId).FirstOrDefaultAsync(ct);
        if (user is null) return NotFound(new { error = "User profile not found." });

        var isCurrent = request.IsCurrent || CvDateParser.IsPresent(request.EndDate);
        var entry = new WorkExperience
        {
            Id = Guid.NewGuid(),
            SourceCvId = null,
            Company = request.Company.Trim(),
            Title = request.Title.Trim(),
            Location = NullIfWhiteSpace(request.Location),
            StartDate = CvDateParser.Parse(request.StartDate),
            EndDate = isCurrent ? null : CvDateParser.Parse(request.EndDate),
            IsCurrent = isCurrent,
            Description = NullIfWhiteSpace(request.Description)
        };

        user.Experience.Add(entry);
        user.Experience = ProfileMerge.OrderExperience(user.Experience);
        await db.Users.ReplaceOneAsync(u => u.Id == userId, user, cancellationToken: ct);

        return Created($"/api/profile/experience/{entry.Id}", ToExperienceResponse(entry));
    }

    [HttpPut("experience/{id:guid}")]
    public async Task<ActionResult<object>> UpdateExperience(
        Guid id,
        [FromBody] UpsertExperienceRequest request,
        CancellationToken ct)
    {
        var userId = User.GetAppUserId();
        if (userId is null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Company) || string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest(new { error = "Company and title are required." });
        }

        var user = await db.Users.Find(u => u.Id == userId).FirstOrDefaultAsync(ct);
        if (user is null) return NotFound(new { error = "User profile not found." });

        var entry = user.Experience.FirstOrDefault(e => e.Id == id);
        if (entry is null) return NotFound(new { error = "Experience entry not found." });

        var isCurrent = request.IsCurrent || CvDateParser.IsPresent(request.EndDate);
        entry.Company = request.Company.Trim();
        entry.Title = request.Title.Trim();
        entry.Location = NullIfWhiteSpace(request.Location);
        entry.StartDate = CvDateParser.Parse(request.StartDate);
        entry.IsCurrent = isCurrent;
        entry.EndDate = isCurrent ? null : CvDateParser.Parse(request.EndDate);
        entry.Description = NullIfWhiteSpace(request.Description);

        user.Experience = ProfileMerge.OrderExperience(user.Experience);
        await db.Users.ReplaceOneAsync(u => u.Id == userId, user, cancellationToken: ct);
        return Ok(ToExperienceResponse(entry));
    }

    [HttpDelete("experience/{id:guid}")]
    public async Task<IActionResult> DeleteExperience(Guid id, CancellationToken ct)
    {
        var userId = User.GetAppUserId();
        if (userId is null) return Unauthorized();

        var user = await db.Users.Find(u => u.Id == userId).FirstOrDefaultAsync(ct);
        if (user is null) return NotFound(new { error = "User profile not found." });

        var removed = user.Experience.RemoveAll(e => e.Id == id);
        if (removed == 0) return NotFound(new { error = "Experience entry not found." });

        await db.Users.ReplaceOneAsync(u => u.Id == userId, user, cancellationToken: ct);
        return NoContent();
    }

    [HttpPost("education")]
    public async Task<ActionResult<object>> CreateEducation(
        [FromBody] UpsertEducationRequest request,
        CancellationToken ct)
    {
        var userId = User.GetAppUserId();
        if (userId is null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Institution))
        {
            return BadRequest(new { error = "Institution is required." });
        }

        var user = await db.Users.Find(u => u.Id == userId).FirstOrDefaultAsync(ct);
        if (user is null) return NotFound(new { error = "User profile not found." });

        var entry = new EducationRecord
        {
            Id = Guid.NewGuid(),
            SourceCvId = null,
            Institution = request.Institution.Trim(),
            Degree = NullIfWhiteSpace(request.Degree),
            FieldOfStudy = NullIfWhiteSpace(request.FieldOfStudy),
            StartDate = CvDateParser.Parse(request.StartDate),
            EndDate = CvDateParser.Parse(request.EndDate),
            Description = NullIfWhiteSpace(request.Description)
        };

        user.Education.Add(entry);
        user.Education = ProfileMerge.OrderEducation(user.Education);
        await db.Users.ReplaceOneAsync(u => u.Id == userId, user, cancellationToken: ct);

        return Created($"/api/profile/education/{entry.Id}", ToEducationResponse(entry));
    }

    [HttpPut("education/{id:guid}")]
    public async Task<ActionResult<object>> UpdateEducation(
        Guid id,
        [FromBody] UpsertEducationRequest request,
        CancellationToken ct)
    {
        var userId = User.GetAppUserId();
        if (userId is null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Institution))
        {
            return BadRequest(new { error = "Institution is required." });
        }

        var user = await db.Users.Find(u => u.Id == userId).FirstOrDefaultAsync(ct);
        if (user is null) return NotFound(new { error = "User profile not found." });

        var entry = user.Education.FirstOrDefault(e => e.Id == id);
        if (entry is null) return NotFound(new { error = "Education entry not found." });

        entry.Institution = request.Institution.Trim();
        entry.Degree = NullIfWhiteSpace(request.Degree);
        entry.FieldOfStudy = NullIfWhiteSpace(request.FieldOfStudy);
        entry.StartDate = CvDateParser.Parse(request.StartDate);
        entry.EndDate = CvDateParser.Parse(request.EndDate);
        entry.Description = NullIfWhiteSpace(request.Description);

        user.Education = ProfileMerge.OrderEducation(user.Education);
        await db.Users.ReplaceOneAsync(u => u.Id == userId, user, cancellationToken: ct);
        return Ok(ToEducationResponse(entry));
    }

    [HttpDelete("education/{id:guid}")]
    public async Task<IActionResult> DeleteEducation(Guid id, CancellationToken ct)
    {
        var userId = User.GetAppUserId();
        if (userId is null) return Unauthorized();

        var user = await db.Users.Find(u => u.Id == userId).FirstOrDefaultAsync(ct);
        if (user is null) return NotFound(new { error = "User profile not found." });

        var removed = user.Education.RemoveAll(e => e.Id == id);
        if (removed == 0) return NotFound(new { error = "Education entry not found." });

        await db.Users.ReplaceOneAsync(u => u.Id == userId, user, cancellationToken: ct);
        return NoContent();
    }

    private static object ToExperienceResponse(WorkExperience e) => new
    {
        e.Id,
        e.SourceCvId,
        e.Company,
        e.Title,
        e.Location,
        StartDate = e.StartDate?.ToUniversalTime().ToString("O"),
        EndDate = e.EndDate?.ToUniversalTime().ToString("O"),
        e.IsCurrent,
        e.Description
    };

    private static object ToEducationResponse(EducationRecord e) => new
    {
        e.Id,
        e.SourceCvId,
        e.Institution,
        e.Degree,
        e.FieldOfStudy,
        StartDate = e.StartDate?.ToUniversalTime().ToString("O"),
        EndDate = e.EndDate?.ToUniversalTime().ToString("O"),
        e.Description
    };

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
