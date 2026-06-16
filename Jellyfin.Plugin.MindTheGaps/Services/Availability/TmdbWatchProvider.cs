namespace Jellyfin.Plugin.MindTheGaps.Services.Availability;

/// <summary>
/// A single streaming provider in a TMDB watch/providers response.
/// </summary>
public class TmdbWatchProvider
{
    /// <summary>Gets or sets the provider display name.</summary>
    public string? ProviderName { get; set; }

    /// <summary>Gets or sets the TMDB provider id.</summary>
    public int ProviderId { get; set; }
}
