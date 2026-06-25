using System;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Series;

/// <summary>
/// A normalized episode from an external show database (TVmaze, TheTVDB), reduced to the fields the gap
/// diff needs. Only regular numbered episodes are represented.
/// </summary>
/// <param name="Season">The season number.</param>
/// <param name="Number">The episode number within the season.</param>
/// <param name="Name">The episode title, if any.</param>
/// <param name="ReleaseDate">The original air date, if known.</param>
/// <param name="Overview">A short overview, if any.</param>
internal sealed record CanonicalEpisode(int Season, int Number, string? Name, DateTime? ReleaseDate, string? Overview);
