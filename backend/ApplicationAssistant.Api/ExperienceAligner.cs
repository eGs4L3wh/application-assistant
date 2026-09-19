using ApplicationAssistant.AI;
using ApplicationAssistant.Api.Models;

namespace ApplicationAssistant.Api;

public static class ExperienceAligner
{
    private static readonly TimeSpan MaxAllowedGap = TimeSpan.FromDays(62); // ~2 months

    public sealed class AlignedExperience
    {
        public Guid Id { get; init; }
        public string Company { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string? Location { get; init; }
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public bool IsCurrent { get; set; }
        public double RelevanceScore { get; init; }
        public List<string> Bullets { get; init; } = [];
    }

    public static List<AlignedExperience> Align(
        IReadOnlyList<WorkExperience> source,
        IReadOnlyList<CvDraftExperience> draft)
    {
        var sourceById = source.ToDictionary(e => e.Id);
        var now = DateTime.UtcNow.Date;

        var candidates = draft
            .Where(d => sourceById.ContainsKey(d.Id))
            .Select(d =>
            {
                var src = sourceById[d.Id];
                var start = (src.StartDate ?? src.EndDate ?? now).Date;
                var end = src.IsCurrent || src.EndDate is null ? (DateTime?)null : src.EndDate.Value.Date;
                if (end is not null && end < start)
                {
                    end = start;
                }

                return new AlignedExperience
                {
                    Id = src.Id,
                    Company = string.IsNullOrWhiteSpace(d.Company) ? src.Company : d.Company,
                    Title = string.IsNullOrWhiteSpace(d.Title) ? src.Title : d.Title,
                    Location = d.Location ?? src.Location,
                    StartDate = start,
                    EndDate = end,
                    IsCurrent = src.IsCurrent || end is null,
                    RelevanceScore = d.RelevanceScore,
                    Bullets = d.Bullets.Count > 0 ? d.Bullets : ["Contributed to delivery and outcomes."]
                };
            })
            .OrderByDescending(e => e.RelevanceScore)
            .ThenByDescending(e => e.IsCurrent)
            .ThenByDescending(e => e.StartDate)
            .ToList();

        // Start from LLM include flags, then restore gap fillers.
        var keptIds = new HashSet<Guid>(
            draft.Where(d => d.Include && sourceById.ContainsKey(d.Id)).Select(d => d.Id));

        EnsureNoLargeGaps(candidates, keptIds, now);

        var kept = candidates
            .Where(c => keptIds.Contains(c.Id))
            .Select(Clone)
            .OrderBy(e => e.StartDate)
            .ThenByDescending(e => e.RelevanceScore)
            .ToList();

        ResolveOverlaps(kept, now);

        return kept
            .OrderByDescending(e => e.StartDate)
            .ThenByDescending(e => e.RelevanceScore)
            .ToList();
    }

    private static void EnsureNoLargeGaps(
        List<AlignedExperience> candidates,
        HashSet<Guid> keptIds,
        DateTime now)
    {
        // Iteratively add back lowest-cost gap fillers until timeline gaps are acceptable.
        for (var pass = 0; pass < candidates.Count + 2; pass++)
        {
            var timeline = candidates
                .Where(c => keptIds.Contains(c.Id))
                .OrderBy(c => c.StartDate)
                .ToList();

            if (timeline.Count == 0)
            {
                // Keep at least the most relevant role.
                var best = candidates.FirstOrDefault();
                if (best is not null)
                {
                    keptIds.Add(best.Id);
                }

                continue;
            }

            var gap = FindLargestIllegalGap(timeline, now);
            if (gap is null)
            {
                return;
            }

            var filler = candidates
                .Where(c => !keptIds.Contains(c.Id))
                .Where(c => OverlapsWindow(c, gap.Value.GapStart, gap.Value.GapEnd, now))
                .OrderByDescending(c => c.RelevanceScore)
                .ThenByDescending(c => Duration(c, now))
                .FirstOrDefault();

            if (filler is null)
            {
                // No role covers the gap; stop.
                return;
            }

            keptIds.Add(filler.Id);
        }
    }

    private static (DateTime GapStart, DateTime GapEnd)? FindLargestIllegalGap(
        List<AlignedExperience> timeline,
        DateTime now)
    {
        (DateTime GapStart, DateTime GapEnd)? worst = null;
        TimeSpan worstSize = TimeSpan.Zero;

        for (var i = 0; i < timeline.Count - 1; i++)
        {
            var leftEnd = EffectiveEnd(timeline[i], now);
            var rightStart = timeline[i + 1].StartDate;
            if (rightStart <= leftEnd.AddDays(1))
            {
                continue;
            }

            var size = rightStart - leftEnd;
            if (size > MaxAllowedGap && size > worstSize)
            {
                worstSize = size;
                worst = (leftEnd.AddDays(1), rightStart.AddDays(-1));
            }
        }

        // Gap from last role to now (career continuity into present).
        var last = timeline[^1];
        var lastEnd = EffectiveEnd(last, now);
        if (!last.IsCurrent && now - lastEnd > MaxAllowedGap)
        {
            var size = now - lastEnd;
            if (size > worstSize)
            {
                worst = (lastEnd.AddDays(1), now);
            }
        }

        return worst;
    }

    private static bool OverlapsWindow(AlignedExperience role, DateTime windowStart, DateTime windowEnd, DateTime now)
    {
        var roleEnd = EffectiveEnd(role, now);
        return role.StartDate <= windowEnd && roleEnd >= windowStart;
    }

    private static void ResolveOverlaps(List<AlignedExperience> kept, DateTime now)
    {
        // Process from highest relevance so protected roles stay long.
        var byRelevance = kept
            .OrderByDescending(e => e.RelevanceScore)
            .ThenByDescending(e => e.IsCurrent)
            .ThenByDescending(e => e.StartDate)
            .ToList();

        for (var i = 0; i < byRelevance.Count; i++)
        {
            var protectedRole = byRelevance[i];
            for (var j = i + 1; j < byRelevance.Count; j++)
            {
                var other = byRelevance[j];
                if (!Overlaps(protectedRole, other, now))
                {
                    continue;
                }

                ShrinkToAvoid(other, protectedRole, now);
            }
        }

        // Final chronological sweep: if anything still overlaps, shrink the lower-relevance one.
        kept.Sort((a, b) => a.StartDate.CompareTo(b.StartDate));
        for (var i = 0; i < kept.Count; i++)
        {
            for (var j = i + 1; j < kept.Count; j++)
            {
                if (!Overlaps(kept[i], kept[j], now))
                {
                    continue;
                }

                var shrink = kept[i].RelevanceScore >= kept[j].RelevanceScore ? kept[j] : kept[i];
                var protect = ReferenceEquals(shrink, kept[i]) ? kept[j] : kept[i];
                ShrinkToAvoid(shrink, protect, now);
            }
        }

        // Drop zero/negative length roles that couldn't be salvaged.
        kept.RemoveAll(e =>
        {
            var end = EffectiveEnd(e, now);
            return end < e.StartDate;
        });
    }

    private static void ShrinkToAvoid(AlignedExperience shrink, AlignedExperience protect, DateTime now)
    {
        if (!Overlaps(shrink, protect, now))
        {
            return;
        }

        var protectStart = protect.StartDate;
        var protectEnd = EffectiveEnd(protect, now);
        var shrinkEnd = EffectiveEnd(shrink, now);

        // Prefer moving end date earlier.
        if (shrink.StartDate < protectStart)
        {
            var newEnd = protectStart.AddDays(-1);
            if (newEnd >= shrink.StartDate)
            {
                shrink.EndDate = newEnd;
                shrink.IsCurrent = false;
                if (!Overlaps(shrink, protect, now))
                {
                    return;
                }
            }
        }

        // Otherwise move start later (after protected end).
        var newStart = protectEnd.AddDays(1);
        if (newStart <= shrinkEnd)
        {
            shrink.StartDate = newStart;
            if (shrink.EndDate is not null && shrink.EndDate < shrink.StartDate)
            {
                shrink.EndDate = shrink.StartDate;
            }

            if (shrink.IsCurrent && shrink.EndDate is not null)
            {
                shrink.IsCurrent = false;
            }
        }
        else
        {
            // Cannot fit; collapse to invalid so cleanup removes it.
            shrink.EndDate = shrink.StartDate.AddDays(-1);
            shrink.IsCurrent = false;
        }
    }

    private static bool Overlaps(AlignedExperience a, AlignedExperience b, DateTime now)
    {
        var aEnd = EffectiveEnd(a, now);
        var bEnd = EffectiveEnd(b, now);
        return a.StartDate <= bEnd && b.StartDate <= aEnd;
    }

    private static DateTime EffectiveEnd(AlignedExperience role, DateTime now) =>
        role.IsCurrent || role.EndDate is null ? now : role.EndDate.Value;

    private static TimeSpan Duration(AlignedExperience role, DateTime now) =>
        EffectiveEnd(role, now) - role.StartDate;

    private static AlignedExperience Clone(AlignedExperience source) => new()
    {
        Id = source.Id,
        Company = source.Company,
        Title = source.Title,
        Location = source.Location,
        StartDate = source.StartDate,
        EndDate = source.EndDate,
        IsCurrent = source.IsCurrent,
        RelevanceScore = source.RelevanceScore,
        Bullets = [.. source.Bullets]
    };
}
