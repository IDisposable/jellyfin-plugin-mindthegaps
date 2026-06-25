namespace Jellyfin.Plugin.MindTheGaps.Services.Trakt;

/// <summary>
/// One entry on a Trakt list. A list can mix movies and shows, so an entry carries a <see cref="Type"/>
/// discriminator and exactly one of <see cref="Movie"/> or <see cref="Show"/>.
/// </summary>
internal class TraktListItem
{
    /// <summary>Gets or sets the entry type ("movie" or "show").</summary>
    public string? Type { get; set; }

    /// <summary>Gets or sets the movie, when <see cref="Type"/> is "movie".</summary>
    public TraktMovie? Movie { get; set; }

    /// <summary>Gets or sets the show, when <see cref="Type"/> is "show".</summary>
    public TraktShow? Show { get; set; }
}
