using ApplicationAssistant.Api.Data;
using ApplicationAssistant.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace ApplicationAssistant.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/cvs")]
public class CvsController(MongoDbContext db, IWebHostEnvironment env) : ControllerBase
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

        return Ok(items.Select(c => new
        {
            c.Id,
            c.FileName,
            c.ContentType,
            c.SizeBytes,
            c.UploadedAt
        }));
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

        var uploadsRoot = Path.Combine(env.ContentRootPath, "uploads", userId.Value.ToString());
        Directory.CreateDirectory(uploadsRoot);

        var storedName = $"{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}";
        var fullPath = Path.Combine(uploadsRoot, storedName);

        await using (var stream = System.IO.File.Create(fullPath))
        {
            await file.CopyToAsync(stream, ct);
        }

        var cv = new CvDocument
        {
            Id = Guid.NewGuid(),
            UserId = userId.Value,
            FileName = Path.GetFileName(file.FileName),
            ContentType = string.IsNullOrWhiteSpace(file.ContentType)
                ? "application/octet-stream"
                : file.ContentType,
            StoragePath = Path.Combine("uploads", userId.Value.ToString(), storedName)
                .Replace('\\', '/'),
            SizeBytes = file.Length,
            UploadedAt = DateTimeOffset.UtcNow
        };

        await db.Cvs.InsertOneAsync(cv, cancellationToken: ct);

        return CreatedAtAction(nameof(List), new
        {
            cv.Id,
            cv.FileName,
            cv.ContentType,
            cv.SizeBytes,
            cv.UploadedAt
        });
    }
}
