using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.Services.Availability;

/// <summary>
/// A TMDB <c>watch/providers/{movie|tv}</c> catalog response: every streaming provider available in one region,
/// each with its logo.
/// </summary>
internal class TmdbProviderList
{
    /// <summary>Gets or sets the providers.</summary>
    public IReadOnlyList<TmdbWatchProvider>? Results { get; set; }
}
