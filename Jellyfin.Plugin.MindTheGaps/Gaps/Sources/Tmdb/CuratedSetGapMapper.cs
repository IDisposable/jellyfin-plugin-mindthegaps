using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using TMDbLib.Objects.Search;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Tmdb;

/// <summary>
/// Turns a TMDB discover result for a curated set (a studio/company or a keyword) into SetCompletion
/// gaps for the movies in that set the library does not own. Widens SetCompletion beyond formal TMDB
/// BoxSets to broader sets like "every A24 film" or "every Studio Ghibli film".
/// </summary>
internal static class CuratedSetGapMapper
{
    /// <summary>
    /// Builds movie gaps for the unowned members of a curated set.
    /// </summary>
    /// <param name="results">The discover results for the set.</param>
    /// <param name="setKey">A stable key for the set, for example "company:41077" or "keyword:9715".</param>
    /// <param name="setLabel">A human label for the set, for example "A24".</param>
    /// <param name="setType">The set type label, for example "Studio" or "Keyword".</param>
    /// <param name="ownership">The library ownership index.</param>
    /// <param name="posterUrl">Resolves a TMDB poster path to a URL.</param>
    /// <param name="perSet">The maximum gaps to emit for this set.</param>
    /// <param name="pattern">The gap pattern: SetCompletion for a curated studio/keyword set; Recommendation
    /// for a discovery list (a TMDB list), which the dashboard shows under the discover tab.</param>
    /// <param name="sourceItemId">A stable per-source id (so a discovery list can be dismissed on its own);
    /// empty for the studio/keyword sets, which group by name and type instead.</param>
    /// <param name="gapPrefix">The gap-id prefix; defaults to <see cref="GapSourceKeys.Curated"/>'s for a
    /// studio/keyword/TMDB-list set. A caller with its own <see cref="GapSourceKey"/> (a source that is not
    /// itself a curated set, such as a TMDB discover feed) passes its own prefix instead.</param>
    /// <returns>The gaps for the set's unowned members.</returns>
    public static IEnumerable<GapItem> BuildMovies(
        IEnumerable<SearchMovie> results,
        string setKey,
        string setLabel,
        string setType,
        OwnershipIndex ownership,
        Func<string?, string?> posterUrl,
        int perSet,
        GapPattern pattern = GapPattern.SetCompletion,
        string? sourceItemId = null,
        string? gapPrefix = null)
    {
        var prefix = gapPrefix ?? GapSourceKeys.Curated.GapPrefix;

        // The set's own TMDB id, when setKey carries one ("company:41077" -> "41077"), links the set
        // itself (not just its members) to TMDB. A caller whose setKey has no such id (a discover feed
        // has no TMDB page of its own to link to) gets no source link, rather than a wrong one.
        var colonIndex = setKey.LastIndexOf(':');
        var sourceProviderIds = colonIndex < 0
            ? null
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [ProviderIds.Tmdb] = setKey[(colonIndex + 1)..] };

        var emitted = 0;
        foreach (var movie in results)
        {
            if (emitted >= perSet)
            {
                break;
            }

            if (string.IsNullOrEmpty(movie.Title))
            {
                continue;
            }

            var providerIds = TmdbId(movie.Id);
            if (ownership.OwnsAny(BaseItemKind.Movie, providerIds))
            {
                continue;
            }

            var year = movie.ReleaseDate?.Year;
            emitted++;
            yield return GapItemFactory.Create(
                id: string.Create(CultureInfo.InvariantCulture, $"{prefix}{setKey}:{movie.Id}"),
                pattern: pattern,
                domain: MediaDomain.Movies,
                targetKind: BaseItemKind.Movie,
                name: movie.Title,
                providerIds: providerIds,
                sourceItemId: sourceItemId ?? string.Empty,
                sourceItemName: setLabel,
                sourceItemType: setType,
                sourceProviderIds: sourceProviderIds,
                releaseDate: movie.ReleaseDate,
                imageUrl: posterUrl(movie.PosterPath),
                overview: movie.Overview,
                sourceItemYear: year,
                sortScore: movie.Popularity);
        }
    }

    private static Dictionary<string, string> TmdbId(int id)
        => new(StringComparer.OrdinalIgnoreCase)
        {
            [ProviderIds.Tmdb] = id.ToString(CultureInfo.InvariantCulture)
        };
}
