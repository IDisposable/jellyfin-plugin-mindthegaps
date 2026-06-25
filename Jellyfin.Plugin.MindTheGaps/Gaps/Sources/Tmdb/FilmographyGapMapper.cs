using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using TmdbPerson = TMDbLib.Objects.People.Person;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Tmdb;

/// <summary>
/// Turns a TMDB person's movie and TV credits into gaps for the unowned titles (acting roles plus
/// directing/writing crew). Movie credits become Movies/Movie gaps and TV credits become Shows/Series
/// gaps, both under the Creator works pattern.
/// </summary>
internal static class FilmographyGapMapper
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
    /// Builds filmography gaps for a person's unowned movie and TV credits, capped per person.
    /// </summary>
    /// <param name="person">The TMDB person (with movie and TV credits populated).</param>
    /// <param name="sourceItemId">The owned library person's id.</param>
    /// <param name="sourceItemName">The owned library person's name.</param>
    /// <param name="ownership">The library ownership index.</param>
    /// <param name="posterUrl">Resolves a TMDB poster path to a URL.</param>
    /// <param name="minVotes">The minimum TMDB vote count a credit must have to be kept (0 disables).</param>
    /// <param name="maxCastOrder">The deepest cast billing order to keep (0 disables).</param>
    /// <returns>The filmography gaps.</returns>
    /// <remarks>
    /// The relevance gate keeps the list actionable for a large library, where the noise is cast roles: a
    /// cast credit is kept only if its TMDB vote count is at least <paramref name="minVotes"/> (which drops
    /// obscure and unreleased titles) and it is billed no deeper than <paramref name="maxCastOrder"/> (a
    /// minor appearance is not really the person's work). Directing/writing crew is not gated: TMDB's
    /// filmography crew entries carry no vote count, and those credits are few and inherently key works.
    /// </remarks>
    public static IEnumerable<GapItem> Build(
        TmdbPerson person,
        string sourceItemId,
        string? sourceItemName,
        OwnershipIndex ownership,
        Func<string?, string?> posterUrl,
        int minVotes,
        int maxCastOrder)
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

                if (!MeetsVotes(role.VoteCount, minVotes) || (maxCastOrder > 0 && role.Order > maxCastOrder))
                {
                    continue;
                }

                var gap = BuildGap(
                    role.Id,
                    role.Title,
                    role.ReleaseDate,
                    role.PosterPath,
                    string.IsNullOrEmpty(role.Character) ? null : "as " + role.Character,
                    role.Popularity,
                    sourceItemId,
                    sourceItemName,
                    person.Id,
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

                var gap = BuildGap(job.Id, job.Title, job.ReleaseDate, job.PosterPath, job.Job, null, sourceItemId, sourceItemName, person.Id, ownership, posterUrl);
                if (gap is not null)
                {
                    emitted++;
                    yield return gap;
                }
            }
        }

        var tvCredits = person.TvCredits;

        if (tvCredits?.Cast is not null)
        {
            foreach (var role in tvCredits.Cast)
            {
                if (emitted >= GapScanLimits.MaxCreditsPerPerson)
                {
                    break;
                }

                // TV filmography roles carry no vote count or billing order, so the same relevance gates are
                // applied with the zero those fields default to: a positive vote floor drops TV cast roles
                // (vote count 0), matching how the same floor trims obscure movie cast roles, while the
                // cast-billing limit never drops a TV role (billing order 0 is never deeper than any limit).
                if (!MeetsVotes(0, minVotes))
                {
                    continue;
                }

                var gap = BuildSeriesGap(
                    role.Id,
                    role.Name,
                    role.FirstAirDate,
                    role.PosterPath,
                    string.IsNullOrEmpty(role.Character) ? null : "as " + role.Character,
                    sourceItemId,
                    sourceItemName,
                    person.Id,
                    ownership,
                    posterUrl);
                if (gap is not null)
                {
                    emitted++;
                    yield return gap;
                }
            }
        }

        if (tvCredits?.Crew is not null)
        {
            foreach (var job in tvCredits.Crew)
            {
                if (emitted >= GapScanLimits.MaxCreditsPerPerson)
                {
                    break;
                }

                if (string.IsNullOrEmpty(job.Department) || !_crewDepartments.Contains(job.Department))
                {
                    continue;
                }

                var gap = BuildSeriesGap(job.Id, job.Name, job.FirstAirDate, job.PosterPath, job.Job, sourceItemId, sourceItemName, person.Id, ownership, posterUrl);
                if (gap is not null)
                {
                    emitted++;
                    yield return gap;
                }
            }
        }
    }

    // A credit clears the relevance floor when the gate is off (min <= 0) or it has enough TMDB votes.
    private static bool MeetsVotes(int voteCount, int minVotes) => minVotes <= 0 || voteCount >= minVotes;

    private static GapItem? BuildGap(
        int tmdbId,
        string? title,
        DateTime? releaseDate,
        string? posterPath,
        string? role,
        double? popularity,
        string sourceItemId,
        string? sourceItemName,
        int sourcePersonTmdbId,
        OwnershipIndex ownership,
        Func<string?, string?> posterUrl)
    {
        if (string.IsNullOrEmpty(title))
        {
            return null;
        }

        var providerIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ProviderIds.Tmdb] = tmdbId.ToString(CultureInfo.InvariantCulture)
        };

        if (ownership.OwnsAny(BaseItemKind.Movie, providerIds))
        {
            return null;
        }

        return GapItemFactory.Create(
            id: string.Create(CultureInfo.InvariantCulture, $"filmography:movie:{tmdbId}"),
            pattern: GapPattern.CreatorWorks,
            domain: MediaDomain.Movies,
            targetKind: BaseItemKind.Movie,
            name: title,
            providerIds: providerIds,
            sourceItemId: sourceItemId,
            sourceItemName: sourceItemName,
            sourceItemType: "Person",
            sourceProviderIds: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [ProviderIds.Tmdb] = sourcePersonTmdbId.ToString(CultureInfo.InvariantCulture) },
            releaseDate: releaseDate,
            imageUrl: posterUrl(posterPath),
            overview: role,
            sortScore: popularity);
    }

    private static GapItem? BuildSeriesGap(
        int tmdbId,
        string? title,
        DateTime? firstAirDate,
        string? posterPath,
        string? role,
        string sourceItemId,
        string? sourceItemName,
        int sourcePersonTmdbId,
        OwnershipIndex ownership,
        Func<string?, string?> posterUrl)
    {
        if (string.IsNullOrEmpty(title))
        {
            return null;
        }

        // The TV filmography response carries only the series' TMDB id; the owned-series index also holds
        // each series' TheTVDB id, so ownership matches whichever id the library tagged the series with.
        var providerIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ProviderIds.Tmdb] = tmdbId.ToString(CultureInfo.InvariantCulture)
        };

        if (ownership.OwnsAny(BaseItemKind.Series, providerIds))
        {
            return null;
        }

        return GapItemFactory.Create(
            id: string.Create(CultureInfo.InvariantCulture, $"filmography:series:{tmdbId}"),
            pattern: GapPattern.CreatorWorks,
            domain: MediaDomain.Shows,
            targetKind: BaseItemKind.Series,
            name: title,
            providerIds: providerIds,
            sourceItemId: sourceItemId,
            sourceItemName: sourceItemName,
            sourceItemType: "Person",
            sourceProviderIds: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [ProviderIds.Tmdb] = sourcePersonTmdbId.ToString(CultureInfo.InvariantCulture) },
            releaseDate: firstAirDate,
            imageUrl: posterUrl(posterPath),
            overview: role);
    }
}
