using System.Globalization;
using System.Text.RegularExpressions;

namespace ApplicationAssistant.Api;

public static partial class CvDateParser
{
    /// <summary>
    /// Parses CV-style dates into the first day of the resolved month (UTC).
    /// Examples: "2025" → 2025-01-01, "Jul 2025" → 2025-07-01, "2025-07" → 2025-07-01.
    /// Present/current/ongoing → null.
    /// </summary>
    public static DateTime? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var raw = Whitespace().Replace(value.Trim(), " ");
        var lower = raw.ToLowerInvariant();
        if (lower is "present" or "current" or "now" or "ongoing" or "today")
        {
            return null;
        }

        // YYYY-MM or YYYY-MM-DD
        if (Regex.IsMatch(raw, @"^\d{4}-\d{2}(-\d{2})?$"))
        {
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var iso))
            {
                return FirstOfMonthUtc(iso.Year, iso.Month);
            }
        }

        // Year only
        if (Regex.IsMatch(raw, @"^(19|20)\d{2}$"))
        {
            return FirstOfMonthUtc(int.Parse(raw, CultureInfo.InvariantCulture), 1);
        }

        // "Jul 2025", "July 2025", "Jul-2025"
        var monthYear = MonthYear().Match(raw);
        if (monthYear.Success
            && TryParseMonth(monthYear.Groups["month"].Value, out var month)
            && int.TryParse(monthYear.Groups["year"].Value, out var myYear))
        {
            return FirstOfMonthUtc(myYear, month);
        }

        // "2025 Jul"
        var yearMonth = YearMonth().Match(raw);
        if (yearMonth.Success
            && int.TryParse(yearMonth.Groups["year"].Value, out var ymYear)
            && TryParseMonth(yearMonth.Groups["month"].Value, out var ymMonth))
        {
            return FirstOfMonthUtc(ymYear, ymMonth);
        }

        // Fallback culture parse, then snap to first of month
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dt)
            || DateTime.TryParse(raw, CultureInfo.GetCultureInfo("en-GB"), DateTimeStyles.AllowWhiteSpaces, out dt)
            || DateTime.TryParse(raw, CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.AllowWhiteSpaces, out dt))
        {
            return FirstOfMonthUtc(dt.Year, dt.Month);
        }

        // Last resort: extract a year
        var yearOnly = YearOnly().Match(raw);
        if (yearOnly.Success)
        {
            return FirstOfMonthUtc(int.Parse(yearOnly.Value, CultureInfo.InvariantCulture), 1);
        }

        return null;
    }

    public static bool IsPresent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var lower = value.Trim().ToLowerInvariant();
        return lower is "present" or "current" or "now" or "ongoing" or "today";
    }

    private static DateTime FirstOfMonthUtc(int year, int month) =>
        new(year, month, 1, 0, 0, 0, DateTimeKind.Utc);

    private static bool TryParseMonth(string text, out int month)
    {
        month = 0;
        if (int.TryParse(text, out var numeric) && numeric is >= 1 and <= 12)
        {
            month = numeric;
            return true;
        }

        foreach (var culture in new[]
                 {
                     CultureInfo.InvariantCulture,
                     CultureInfo.GetCultureInfo("en-GB"),
                     CultureInfo.GetCultureInfo("en-US")
                 })
        {
            if (DateTime.TryParseExact(
                    text,
                    ["MMM", "MMMM"],
                    culture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out var dt))
            {
                month = dt.Month;
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^(?<month>[A-Za-z]{3,9})[ \-/]+(?<year>(?:19|20)\d{2})$", RegexOptions.IgnoreCase)]
    private static partial Regex MonthYear();

    [GeneratedRegex(@"^(?<year>(?:19|20)\d{2})[ \-/]+(?<month>[A-Za-z]{3,9})$", RegexOptions.IgnoreCase)]
    private static partial Regex YearMonth();

    [GeneratedRegex(@"\b(?:19|20)\d{2}\b")]
    private static partial Regex YearOnly();
}
