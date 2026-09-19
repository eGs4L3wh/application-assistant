using ApplicationAssistant.AI;
using ApplicationAssistant.Api.Data;
using ApplicationAssistant.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace ApplicationAssistant.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/cvs")]
public class CvsController(MongoDbContext db, IWebHostEnvironment env, ICvParser cvParser) : ControllerBase
{
    private static readonly HashSet<string> AllowedContentTypes =
    [
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    ];

    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> List(CancellationToken ct)
    {
        var userId = User.GetAppUserId();
        if (userId is null) return Unauthorized();

        var items = await db.Cvs
            .Find(c => c.UserId == userId)
            .SortByDescending(c => c.UploadedAt)
            .ToListAsync(ct);

        return Ok(items.Select(ToCvResponse));
    }

    [HttpPost]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<object>> Upload(IFormFile file, CancellationToken ct)
    {
        var userId = User.GetAppUserId();
        if (userId is null) return Unauthorized();

        if (file is null || file.Length == 0)
        {
            return BadRequest(new { error = "A non-empty file is required." });
        }

        if (!AllowedContentTypes.Contains(file.ContentType)
            && !file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            && !file.FileName.EndsWith(".doc", StringComparison.OrdinalIgnoreCase)
            && !file.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "Only PDF or Word documents are allowed." });
        }

        await using var memory = new MemoryStream();
        await file.CopyToAsync(memory, ct);
        var bytes = memory.ToArray();

        var contentType = string.IsNullOrWhiteSpace(file.ContentType)
            ? "application/octet-stream"
            : file.ContentType;
        var fileName = Path.GetFileName(file.FileName);

        CvParseResult parsed;
        try
        {
            parsed = await cvParser.ParseAsync(bytes, contentType, fileName, ct);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = "Failed to parse CV with AI.",
                detail = ex.Message,
                inner = ex.InnerException?.Message
            });
        }

        var uploadsRoot = Path.Combine(env.ContentRootPath, "uploads", userId.Value.ToString());
        Directory.CreateDirectory(uploadsRoot);

        var storedName = $"{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}";
        var fullPath = Path.Combine(uploadsRoot, storedName);
        await System.IO.File.WriteAllBytesAsync(fullPath, bytes, ct);

        var cvId = Guid.NewGuid();
        var cv = new CvDocument
        {
            Id = cvId,
            UserId = userId.Value,
            FileName = fileName,
            ContentType = contentType,
            StoragePath = Path.Combine("uploads", userId.Value.ToString(), storedName)
                .Replace('\\', '/'),
            Content = bytes,
            SizeBytes = bytes.LongLength,
            UploadedAt = DateTimeOffset.UtcNow
        };

        var user = await db.Users.Find(u => u.Id == userId.Value).FirstOrDefaultAsync(ct);
        if (user is null)
        {
            return NotFound(new { error = "User profile not found." });
        }

        var experience = ProfileMerge.MergeExperience(user.Experience, parsed.Experiences, cvId);
        var education = ProfileMerge.MergeEducation(user.Education, parsed.Education, cvId);

        await db.Cvs.InsertOneAsync(cv, cancellationToken: ct);
        await db.Users.UpdateOneAsync(
            u => u.Id == userId.Value,
            Builders<AppUser>.Update
                .Set(u => u.Experience, experience)
                .Set(u => u.Education, education),
            cancellationToken: ct);

        return CreatedAtAction(nameof(List), ToCvResponse(cv));
    }

    private static object ToCvResponse(CvDocument cv) => new
    {
        cv.Id,
        cv.FileName,
        cv.ContentType,
        cv.SizeBytes,
        cv.UploadedAt
    };
}
