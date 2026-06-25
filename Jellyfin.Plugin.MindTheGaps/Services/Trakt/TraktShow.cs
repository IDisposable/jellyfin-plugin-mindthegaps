namespace Jellyfin.Plugin.MindTheGaps.Services.Trakt;

/// <summary>
/// A Trakt show (Trakt's own term for an episodic series).
/// </summary>
internal class TraktShow
{
    /// <summary>Gets or sets the title.</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets the release year.</summary>
    public int? Year { get; set; }

    /// <summary>Gets or sets the ids.</summary>
    public TraktIds? Ids { get; set; }
}
