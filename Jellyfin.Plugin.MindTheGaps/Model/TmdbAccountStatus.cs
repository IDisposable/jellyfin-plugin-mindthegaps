namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// Whether the settings page can connect a TheMovieDb account, and whether one is connected.
/// </summary>
public class TmdbAccountStatus
{
    /// <summary>
    /// Gets or sets a value indicating whether connecting is possible at all, which needs the user's own
    /// TMDB API key. False means the connect button stays disabled.
    /// </summary>
    public bool CanConnect { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a working session is stored.
    /// </summary>
    public bool Connected { get; set; }

    /// <summary>
    /// Gets or sets the connected account's username, when connected.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Gets or sets the explanation to show, when there is something to explain.
    /// </summary>
    public string? Message { get; set; }
}
