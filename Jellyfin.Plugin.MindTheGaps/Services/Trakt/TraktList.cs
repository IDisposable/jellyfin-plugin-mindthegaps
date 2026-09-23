namespace Jellyfin.Plugin.MindTheGaps.Services.Trakt;

/// <summary>
/// A Trakt list's metadata, as returned by the list-info and list-search endpoints.
/// </summary>
internal class TraktList
{
    /// <summary>Gets or sets the list's display name.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the list's ids (trakt numeric id and slug); null from the list-info endpoint,
    /// which this class also binds and which carries no ids block.</summary>
    public TraktIds? Ids { get; set; }
}
