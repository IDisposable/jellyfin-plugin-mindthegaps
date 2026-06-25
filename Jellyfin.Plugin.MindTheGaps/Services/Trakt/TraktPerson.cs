namespace Jellyfin.Plugin.MindTheGaps.Services.Trakt;

/// <summary>
/// A Trakt person.
/// </summary>
internal class TraktPerson
{
    /// <summary>Gets or sets the name.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the ids.</summary>
    public TraktIds? Ids { get; set; }
}
