using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// A gap as the report list needs it: enough to group, filter, sort and draw a collapsed row, and nothing
/// that only an opened row shows. A tab holds thousands of these and the browser parses every one, so what
/// is left off (the overview, the external links, each offer's deeplink, and the source links repeated on
/// every member of a set) is fetched per row on demand as a full <see cref="GapItem"/>. Every property is
/// omitted from the JSON when empty, since most rows leave most of them empty.
/// </summary>
public class GapRow
{
    /// <summary>
    /// Gets or sets the stable gap id.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the gap pattern name, or null when every row in the report shares one (see
    /// <see cref="GapRowReport.PatternName"/>).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PatternName { get; set; }

    /// <summary>
    /// Gets or sets the media domain name, or null when every row in the report shares one (see
    /// <see cref="GapRowReport.DomainName"/>).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DomainName { get; set; }

    /// <summary>
    /// Gets or sets the index of this gap's target kind in <see cref="GapRowReport.TargetKinds"/>.
    /// </summary>
    public int TargetKindRef { get; set; }

    /// <summary>
    /// Gets or sets the title.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the release year, if known.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Year { get; set; }

    /// <summary>
    /// Gets or sets the season number for an episode gap.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Season { get; set; }

    /// <summary>
    /// Gets or sets the release date, if known.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? ReleaseDate { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the title is announced but not yet released.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsUpcoming { get; set; }

    /// <summary>
    /// Gets or sets the poster/image URL.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the gap has an overview, which the row does not carry.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool HasOverview { get; set; }

    /// <summary>
    /// Gets or sets the provider ids. Small, and the row needs them to decide whether it can be minted or
    /// looked up.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? ProviderIds { get; set; }

    /// <summary>
    /// Gets or sets the id of the owned item that surfaced this gap.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceItemId { get; set; }

    /// <summary>
    /// Gets or sets the name of the owned item that surfaced this gap.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceItemName { get; set; }

    /// <summary>
    /// Gets or sets the type of the owning item.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceItemType { get; set; }

    /// <summary>
    /// Gets or sets the release year of the owning item.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SourceItemYear { get; set; }

    /// <summary>
    /// Gets or sets the index into <see cref="GapRowReport.SourceLinkSets"/> of this gap's source links, so a
    /// set with hundreds of gaps sends its links once. Null when the source has none.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SourceLinksRef { get; set; }

    /// <summary>
    /// Gets or sets the other owned items that surfaced this same gap.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<GapSourceRef>? OtherSources { get; set; }

    /// <summary>
    /// Gets or sets how many members of this gap's set the library already owns.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SetOwnedCount { get; set; }

    /// <summary>
    /// Gets or sets the total number of members in this gap's set.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SetTotalCount { get; set; }

    /// <summary>
    /// Gets or sets the popularity score used by the report's optional popularity sort.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? SortScore { get; set; }

    /// <summary>
    /// Gets or sets the id of this gap's own item in the library, when the server already tracks it.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LibraryItemId { get; set; }

    /// <summary>
    /// Gets or sets the id of this gap's season in the library, when known.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SeasonItemId { get; set; }

    /// <summary>
    /// Gets or sets the TMDB id to use for "where to watch" when the gap is not directly watchable.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WatchTmdbId { get; set; }

    /// <summary>
    /// Gets or sets the distinct services the title is on, or null when there are none.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AvailabilityBadge>? Availability { get; set; }

    /// <summary>
    /// Gets or sets the title's "where to watch" page on TMDB, which lists every service and how it is
    /// offered in the configured region, or null when there is none. It is the same for every offer, so a row
    /// carries it once for the service icons to link to.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WatchUrl { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether "where to watch" has been looked up for this gap.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AvailabilityChecked { get; set; }
}
