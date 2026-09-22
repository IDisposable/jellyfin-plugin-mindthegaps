using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// One title on the fulfillment queue: every user's todo entry about the same title, folded into one row so
/// an administrator without a Radarr/Sonarr/Jellyseerr setup can see what is actually in demand and go find
/// it themselves, then close it out for everyone who asked in one action.
/// </summary>
public sealed class TodoDemandRow
{
    /// <summary>
    /// Gets or sets a stable id for the row (the lowest of its member entries' ids, ordinally), so the
    /// dashboard has a row key that does not change between loads.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the title.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the release year, if known.
    /// </summary>
    public int? Year { get; set; }

    /// <summary>
    /// Gets or sets the media domain name (Movies, Shows, Music, Books).
    /// </summary>
    public string DomainName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target item-kind name (Movie, Series, Episode, MusicAlbum, Book, ...).
    /// </summary>
    public string TargetKindName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the gap pattern name of the entry the row's shared fields were taken from.
    /// </summary>
    public string PatternName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the creator/source name of the entry the row's shared fields were taken from.
    /// </summary>
    public string? Creator { get; set; }

    /// <summary>
    /// Gets or sets the poster URL, if any member entry carried one.
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Gets or sets the release date, if known.
    /// </summary>
    public DateTime? ReleaseDate { get; set; }

    /// <summary>
    /// Gets or sets the provider ids, unioned across every member entry (one requester's copy may carry an
    /// id another's does not).
    /// </summary>
    public IReadOnlyDictionary<string, string> ProviderIds { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// Gets or sets the external links, unioned across every member entry and de-duplicated by URL.
    /// </summary>
    public IReadOnlyList<ExternalLink> Links { get; set; } = [];

    /// <summary>
    /// Gets or sets how many distinct users have this title on their list.
    /// </summary>
    public int RequestCount { get; set; }

    /// <summary>
    /// Gets or sets how many of those users have not yet marked it done.
    /// </summary>
    public int OpenCount { get; set; }

    /// <summary>
    /// Gets or sets the names of the requesting users, alphabetical.
    /// </summary>
    public IReadOnlyList<string> RequestedBy { get; set; } = [];

    /// <summary>
    /// Gets or sets every member entry's owner and entry id, so marking the row fetched can flip each one.
    /// </summary>
    public IReadOnlyList<TodoDemandEntryRef> Entries { get; set; } = [];
}
