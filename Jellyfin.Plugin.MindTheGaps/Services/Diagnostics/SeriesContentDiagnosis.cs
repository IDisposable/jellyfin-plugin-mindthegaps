using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Series;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps.Services.Diagnostics;

/// <summary>
/// The episode/season half of <see cref="GapDiagnostics"/>: given a missing-episode gap and what the
/// library owns for its series (the owning series' identity, the years of the owned and missing episodes,
/// and the owned episodes themselves), decides whether it looks like a genuine gap, an already-owned
/// episode under a different number, or content belonging to a same-named reboot the owning item is
/// mis-tagged as. Pure and standalone, like <see cref="TitleIdentityDiagnosis"/>: <see cref="GapDiagnostics"/>
/// is the only caller, and owns the library reads (the owning series and its owned/missing episode years)
/// that feed this.
/// </summary>
internal static class SeriesContentDiagnosis
{
    // Diagnose an episode or season gap against the owning series, the episode numbers you own for it, and the
    // years of the episodes you own (and are missing): the testable seam, no library load. An episode whose own
    // number is among the owned set is not missing at all (a stale gap, or a numbering the cross-check disagrees
    // on); the year heuristic cannot see that, so the owned numbers are checked first. For the rest, the owned
    // run expanded through the missing years into the series' episode era tells a genuine missing piece (within
    // that era) from content of a different same-named series (a reboot) the owning item is mis-tagged as. This
    // is the same era the library scan uses, so the popup and the report agree. The series' ids ride along as an
    // extra disambiguation the reader can check; the verdict does not depend on them.
    public static GapDiagnosis DiagnoseSeriesContentAgainst(
        GapItem gap,
        string? seriesName,
        int? seriesYear,
        IReadOnlyDictionary<string, string> seriesProviderIds,
        string? seriesJellyfinId,
        IReadOnlyList<int> ownedEpisodeYears,
        IReadOnlyList<int> missingEpisodeYears,
        IReadOnlyList<(int Season, int Number, string? Title, int Versions)> ownedEpisodes)
    {
        var name = string.IsNullOrEmpty(seriesName) ? "this series" : seriesName!;
        var noun = gap.TargetKind == BaseItemKind.Season ? "season" : "episode";

        var target = new DiagnosisItem
        {
            Relation = "target",
            Name = gap.Name,
            Year = gap.Year,
            ProviderIds = gap.ProviderIds,
            Note = "reported missing",
            Links = ProviderLinks.Build(gap.TargetKind, gap.ProviderIds)
        };

        var candidates = new List<DiagnosisItem>();
        if (!string.IsNullOrEmpty(seriesJellyfinId) || seriesProviderIds.Count > 0)
        {
            candidates.Add(new DiagnosisItem
            {
                Relation = "series",
                Name = name,
                Year = seriesYear,
                ProviderIds = seriesProviderIds,
                JellyfinItemId = seriesJellyfinId,
                Note = "the owning series",
                Links = ProviderLinks.Build(BaseItemKind.Series, seriesProviderIds)
            });
        }

        GapDiagnosis Result(DiagnosisReason reason, string summary) => new()
        {
            GapId = gap.Id,
            Summary = summary,
            Reason = reason,
            TargetKind = gap.TargetKind,
            Target = target,
            Candidates = candidates
        };

        // Identity check first: an episode whose own number is among the ones the library owns for this series
        // is not missing at all. The year comparison below cannot tell that apart from a genuine gap, so probe
        // the owned numbers directly. Only an episode gap carries a parseable season/number; a season gap falls
        // through to the year logic.
        string? episodeCode = null;
        if (gap.TargetKind == BaseItemKind.Episode && SeriesGapKey.TryParseEpisode(gap.Id, out var season, out var number))
        {
            episodeCode = string.Create(CultureInfo.InvariantCulture, $"S{season:D2}E{number:D2}");

            // The exact number is owned, so it is not missing (a stale gap, or a numbering the cross-check
            // disagrees on).
            if (ownedEpisodes.Any(e => e.Season == season && e.Number == number))
            {
                return Result(
                    DiagnosisReason.OwnedUnderWrongId,
                    string.Create(CultureInfo.InvariantCulture, $"{episodeCode} is among the episodes you own for '{name}', so it is not actually missing. The gap is most likely stale (rescan to clear it), or the cross-check source numbers this episode differently than your library."));
            }

            // The number is absent, but an episode with the same title (ignoring a part marker like "(2)" or
            // "Part 2") is owned at another number in the season: the content is present and the library numbers
            // it differently than the catalog (a two-part episode, or the pilot counted as one episode here and
            // two there), so this is a false gap rather than a missing one.
            var titleKey = EpisodeTitleKey.Of(EpisodeTitleOf(gap.Name));
            if (titleKey.Length > 0)
            {
                foreach (var owned in ownedEpisodes)
                {
                    if (owned.Season == season && owned.Number != number && EpisodeTitleKey.Of(owned.Title) == titleKey)
                    {
                        var ownedCode = string.Create(CultureInfo.InvariantCulture, $"S{owned.Season:D2}E{owned.Number:D2}");
                        var versions = owned.Versions > 1
                            ? string.Create(CultureInfo.InvariantCulture, $" (with {owned.Versions} versions)")
                            : string.Empty;
                        return Result(
                            DiagnosisReason.OwnedUnderWrongId,
                            string.Create(CultureInfo.InvariantCulture, $"{episodeCode} is not in your library by number, but you own an episode with the same title at {ownedCode}{versions}. Your library most likely numbers this episode differently than the catalog (a two-part episode, or the pilot counted as one episode here and two there); renumber {ownedCode} to match, or this stays a permanent false gap."));
                    }
                }
            }
        }

        // No dated episode to compare against. Split the old single "not enough dated content" message by what
        // it actually means: owning nothing on disk for the whole series is the structural footgun (an empty or
        // duplicate season folder), not a per-episode gap; owning episodes that simply lack air dates is a
        // metadata problem. Calling it "genuinely missing" with no detail is what hid the Highlander case, where
        // a duplicate "Season 1" and "Season 01" left the series with no episodes the diagnosis could see.
        if (ownedEpisodeYears.Count == 0)
        {
            if (ownedEpisodes.Count == 0)
            {
                return Result(
                    DiagnosisReason.NotOwned,
                    string.Create(CultureInfo.InvariantCulture, $"You own no episodes on disk for '{name}', so every episode reads as missing. That usually points to an empty or mis-structured season folder (for example a duplicate 'Season 1' and 'Season 01' where one holds only extras, so the episodes are split or hidden), not a real gap. Run the library audit to check this series' season folders, fix them, then rescan."));
            }

            var ownedSeasons = ownedEpisodes.Select(e => e.Season).Distinct().Count();
            var ownedCount = ownedEpisodes.Select(e => (e.Season, e.Number)).Distinct().Count();
            var subject = episodeCode ?? string.Create(CultureInfo.InvariantCulture, $"this {noun}");
            return Result(
                DiagnosisReason.NotOwned,
                string.Create(CultureInfo.InvariantCulture, $"You own {ownedCount} episode(s) across {ownedSeasons} season(s) of '{name}', but none carry an air date, so {subject} cannot be placed by year. It is not among the owned episodes by number or title either, so it looks like a genuine gap; refresh the series' metadata to restore air dates if that is wrong."));
        }

        if (gap.Year is not int airedYear)
        {
            return Result(
                DiagnosisReason.NotOwned,
                string.Create(CultureInfo.InvariantCulture, $"This {noun} carries no air date to place against the run of '{name}', and it is not among the episodes you own by number or title, so it looks like a genuine gap."));
        }

        // Expand the owned run through the series' missing-episode years into its real episode era, the same
        // way the library scan does, so an earlier or later season that bridges in episode by episode reads as
        // genuine and only a far-separated same-named reboot is flagged.
        var era = EpisodeEra.Expand((ownedEpisodeYears.Min(), ownedEpisodeYears.Max()), missingEpisodeYears);
        if (!EpisodeEra.IsOutside(airedYear, era))
        {
            var summary = episodeCode is null
                ? string.Create(CultureInfo.InvariantCulture, $"This {noun} aired {airedYear}, within the run of '{name}' ({era.Min} to {era.Max}), so it looks like a genuine missing {noun}.")
                : string.Create(CultureInfo.InvariantCulture, $"{episodeCode} is not among the episodes you own for '{name}', and it aired {airedYear}, within the run ({era.Min} to {era.Max}), so it is a genuine missing {noun}.");
            return Result(DiagnosisReason.NotOwned, summary);
        }

        var idHint = seriesProviderIds.Count > 0
            ? string.Create(CultureInfo.InvariantCulture, $" The series carries {DescribeIds(seriesProviderIds)}; confirm it points to the {era.Min}-{era.Max} series, not a {airedYear} one.")
            : " The series carries no external id to confirm against.";

        return Result(
            DiagnosisReason.OwnedUnderWrongId,
            string.Create(CultureInfo.InvariantCulture, $"This {noun} aired {airedYear}, but the run of '{name}' spans {era.Min} to {era.Max} with nothing bridging to {airedYear}, so it is almost certainly a different, same-named series (a reboot).{idHint}"));
    }

    // The owning series' external ids in a readable form, for the episode/season verdict's id hint.
    private static string DescribeIds(IReadOnlyDictionary<string, string> ids)
    {
        var parts = new List<string>();
        foreach (var (provider, label) in new[] { (ProviderIds.Tmdb, "TheMovieDb"), (ProviderIds.Tvdb, "TheTVDB"), (ProviderIds.Imdb, "IMDb"), (ProviderIds.TVmaze, "TVmaze") })
        {
            if (ids.TryGetValue(provider, out var value) && !string.IsNullOrEmpty(value))
            {
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"{label} {value}"));
            }
        }

        return parts.Count > 0 ? string.Join(", ", parts) : "no external ids";
    }

    // The bare episode title out of a series-content gap name, which the gap builds as "{series} {code} - {title}".
    private static string EpisodeTitleOf(string gapName)
    {
        var dash = gapName.IndexOf(" - ", StringComparison.Ordinal);
        return dash >= 0 ? gapName[(dash + 3)..] : gapName;
    }
}
