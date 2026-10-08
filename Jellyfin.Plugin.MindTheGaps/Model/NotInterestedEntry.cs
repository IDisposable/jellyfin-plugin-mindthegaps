using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// A title a signed-in user said they are not interested in on one of the web UI surfaces, kept on their own
/// list so the surfaces stop showing it to them. It holds enough of the gap to match the title wherever it
/// appears again and to show it on the list they restore from. It is the user's alone: the report's own
/// resolutions are a different, server-wide thing.
/// </summary>
public class NotInterestedEntry
{
    /// <summary>
    /// Gets or sets the id of the gap the title was dismissed from (the key the entry is stored under).
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
    /// Gets or sets the target item-kind name (Movie, Series, MusicAlbum, Book).
    /// </summary>
    public string TargetKindName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the author or artist, which is half of an album's identity when it shares no provider id.
    /// </summary>
    public string? Creator { get; set; }

    /// <summary>
    /// Gets or sets the poster or cover URL the gap carried, for the list.
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Gets or sets the gap's provider ids, which are what match the title on another surface.
    /// </summary>
    public IReadOnlyDictionary<string, string> ProviderIds { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// Gets or sets when the title was dismissed (ISO 8601 UTC string).
    /// </summary>
    public string AddedUtc { get; set; } = string.Empty;
}
