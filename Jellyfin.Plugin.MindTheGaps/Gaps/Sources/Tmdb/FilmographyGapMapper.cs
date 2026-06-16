using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using TmdbPerson = TMDbLib.Objects.People.Person;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Tmdb;

/// <summary>
/// Turns a TMDB person's movie credits into gaps for the unowned titles (acting roles plus
/// directing/writing crew).
/// </summary>
public static class FilmographyGapMapper
{
    // Filter crew credits by department, not by individual job strings: TMDB's job names are a large,
    // evolving free-text taxonomy (Writing alone = Screenplay/Story/Novel/Characters/...), whereas the
    // department set is small and stable. Authoritative list: TMDB GET /configuration/jobs
    // (https://developer.themoviedb.org/reference/configuration-jobs).
    //
    // Jellyfin encodes the same mapping in MediaBrowser.Providers TmdbUtils.MapCrewToPersonType /
    // WantedCrewKinds, but that is typed to TMDbLib's Crew (a movie's credits), not the MovieJob we
    // get from person filmography, so it isn't directly callable here. This set is narrower than
    // WantedCrewKinds (which also adds "Production"/Producer); add "Production" here to include
    // producer credits in filmography.
    private static readonly HashSet<string> _crewDepartments = new(StringComparer.OrdinalIgnoreCase)
    {
        "Directing", "Writing"
    };

    /// <summary>
    /// Builds filmography gaps for a person's unowned movie credits, capped per person.
    /// </summary>
    /// <param name="person">The TMDB person (with movie credits populated).</param>
    /// <param name="sourceItemId">The owned library person's id.</param>
    /// <param name="sourceItemName">The owned library person's name.</param>
    /// <param name="ownership">The library ownership index.</param>
    /// <param name="posterUrl">Resolves a TMDB poster path to a URL.</param>
    /// <returns>The filmography gaps.</returns>
    public static IEnumerable<GapItem> Build(
        TmdbPerson person,
        string sourceItemId,
        string? sourceItemName,
        OwnershipIndex ownership,
        Func<string?, string?> posterUrl)
    {
        var emitted = 0;
        var credits = person.MovieCredits;

        if (credits?.Cast is not null)
        {
            foreach (var role in credits.Cast)
            {
                if (emitted >= GapScanLimits.MaxCreditsPerPerson)
                {
                    break;
                }

                var gap = BuildGap(
                    role.Id,
                    role.Title,
                    role.ReleaseDate,
                    role.PosterPath,
                    string.IsNullOrEmpty(role.Character) ? null : "as " + role.Character,
                    sourceItemId,
                    sourceItemName,
                    ownership,
                    posterUrl);
                if (gap is not null)
                {
                    emitted++;
                    yield return gap;
                }
            }
        }

        if (credits?.Crew is not null)
        {
            foreach (var job in credits.Crew)
            {
                if (emitted >= GapScanLimits.MaxCreditsPerPerson)
                {
                    break;
                }

                if (string.IsNullOrEmpty(job.Department) || !_crewDepartments.Contains(job.Department))
                {
                    continue;
                }

                var gap = BuildGap(job.Id, job.Title, job.ReleaseDate, job.PosterPath, job.Job, sourceItemId, sourceItemName, ownership, posterUrl);
                if (gap is not null)
                {
                    emitted++;
                    yield return gap;
                }
            }
        }
    }

    private static GapItem? BuildGap(
        int tmdbId,
        string? title,
        DateTime? releaseDate,
        string? posterPath,
        string? role,
        string sourceItemId,
        string? sourceItemName,
        OwnershipIndex ownership,
        Func<string?, string?> posterUrl)
    {
        if (string.IsNullOrEmpty(title))
        {
            return null;
        }

        var providerIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [GapScanContext.TmdbProvider] = tmdbId.ToString(CultureInfo.InvariantCulture)
        };

        if (ownership.OwnsAny(BaseItemKind.Movie, providerIds))
        {
            return null;
        }

        return GapItemFactory.Create(
            id: string.Create(CultureInfo.InvariantCulture, $"filmography:movie:{tmdbId}"),
            pattern: GapPattern.CreatorWorks,
            domain: MediaDomain.Video,
            targetKind: BaseItemKind.Movie,
            name: title,
            providerIds: providerIds,
            sourceItemId: sourceItemId,
            sourceItemName: sourceItemName,
            sourceItemType: "Person",
            releaseDate: releaseDate,
            imageUrl: posterUrl(posterPath),
            overview: role);
    }
}
