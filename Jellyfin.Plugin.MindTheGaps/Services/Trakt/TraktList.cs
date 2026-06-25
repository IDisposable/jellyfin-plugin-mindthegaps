namespace Jellyfin.Plugin.MindTheGaps.Services.Trakt;

/// <summary>
/// A Trakt list's metadata, as returned by the list-info endpoint. Only the name is bound (for the chip).
/// </summary>
internal class TraktList
{
    /// <summary>Gets or sets the list's display name.</summary>
    public string? Name { get; set; }
}
