using System.Text.RegularExpressions;
using ApplicationAssistant.AI;
using ApplicationAssistant.Api.Models;

namespace ApplicationAssistant.Api;

public static partial class ProfileMerge
{
    public static List<WorkExperience> MergeExperience(
        IEnumerable<WorkExperience>? existing,
        IEnumerable<ParsedWorkExperience> incoming,
        Guid sourceCvId)
    {
        var merged = CollapseExperience(existing ?? []);

        foreach (var item in DeduplicateIncomingExperience(incoming))
        {
            var match = merged.FirstOrDefault(e => ExperienceMatches(e, item));
            if (match is null)
            {
                var isCurrent = item.IsCurrent || CvDateParser.IsPresent(item.EndDate);
                merged.Add(new WorkExperience
                {
                    Id = Guid.NewGuid(),
                    SourceCvId = sourceCvId,
                    Company = item.Company.Trim(),
                    Title = item.Title.Trim(),
                    Location = NullIfWhiteSpace(item.Location),
                    StartDate = CvDateParser.Parse(item.StartDate),
                    EndDate = isCurrent ? null : CvDateParser.Parse(item.EndDate),
                    IsCurrent = isCurrent,
                    Description = NullIfWhiteSpace(item.Description)
                });
                continue;
            }

            match.SourceCvId = sourceCvId;
            match.Company = PreferRequired(item.Company, match.Company);
            match.Title = PreferRequired(item.Title, match.Title);
            match.Location = PreferOptional(item.Location, match.Location);
            match.StartDate = PreferDate(CvDateParser.Parse(item.StartDate), match.StartDate);
            match.IsCurrent = item.IsCurrent || CvDateParser.IsPresent(item.EndDate) || match.IsCurrent;
            match.EndDate = match.IsCurrent
                ? null
                : PreferDate(CvDateParser.Parse(item.EndDate), match.EndDate);
            match.Description = PreferOptional(item.Description, match.Description);
        }

        return OrderExperience(merged);
    }

    public static List<EducationRecord> MergeEducation(
        IEnumerable<EducationRecord>? existing,
        IEnumerable<ParsedEducation> incoming,
        Guid sourceCvId)
    {
        var merged = CollapseEducation(existing ?? []);

        foreach (var item in DeduplicateIncomingEducation(incoming))
        {
            var match = merged.FirstOrDefault(e => EducationMatches(e, item));
            if (match is null)
            {
                merged.Add(new EducationRecord
                {
                    Id = Guid.NewGuid(),
                    SourceCvId = sourceCvId,
                    Institution = item.Institution.Trim(),
                    Degree = NullIfWhiteSpace(item.Degree),
                    FieldOfStudy = NullIfWhiteSpace(item.FieldOfStudy),
                    StartDate = CvDateParser.Parse(item.StartDate),
                    EndDate = CvDateParser.Parse(item.EndDate),
                    Description = NullIfWhiteSpace(item.Description)
                });
                continue;
            }

            match.SourceCvId = sourceCvId;
            match.Institution = PreferRequired(item.Institution, match.Institution);
            match.Degree = PreferOptional(item.Degree, match.Degree);
            match.FieldOfStudy = PreferOptional(item.FieldOfStudy, match.FieldOfStudy);
            match.StartDate = PreferDate(CvDateParser.Parse(item.StartDate), match.StartDate);
            match.EndDate = PreferDate(CvDateParser.Parse(item.EndDate), match.EndDate);
            match.Description = PreferOptional(item.Description, match.Description);
        }

        return OrderEducation(merged);
    }

    public static List<WorkExperience> OrderExperience(IEnumerable<WorkExperience> items) =>
        items
            .OrderByDescending(e => e.IsCurrent)
            .ThenByDescending(e => e.StartDate ?? DateTime.MinValue)
            .ThenBy(e => e.Company, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static List<EducationRecord> OrderEducation(IEnumerable<EducationRecord> items) =>
        items
            .OrderByDescending(e => e.StartDate ?? DateTime.MinValue)
            .ThenBy(e => e.Institution, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static bool ExperienceMatches(WorkExperience existing, ParsedWorkExperience incoming)
    {
        if (Normalize(existing.Company) != Normalize(incoming.Company))
        {
            return false;
        }

        return YearsCompatible(existing.StartDate, CvDateParser.Parse(incoming.StartDate));
    }

    private static bool ExperienceMatches(WorkExperience left, WorkExperience right) =>
        Normalize(left.Company) == Normalize(right.Company)
        && YearsCompatible(left.StartDate, right.StartDate);

    private static bool EducationMatches(EducationRecord existing, ParsedEducation incoming)
    {
        if (Normalize(existing.Institution) != Normalize(incoming.Institution))
        {
            return false;
        }

        var existingDegree = Normalize(existing.Degree);
        var incomingDegree = Normalize(incoming.Degree);
        var existingField = Normalize(existing.FieldOfStudy);
        var incomingField = Normalize(incoming.FieldOfStudy);

        var degreeOk = string.IsNullOrEmpty(existingDegree)
            || string.IsNullOrEmpty(incomingDegree)
            || existingDegree == incomingDegree;
        var fieldOk = string.IsNullOrEmpty(existingField)
            || string.IsNullOrEmpty(incomingField)
            || existingField == incomingField;

        if (!degreeOk || !fieldOk)
        {
            return false;
        }

        if (YearsCompatible(existing.StartDate, CvDateParser.Parse(incoming.StartDate)))
        {
            return true;
        }

        return (!string.IsNullOrEmpty(existingDegree) || !string.IsNullOrEmpty(incomingDegree)
                || !string.IsNullOrEmpty(existingField) || !string.IsNullOrEmpty(incomingField))
               && degreeOk
               && fieldOk;
    }

    private static bool EducationMatches(EducationRecord left, EducationRecord right)
    {
        if (Normalize(left.Institution) != Normalize(right.Institution))
        {
            return false;
        }

        var leftDegree = Normalize(left.Degree);
        var rightDegree = Normalize(right.Degree);
        var leftField = Normalize(left.FieldOfStudy);
        var rightField = Normalize(right.FieldOfStudy);

        var degreeOk = string.IsNullOrEmpty(leftDegree)
            || string.IsNullOrEmpty(rightDegree)
            || leftDegree == rightDegree;
        var fieldOk = string.IsNullOrEmpty(leftField)
            || string.IsNullOrEmpty(rightField)
            || leftField == rightField;

        return degreeOk && fieldOk && YearsCompatible(left.StartDate, right.StartDate);
    }

    private static bool YearsCompatible(DateTime? left, DateTime? right)
    {
        if (left is null || right is null)
        {
            return true;
        }

        return left.Value.Year == right.Value.Year;
    }

    private static List<WorkExperience> CollapseExperience(IEnumerable<WorkExperience> existing)
    {
        var result = new List<WorkExperience>();
        foreach (var item in existing)
        {
            var match = result.FirstOrDefault(e => ExperienceMatches(e, item));
            if (match is null)
            {
                result.Add(item);
                continue;
            }

            match.SourceCvId = item.SourceCvId ?? match.SourceCvId;
            match.Location = PreferOptional(item.Location, match.Location);
            match.StartDate = PreferDate(item.StartDate, match.StartDate);
            match.IsCurrent = item.IsCurrent || match.IsCurrent;
            match.EndDate = match.IsCurrent ? null : PreferDate(item.EndDate, match.EndDate);
            match.Description = PreferOptional(item.Description, match.Description);
            match.Title = PreferRequired(item.Title, match.Title);
            match.Company = PreferRequired(item.Company, match.Company);
        }

        return result;
    }

    private static List<EducationRecord> CollapseEducation(IEnumerable<EducationRecord> existing)
    {
        var result = new List<EducationRecord>();
        foreach (var item in existing)
        {
            var match = result.FirstOrDefault(e => EducationMatches(e, item));
            if (match is null)
            {
                result.Add(item);
                continue;
            }

            match.SourceCvId = item.SourceCvId ?? match.SourceCvId;
            match.Degree = PreferOptional(item.Degree, match.Degree);
            match.FieldOfStudy = PreferOptional(item.FieldOfStudy, match.FieldOfStudy);
            match.StartDate = PreferDate(item.StartDate, match.StartDate);
            match.EndDate = PreferDate(item.EndDate, match.EndDate);
            match.Description = PreferOptional(item.Description, match.Description);
            match.Institution = PreferRequired(item.Institution, match.Institution);
        }

        return result;
    }

    private static IEnumerable<ParsedWorkExperience> DeduplicateIncomingExperience(
        IEnumerable<ParsedWorkExperience> incoming)
    {
        var result = new List<ParsedWorkExperience>();
        foreach (var item in incoming)
        {
            if (result.Any(e =>
                    Normalize(e.Company) == Normalize(item.Company)
                    && YearsCompatible(CvDateParser.Parse(e.StartDate), CvDateParser.Parse(item.StartDate))))
            {
                continue;
            }

            result.Add(item);
        }

        return result;
    }

    private static IEnumerable<ParsedEducation> DeduplicateIncomingEducation(
        IEnumerable<ParsedEducation> incoming)
    {
        var result = new List<ParsedEducation>();
        foreach (var item in incoming)
        {
            if (result.Any(e => EducationMatches(
                    new EducationRecord
                    {
                        Institution = e.Institution,
                        Degree = e.Degree,
                        FieldOfStudy = e.FieldOfStudy,
                        StartDate = CvDateParser.Parse(e.StartDate)
                    },
                    item)))
            {
                continue;
            }

            result.Add(item);
        }

        return result;
    }

    private static string PreferRequired(string? incoming, string? existing) =>
        !string.IsNullOrWhiteSpace(incoming) ? incoming.Trim() : (existing?.Trim() ?? string.Empty);

    private static string? PreferOptional(string? incoming, string? existing) =>
        !string.IsNullOrWhiteSpace(incoming)
            ? incoming.Trim()
            : !string.IsNullOrWhiteSpace(existing)
                ? existing.Trim()
                : null;

    private static DateTime? PreferDate(DateTime? incoming, DateTime? existing) =>
        incoming ?? existing;

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var collapsed = Whitespace().Replace(value.Trim(), " ").ToLowerInvariant();
        return NonAlphaNumeric().Replace(collapsed, string.Empty);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonAlphaNumeric();
}
