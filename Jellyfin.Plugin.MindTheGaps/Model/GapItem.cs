using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
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
    /// Gets or sets the season number for episode gaps (0 is specials), used to group episodes by season.
    /// </summary>
    public int? Season { get; set; }

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
    /// Gets or sets the release year of the owning item, when known.
    /// </summary>
    public int? SourceItemYear { get; set; }

    /// <summary>
    /// Gets or sets additional owned items that surfaced this same gap, beyond the primary one in
    /// <see cref="SourceItemName"/>. The engine accumulates these when the same recommendation target is
    /// surfaced by several owned titles, so the report can list every recommending source. Null (omitted
    /// from the JSON) for the common case of a single source.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<GapSourceRef>? OtherSources { get; set; }

    /// <summary>
    /// Gets or sets how many members of this gap's set the library already owns, for a SetCompletion gap
    /// (a collection/studio/keyword set). Null (and omitted) for gaps that are not part of a counted set.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SetOwnedCount { get; set; }

    /// <summary>
    /// Gets or sets the total number of members in this gap's set, for a SetCompletion gap. Null (and
    /// omitted) for gaps that are not part of a counted set.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SetTotalCount { get; set; }

    /// <summary>
    /// Gets or sets a popularity score (TMDB popularity) used by the report's optional "sort by
    /// popularity". Null (and omitted) for gaps from sources that carry no such signal.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? SortScore { get; set; }

    /// <summary>
    /// Gets or sets the id of this gap's own item in the library, when it exists as a (virtual) item the
    /// server already tracks (for example a missing episode). Lets the report link straight to it.
    /// </summary>
    public string? LibraryItemId { get; set; }

    /// <summary>
    /// Gets or sets the id of this gap's season in the library, when known (episode gaps from the server's
    /// own missing-episode list). Lets the report link the season header to it.
    /// </summary>
    public string? SeasonItemId { get; set; }

    /// <summary>
    /// Gets or sets the TMDB id to use for "where to watch" when the gap itself is not directly
    /// watchable: an episode gap carries its owning series' TMDB id here so the row can link to where
    /// the show streams. Null for Movie/Series gaps, which use their own TMDB id.
    /// </summary>
    public string? WatchTmdbId { get; set; }

    /// <summary>
    /// Gets or sets the streaming-availability offers.
    /// </summary>
    public IReadOnlyList<AvailabilityOffer> Availability { get; set; } = Array.Empty<AvailabilityOffer>();

    /// <summary>
    /// Gets or sets a value indicating whether "where to watch" has been looked up for this gap (so an
    /// empty <see cref="Availability"/> means "checked, no sources" rather than "not looked up yet").
    /// </summary>
    public bool AvailabilityChecked { get; set; }
}
