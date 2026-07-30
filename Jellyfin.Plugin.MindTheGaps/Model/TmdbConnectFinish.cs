namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// The second step of the TheMovieDb connect flow: the request token the user has approved.
/// </summary>
public class TmdbConnectFinish
{
    /// <summary>
    /// Gets or sets the request token handed out by the start call.
    /// </summary>
    public string RequestToken { get; set; } = string.Empty;
}
