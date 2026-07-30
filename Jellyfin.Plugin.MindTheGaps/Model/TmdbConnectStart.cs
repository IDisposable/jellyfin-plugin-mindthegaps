namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// The first step of the TheMovieDb connect flow: the request token and the page the user approves it on.
/// </summary>
public class TmdbConnectStart
{
    /// <summary>
    /// Gets or sets the request token, handed back on the finish call.
    /// </summary>
    public string RequestToken { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the themoviedb.org page the user approves the token on. It carries no redirect
    /// parameter, so nothing has to call back into this server.
    /// </summary>
    public string ApprovalUrl { get; set; } = string.Empty;
}
