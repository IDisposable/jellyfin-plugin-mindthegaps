namespace Jellyfin.Plugin.MindTheGaps.Services.Trakt;

/// <summary>
/// A Trakt search result row.
/// </summary>
public class TraktSearchResult
{
    /// <summary>Gets or sets the result type (e.g. "person").</summary>
    public string? Type { get; set; }

    /// <summary>Gets or sets the matched person.</summary>
    public TraktPerson? Person { get; set; }
}
