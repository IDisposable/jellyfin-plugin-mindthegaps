using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.Services.Availability;

/// <summary>
/// A TMDB <c>watch/providers</c> response, keyed by ISO country code.
/// </summary>
public class TmdbWatchResponse
{
    /// <summary>Gets or sets the per-country offers.</summary>
    public IReadOnlyDictionary<string, TmdbWatchCountry>? Results { get; set; }
}
