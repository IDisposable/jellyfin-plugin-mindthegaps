using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.Services.Availability;

/// <summary>
/// The per-country offers in a TMDB watch/providers response.
/// </summary>
internal class TmdbWatchCountry
{
    /// <summary>Gets or sets the TMDB region watch link.</summary>
    public string? Link { get; set; }

    /// <summary>Gets or sets the subscription (flatrate) providers.</summary>
    public IReadOnlyList<TmdbWatchProvider>? Flatrate { get; set; }

    /// <summary>Gets or sets the rental providers.</summary>
    public IReadOnlyList<TmdbWatchProvider>? Rent { get; set; }

    /// <summary>Gets or sets the purchase providers.</summary>
    public IReadOnlyList<TmdbWatchProvider>? Buy { get; set; }

    /// <summary>Gets or sets the free providers.</summary>
    public IReadOnlyList<TmdbWatchProvider>? Free { get; set; }

    /// <summary>Gets or sets the ad-supported providers.</summary>
    public IReadOnlyList<TmdbWatchProvider>? Ads { get; set; }
}
