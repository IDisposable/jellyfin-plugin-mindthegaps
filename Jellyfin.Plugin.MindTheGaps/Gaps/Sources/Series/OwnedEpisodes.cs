using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Series;

/// <summary>
/// The episodes a library owns for one series, indexed for the content diff. Beyond exact
/// (season, number) it carries air dates and folded titles, so an episode the library owns under a
/// different number is still recognized as owned rather than reported missing. This is what lets a
/// provider that numbers a season differently from the library's authority (a renumber, a reorder, or a
/// two-part episode the library merged into one file) stop reading as a wall of false gaps.
/// </summary>
internal sealed class OwnedEpisodes
{
    private readonly HashSet<(int Season, int Number)> _numbers = [];

    // Air dates are series-wide: an original air date pins an episode whatever season or number a provider
    // files it under, so a renumber or a re-season still matches.
    private readonly HashSet<DateOnly> _airDates = [];

    // Titles are season-scoped (a title like "Pilot" recurs across seasons) and part-marker folded, so a
    // provider's "X (2)" matches a library that owns the merged "X".
    private readonly HashSet<(int Season, string TitleKey)> _titles = [];

    /// <summary>Records that the library owns the given episode number within a season.</summary>
    /// <param name="season">The season number.</param>
    /// <param name="number">The episode number.</param>
    public void AddNumber(int season, int number) => _numbers.Add((season, number));

    /// <summary>Records an owned episode's original air date.</summary>
    /// <param name="airDate">The air date.</param>
    public void AddAirDate(DateTime airDate) => _airDates.Add(DateOnly.FromDateTime(airDate));

    /// <summary>Records an owned episode's title within a season.</summary>
    /// <param name="season">The season number.</param>
    /// <param name="title">The episode title, if any.</param>
    public void AddTitle(int season, string? title)
    {
        if (!string.IsNullOrEmpty(title))
        {
            _titles.Add((season, EpisodeTitleKey.Of(title)));
        }
    }

    /// <summary>
    /// Determines whether the library already holds the given canonical episode: by exact number, or
    /// (across a provider's different numbering) by the same air date or the same folded title.
    /// </summary>
    /// <param name="episode">The canonical episode from an external source.</param>
    /// <returns><see langword="true"/> when the library owns it under some numbering.</returns>
    public bool Owns(CanonicalEpisode episode)
    {
        if (_numbers.Contains((episode.Season, episode.Number)))
        {
            return true;
        }

        if (episode.ReleaseDate is { } date && _airDates.Contains(DateOnly.FromDateTime(date)))
        {
            return true;
        }

        return !string.IsNullOrEmpty(episode.Name)
            && _titles.Contains((episode.Season, EpisodeTitleKey.Of(episode.Name)));
    }
}
