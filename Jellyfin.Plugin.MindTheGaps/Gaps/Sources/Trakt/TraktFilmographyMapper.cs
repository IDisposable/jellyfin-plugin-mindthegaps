using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Trakt;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Trakt;

/// <summary>
/// Turns a Trakt person's movie credits into filmography gaps for the unowned titles. Emits the same
/// gap ids as the TMDB filmography source so the engine de-dupes; Trakt only adds what TMDB missed.
/// </summary>
public static class TraktFilmographyMapper
{
    /// <summary>
    /// Builds filmography gaps for a person's unowned Trakt movie credits, capped per person.
    /// </summary>
    /// <param name="credits">The Trakt person movie credits.</param>
    /// <param name="sourceItemId">The owned library person's id.</param>
    /// <param name="sourceItemName">The owned library person's name.</param>
    /// <param name="ownership">The library ownership index.</param>
    /// <returns>The filmography gaps.</returns>
    public static IEnumerable<GapItem> Build(
        TraktPersonMovieCredits credits,
        string sourceItemId,
        string? sourceItemName,
        OwnershipIndex ownership)
    {
        var emitted = 0;

        if (credits.Cast is not null)
        {
            foreach (var credit in credits.Cast)
            {
                if (emitted >= GapScanLimits.MaxCreditsPerPerson)
                {
                    break;
                }

                var gap = BuildGap(credit.Movie, sourceItemId, sourceItemName, ownership, string.IsNullOrEmpty(credit.Character) ? null : "as " + credit.Character);
                if (gap is not null)
                {
                    emitted++;
                    yield return gap;
                }
            }
        }

        foreach (var crewList in new[] { credits.Crew?.Directing, credits.Crew?.Writing })
        {
            if (crewList is null)
            {
                continue;
            }

            foreach (var credit in crewList)
            {
                if (emitted >= GapScanLimits.MaxCreditsPerPerson)
                {
                    break;
                }

                var gap = BuildGap(credit.Movie, sourceItemId, sourceItemName, ownership, credit.Job);
                if (gap is not null)
                {
                    emitted++;
                    yield return gap;
                }
            }
        }
    }

    private static GapItem? BuildGap(TraktMovie? movie, string sourceItemId, string? sourceItemName, OwnershipIndex ownership, string? role)
    {
        if (movie is null || string.IsNullOrEmpty(movie.Title))
        {
            return null;
        }

        var tmdbId = movie.Ids?.Tmdb;
        var imdbId = movie.Ids?.Imdb;

        var providerIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (tmdbId.HasValue)
        {
            providerIds[GapScanContext.TmdbProvider] = tmdbId.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrEmpty(imdbId))
        {
            providerIds["Imdb"] = imdbId;
        }

        if (providerIds.Count == 0)
        {
            return null;
        }

        if (ownership.OwnsAny(BaseItemKind.Movie, providerIds))
        {
            return null;
        }

        var id = tmdbId.HasValue
            ? string.Create(CultureInfo.InvariantCulture, $"filmography:movie:{tmdbId.Value}")
            : string.Create(CultureInfo.InvariantCulture, $"filmography:movie:imdb:{imdbId}");

        List<ExternalLink>? extraLinks = null;
        if (movie.Ids?.Slug is { Length: > 0 } slug)
        {
            extraLinks = new List<ExternalLink>
            {
                new ExternalLink("Trakt", string.Create(CultureInfo.InvariantCulture, $"https://trakt.tv/movies/{slug}"))
            };
        }

        return GapItemFactory.Create(
            id: id,
            pattern: GapPattern.CreatorWorks,
            domain: MediaDomain.Movies,
            targetKind: BaseItemKind.Movie,
            name: movie.Title,
            providerIds: providerIds,
            sourceItemId: sourceItemId,
            sourceItemName: sourceItemName,
            sourceItemType: "Person",
            releaseDate: movie.Year.HasValue ? new DateTime(movie.Year.Value, 1, 1) : null,
            overview: role,
            extraLinks: extraLinks);
    }
}
