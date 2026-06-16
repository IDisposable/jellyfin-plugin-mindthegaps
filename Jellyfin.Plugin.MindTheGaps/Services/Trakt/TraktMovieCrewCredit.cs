namespace Jellyfin.Plugin.MindTheGaps.Services.Trakt;

/// <summary>
/// A Trakt crew credit.
/// </summary>
public class TraktMovieCrewCredit
{
    /// <summary>Gets or sets the job.</summary>
    public string? Job { get; set; }

    /// <summary>Gets or sets the movie.</summary>
    public TraktMovie? Movie { get; set; }
}
