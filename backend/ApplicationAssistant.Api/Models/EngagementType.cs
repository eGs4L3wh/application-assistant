namespace ApplicationAssistant.Api.Models;

/// <summary>Known engagement types for work experience. Stored as strings so new values can be added later.</summary>
public static class EngagementType
{
    public const string Permanent = "Permanent";
    public const string Contract = "Contract";

    public static readonly IReadOnlyList<string> All = [Permanent, Contract];

    public static string Normalize(string? value, string fallback = Permanent)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var trimmed = value.Trim();
        foreach (var known in All)
        {
            if (known.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return known;
            }
        }

        // Allow forward-compatible values that aren't in All yet.
        return trimmed;
    }
}
