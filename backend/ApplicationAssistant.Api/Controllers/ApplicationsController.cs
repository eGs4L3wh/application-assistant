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

        var charged = await db.Users.FindOneAndUpdateAsync(
            u => u.Id == userId.Value && u.Credits > 0,
            Builders<AppUser>.Update.Inc(u => u.Credits, -1),
            new FindOneAndUpdateOptions<AppUser, AppUser> { ReturnDocument = ReturnDocument.After },
            ct);
        if (charged is null)
        {
            return StatusCode(StatusCodes.Status402PaymentRequired, new
            {
                error = "You need a credit to start an application."
            });
        }

        JobAdMetadata metadata;
        try
        {
            metadata = await cvGenerator.ExtractJobMetadataAsync(request.JobAd.Trim(), ct);
        }
        catch
        {
            await db.Users.UpdateOneAsync(
                u => u.Id == userId.Value,
                Builders<AppUser>.Update.Inc(u => u.Credits, 1),
                cancellationToken: CancellationToken.None);
            throw;
        }

        var now = DateTimeOffset.UtcNow;
        var company = string.IsNullOrWhiteSpace(metadata.Company) ? "Unknown company" : metadata.Company.Trim();
        var roleTitle = string.IsNullOrWhiteSpace(metadata.RoleTitle) ? "Unknown role" : metadata.RoleTitle.Trim();

        var application = new JobApplication
        {
            Id = Guid.NewGuid(),
            UserId = userId.Value,
            Company = company,
            RoleTitle = roleTitle,
            JobAd = request.JobAd.Trim(),
            MustHave = metadata.MustHave ?? [],
            NiceToHave = metadata.NiceToHave ?? [],
            Summary = null,
            Skills = [],
            DraftExperiences = charged.Experience.Select(ToSeedExperience).ToList(),
            DraftEducation = charged.Education.Select(ToSeedEducation).ToList(),
            WizardStep = "requirements",
            WizardReached = "requirements",
            Status = "Draft",
            CreatedAt = now,
            UpdatedAt = now
        };

        await db.Applications.InsertOneAsync(application, cancellationToken: ct);
        return CreatedAtAction(nameof(Get), new { id = application.Id }, ToDetailDto(application));
    }

    [HttpPost("{id:guid}/requirements")]
    public async Task<ActionResult<object>> RereadJobAd(
        Guid id,
        [FromBody] RereadJobAdRequest? request,
        CancellationToken ct)
    {
        var userId = User.GetAppUserId();
        if (userId is null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request?.JobAd))
        {
            return BadRequest(new { error = "Job ad is required." });
        }

        var application = await db.Applications
            .Find(a => a.Id == id && a.UserId == userId)
            .FirstOrDefaultAsync(ct);
        if (application is null) return NotFound();

        var user = await db.Users.Find(u => u.Id == userId).FirstOrDefaultAsync(ct);
        if (user is null) return Unauthorized();

        var metadata = await cvGenerator.ExtractJobMetadataAsync(request.JobAd.Trim(), ct);
        application.JobAd = request.JobAd.Trim();
        application.Company = string.IsNullOrWhiteSpace(metadata.Company) ? "Unknown company" : metadata.Company.Trim();
        application.RoleTitle = string.IsNullOrWhiteSpace(metadata.RoleTitle) ? "Unknown role" : metadata.RoleTitle.Trim();
        application.MustHave = metadata.MustHave ?? [];
        application.NiceToHave = metadata.NiceToHave ?? [];
        application.Summary = null;
        application.Skills = [];
        application.ScoredFitNotes = null;
        application.DescriptionsWritten = false;
        application.TailoredExperienceIds = [];
        application.WizardStep = "requirements";
        application.WizardReached = "requirements";
        RestoreDraftExperiences(application, user);
        foreach (var experience in application.DraftExperiences)
        {
            experience.Include = false;
            experience.RelevanceScore = 0;
        }

        application.UpdatedAt = DateTimeOffset.UtcNow;
        await db.Applications.ReplaceOneAsync(a => a.Id == application.Id, application, cancellationToken: ct);
        return Ok(ToDetailDto(application));
    }

    [HttpPost("{id:guid}/relevance")]
    public async Task<ActionResult<object>> ScoreRelevance(
        Guid id,
        [FromBody] ScoreRelevanceRequest? request,
        CancellationToken ct)
    {
        var userId = User.GetAppUserId();
        if (userId is null) return Unauthorized();

        var application = await db.Applications
            .Find(a => a.Id == id && a.UserId == userId)
            .FirstOrDefaultAsync(ct);
        if (application is null) return NotFound();
        await MigrateLegacyWizardAsync(application, ct);

        var user = await db.Users.Find(u => u.Id == userId).FirstOrDefaultAsync(ct);
        if (user is null) return Unauthorized();

        if (request?.FitNotes is not null)
        {
            application.FitNotes = NormalizeFitNotes(request.FitNotes);
        }

        RestoreDraftExperiences(application, user);
        var sources = ResolveSources(application, user);
        var scores = await cvGenerator.ScoreRelevanceAsync(new RelevanceScoreRequest
        {
            JobAd = application.JobAd ?? string.Empty,
            FitNotes = application.FitNotes,
            TargetCompany = application.Company,
            TargetRoleTitle = application.RoleTitle,
            MustHave = application.MustHave ?? [],
            NiceToHave = application.NiceToHave ?? [],
            Experiences = sources
        }, ct);

        var scoreById = scores.Experiences.ToDictionary(score => score.Id, score => score.RelevanceScore);
        foreach (var experience in application.DraftExperiences)
        {
            var score = scoreById.TryGetValue(experience.Id, out var value) ? value : 40;
            experience.RelevanceScore = Math.Clamp(score, 0, 100);
            experience.Include = experience.RelevanceScore >= CvRelevance.IncludeCutoff;
        }

        application.WizardStep = "roles";
        application.WizardReached = "roles";
        application.ScoredFitNotes = application.FitNotes ?? "";
        application.DescriptionsWritten = false;
        application.TailoredExperienceIds = [];
        application.Summary = null;
        application.Skills = [];
        application.UpdatedAt = DateTimeOffset.UtcNow;
        await db.Applications.ReplaceOneAsync(a => a.Id == application.Id, application, cancellationToken: ct);
        return Ok(ToDetailDto(application));
    }

    [HttpPost("{id:guid}/tailor")]
    public async Task<ActionResult<object>> Tailor(
        Guid id,
        [FromBody] TailorApplicationRequest? request,
        CancellationToken ct)
    {
        var userId = User.GetAppUserId();
        if (userId is null) return Unauthorized();

        var application = await db.Applications
            .Find(a => a.Id == id && a.UserId == userId)
            .FirstOrDefaultAsync(ct);
        if (application is null) return NotFound();
        await MigrateLegacyWizardAsync(application, ct);

        if (request?.ExperienceIds is null)
        {
            return BadRequest(new { error = "Choose which roles to include." });
        }

        var user = await db.Users.Find(u => u.Id == userId).FirstOrDefaultAsync(ct);
        if (user is null) return Unauthorized();

        if (request.FitNotes is not null)
        {
            application.FitNotes = NormalizeFitNotes(request.FitNotes);
        }

        var selected = request.ExperienceIds
            .Where(experienceId => application.DraftExperiences.Any(experience => experience.Id == experienceId))
            .Distinct()
            .ToList();
        var sources = ResolveSources(application, user);
        var fullName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email : user.DisplayName;
        var draft = await cvGenerator.TailorAsync(new TailorRequest
        {
            JobAd = application.JobAd ?? string.Empty,
            TargetCompany = application.Company,
            TargetRoleTitle = application.RoleTitle,
            FitNotes = application.FitNotes,
            MustHave = application.MustHave ?? [],
            NiceToHave = application.NiceToHave ?? [],
            FullName = fullName,
            Email = user.GetCvEmail(),
            Phone = string.IsNullOrWhiteSpace(user.Phone) ? null : user.Phone.Trim(),
            Experiences = sources,
            SelectedExperienceIds = selected,
            Education = application.DraftEducation.Select(ToSourceEducationFromDraft).ToList()
        }, ct);

        var tailoredById = draft.Experiences.ToDictionary(experience => experience.Id);
        foreach (var experience in application.DraftExperiences)
        {
            if (!tailoredById.TryGetValue(experience.Id, out var tailored))
            {
                experience.Include = false;
                continue;
            }

            experience.Include = tailored.Include;
            experience.Title = string.IsNullOrWhiteSpace(tailored.Title) ? experience.Title : tailored.Title;
            experience.Description = tailored.Description;
            experience.Skills = NormalizeSkills(tailored.Skills);
        }

        application.Summary = draft.Summary;
        application.Skills = draft.Skills ?? [];
        application.WizardStep = "review";
        application.WizardReached = "review";
        application.DescriptionsWritten = true;
        application.TailoredExperienceIds = selected;
        application.UpdatedAt = DateTimeOffset.UtcNow;
        await db.Applications.ReplaceOneAsync(a => a.Id == application.Id, application, cancellationToken: ct);
        return Ok(ToDetailDto(application));
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
        await MigrateLegacyWizardAsync(application, ct);

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
        await MigrateLegacyWizardAsync(application, ct);

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
        if (request.FitNotes is not null) application.FitNotes = NormalizeFitNotes(request.FitNotes);
        if (request.WizardStep is not null)
        {
            if (!TryNormalizeWizardStep(request.WizardStep, out var step))
            {
                return BadRequest(new { error = "Unsupported wizard step." });
            }

            if (!CanVisit(application, step))
            {
                return BadRequest(new { error = "That step is not available yet." });
            }

            var reached = EffectiveReached(application);
            application.WizardStep = step;
            if (WizardIndex(step) > WizardIndex(reached))
            {
                application.WizardReached = step;
            }
        }
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

    [HttpPost("{id:guid}/coverage")]
    public async Task<ActionResult<object>> AssessCoverage(
        Guid id,
        [FromBody] CoverageRequestBody? request,
        CancellationToken ct)
    {
        var userId = User.GetAppUserId();
        if (userId is null) return Unauthorized();

        var application = await db.Applications
            .Find(a => a.Id == id && a.UserId == userId)
            .FirstOrDefaultAsync(ct);
        if (application is null) return NotFound();

        if (application.MustHave is not { Count: > 0 } && application.NiceToHave is not { Count: > 0 })
        {
            return BadRequest(new { error = "This application has no requirements to check." });
        }

        var experiences = request?.Experiences?
            .Select(e => new CoverageExperience
            {
                Company = e.Company?.Trim() ?? string.Empty,
                Title = e.Title?.Trim() ?? string.Empty,
                Description = e.Description,
                Skills = e.Skills ?? []
            })
            .ToList();
        var education = request?.Education?
            .Select(e => new CoverageEducation
            {
                Institution = e.Institution?.Trim() ?? string.Empty,
                Degree = e.Degree,
                FieldOfStudy = e.FieldOfStudy,
                Description = e.Description
            })
            .ToList();

        if (experiences is null)
        {
            experiences = application.DraftExperiences
                .Where(e => e.Include)
                .Select(e => new CoverageExperience
                {
                    Company = e.Company,
                    Title = e.Title,
                    Description = e.Description,
                    Skills = e.Skills ?? []
                })
                .ToList();
        }

        if (education is null)
        {
            education = application.DraftEducation
                .Where(e => e.Include)
                .Select(e => new CoverageEducation
                {
                    Institution = e.Institution,
                    Degree = e.Degree,
                    FieldOfStudy = e.FieldOfStudy,
                    Description = e.Description
                })
                .ToList();
        }

        var coverage = await cvGenerator.AssessMustHaveCoverageAsync(new RequirementCoverageRequest
        {
            MustHave = application.MustHave ?? [],
            NiceToHave = application.NiceToHave ?? [],
            Experiences = experiences,
            Education = education
        }, ct);

        return Ok(new
        {
            Items = coverage.Items.Select(item => new
            {
                item.Requirement,
                item.Kind,
                item.Coverage,
                item.Evidence
            })
        });
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
            user.Languages,
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

    private static readonly string[] WizardStepOrder = ["ad", "requirements", "fit", "roles", "review"];

    private async Task MigrateLegacyWizardAsync(JobApplication application, CancellationToken ct)
    {
        var changed = false;
        var backfillSelection = false;
        if (string.IsNullOrWhiteSpace(application.WizardStep) && string.IsNullOrWhiteSpace(application.WizardReached))
        {
            application.WizardStep = "review";
            application.WizardReached = "review";
            application.DescriptionsWritten = true;
            backfillSelection = true;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(application.WizardReached))
        {
            application.WizardReached = EffectiveReached(application);
            changed = true;
        }

        var reachedReview = string.Equals(application.WizardStep, "review", StringComparison.OrdinalIgnoreCase)
            || string.Equals(application.WizardReached, "review", StringComparison.OrdinalIgnoreCase);
        if (!application.DescriptionsWritten && reachedReview)
        {
            application.DescriptionsWritten = true;
            backfillSelection = true;
            changed = true;
        }

        var scored = application.ScoredFitNotes is not null
            || application.DraftExperiences.Any(experience => experience.RelevanceScore > 0)
            || application.WizardStep is "roles" or "review"
            || application.WizardReached is "roles" or "review";
        if (application.ScoredFitNotes is null && scored)
        {
            application.ScoredFitNotes = application.FitNotes ?? "";
            changed = true;
        }

        if (backfillSelection && application.TailoredExperienceIds is not { Count: > 0 })
        {
            application.TailoredExperienceIds = application.DraftExperiences
                .Where(experience => experience.Include)
                .Select(experience => experience.Id)
                .ToList();
            changed = true;
        }

        if (!changed) return;

        application.UpdatedAt = DateTimeOffset.UtcNow;
        await db.Applications.ReplaceOneAsync(a => a.Id == application.Id, application, cancellationToken: ct);
    }

    private static bool TryNormalizeWizardStep(string? value, out string step)
    {
        step = value?.Trim().ToLowerInvariant() ?? "";
        return WizardStepOrder.Contains(step);
    }

    private static int WizardIndex(string step) => Array.IndexOf(WizardStepOrder, step);

    private static string EffectiveReached(JobApplication application)
    {
        if (TryNormalizeWizardStep(application.WizardReached, out var reached)) return reached;
        if (TryNormalizeWizardStep(application.WizardStep, out var current)) return current;
        return "review";
    }

    private static bool CanVisit(JobApplication application, string step) => step switch
    {
        "ad" or "requirements" or "fit" => true,
        "roles" => application.ScoredFitNotes is not null
            || application.DraftExperiences.Any(experience => experience.RelevanceScore > 0),
        "review" => application.DescriptionsWritten,
        _ => false
    };

    private static void RestoreDraftExperiences(JobApplication application, AppUser user)
    {
        var profileById = user.Experience.ToDictionary(experience => experience.Id);
        foreach (var draft in application.DraftExperiences)
        {
            if (!profileById.TryGetValue(draft.Id, out var source))
            {
                continue;
            }

            draft.Company = source.Company;
            draft.Title = source.Title;
            draft.Location = source.Location;
            draft.StartDate = source.StartDate;
            draft.EndDate = source.EndDate;
            draft.IsCurrent = source.IsCurrent;
            draft.EngagementType = EngagementType.Normalize(source.EngagementType);
            draft.Description = source.Description;
            draft.Skills = NormalizeSkills(source.Skills);
        }
    }

    private static string NormalizeFitNotes(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= 4000 ? trimmed : trimmed[..4000];
    }

    private static ApplicationDraftExperience ToSeedExperience(WorkExperience experience) => new()
    {
        Id = experience.Id,
        Include = false,
        RelevanceScore = 0,
        Company = experience.Company,
        Title = experience.Title,
        Location = experience.Location,
        StartDate = experience.StartDate,
        EndDate = experience.EndDate,
        IsCurrent = experience.IsCurrent,
        EngagementType = EngagementType.Normalize(experience.EngagementType),
        Description = experience.Description,
        Skills = NormalizeSkills(experience.Skills)
    };

    private static ApplicationDraftEducation ToSeedEducation(EducationRecord education) => new()
    {
        Id = education.Id,
        Include = true,
        Institution = education.Institution,
        Degree = education.Degree,
        FieldOfStudy = education.FieldOfStudy,
        StartDate = education.StartDate,
        EndDate = education.EndDate,
        Description = education.Description
    };

    private static List<CvSourceExperience> ResolveSources(JobApplication application, AppUser user)
    {
        var profileById = user.Experience.ToDictionary(experience => experience.Id);
        return application.DraftExperiences.Select(draft =>
        {
            if (profileById.TryGetValue(draft.Id, out var source))
            {
                return ToSourceExperience(source);
            }

            return new CvSourceExperience
            {
                Id = draft.Id,
                Company = draft.Company,
                Title = draft.Title,
                Location = draft.Location,
                StartDate = draft.StartDate,
                EndDate = draft.EndDate,
                IsCurrent = draft.IsCurrent,
                EngagementType = EngagementType.Normalize(draft.EngagementType),
                Description = draft.Description,
                Skills = draft.Skills ?? []
            };
        }).ToList();
    }

    private static CvSourceEducation ToSourceEducationFromDraft(ApplicationDraftEducation education) => new()
    {
        Id = education.Id,
        Institution = education.Institution,
        Degree = education.Degree,
        FieldOfStudy = education.FieldOfStudy,
        StartDate = education.StartDate,
        EndDate = education.EndDate,
        Description = education.Description
    };

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
        WizardStep = string.IsNullOrWhiteSpace(application.WizardStep) ? "review" : application.WizardStep,
        WizardReached = EffectiveReached(application),
        application.FitNotes,
        application.ScoredFitNotes,
        application.DescriptionsWritten,
        TailoredExperienceIds = application.TailoredExperienceIds ?? [],
        MustHave = application.MustHave ?? [],
        NiceToHave = application.NiceToHave ?? [],
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

public record RereadJobAdRequest(string? JobAd);

public record ScoreRelevanceRequest(string? FitNotes);

public record TailorApplicationRequest(List<Guid>? ExperienceIds, string? FitNotes);

public record CoverageRequestBody(
    List<CoverageExperienceInput>? Experiences,
    List<CoverageEducationInput>? Education);

public record CoverageExperienceInput(string? Company, string? Title, string? Description, List<string>? Skills);

public record CoverageEducationInput(
    string? Institution,
    string? Degree,
    string? FieldOfStudy,
    string? Description);
