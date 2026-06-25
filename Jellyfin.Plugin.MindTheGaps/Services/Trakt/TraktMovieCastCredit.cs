namespace Jellyfin.Plugin.MindTheGaps.Services.Trakt;

/// <summary>
/// A Trakt acting credit.
/// </summary>
internal class TraktMovieCastCredit
{
    /// <summary>Gets or sets the character played.</summary>
    public string? Character { get; set; }

    /// <summary>Gets or sets the movie.</summary>
    public TraktMovie? Movie { get; set; }
}
