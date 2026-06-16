using System;
using System.Collections.Generic;
using Jellyfin.Data.Enums;

namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// One actionable entry in the todo list: something you're missing or could add.
/// Domain-agnostic: works for movies, shows, music, and books.
/// </summary>
public class GapItem
{
    /// <summary>
    /// Gets or sets a stable identifier for de-duplication.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the universal gap pattern.
    /// </summary>
    public GapPattern Pattern { get; set; }

    /// <summary>
    /// Gets the pattern as a stable string (for clients, avoids enum-serialization ambiguity).
    /// </summary>
    public string PatternName => Pattern.ToString();

    /// <summary>
    /// Gets or sets the media domain.
    /// </summary>
    public MediaDomain Domain { get; set; }

    /// <summary>
    /// Gets the domain as a stable string.
    /// </summary>
    public string DomainName => Domain.ToString();

    /// <summary>
    /// Gets or sets what the missing thing is, using Jellyfin's canonical item-kind enum
    /// (Movie, Series, Episode, MusicAlbum, Book, ...). Always set by the source via the factory.
    /// </summary>
    public BaseItemKind TargetKind { get; set; }

    /// <summary>
    /// Gets the target kind as a stable string for clients.
    /// </summary>
    public string TargetKindName => TargetKind.ToString();

    /// <summary>
    /// Gets or sets the title.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the release year, if known.
    /// </summary>
    public int? Year { get; set; }

    /// <summary>
    /// Gets or sets the release date, if known.
    /// </summary>
    public DateTime? ReleaseDate { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the title is announced but not yet released.
    /// </summary>
    public bool IsUpcoming { get; set; }

    /// <summary>
    /// Gets or sets a poster/image URL.
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Gets or sets a short overview.
    /// </summary>
    public string? Overview { get; set; }

    /// <summary>
    /// Gets or sets provider ids for matching/de-duplication (e.g. {"Tmdb":"603","Imdb":"tt0133093"}).
    /// </summary>
    public IReadOnlyDictionary<string, string> ProviderIds { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// Gets or sets external links (TMDB, JustWatch, Trakt, ...).
    /// </summary>
    public IReadOnlyList<ExternalLink> Links { get; set; } = Array.Empty<ExternalLink>();

    /// <summary>
    /// Gets or sets the id of the owned library item that surfaced this gap.
    /// </summary>
    public string? SourceItemId { get; set; }

    /// <summary>
    /// Gets or sets the name of the owned library item that surfaced this gap.
    /// </summary>
    public string? SourceItemName { get; set; }

    /// <summary>
    /// Gets or sets the type of the owning item ("BoxSet", "Series", "Person", "MusicArtist", ...).
    /// </summary>
    public string? SourceItemType { get; set; }

    /// <summary>
    /// Gets or sets the streaming-availability offers.
    /// </summary>
    public IReadOnlyList<AvailabilityOffer> Availability { get; set; } = Array.Empty<AvailabilityOffer>();
}
