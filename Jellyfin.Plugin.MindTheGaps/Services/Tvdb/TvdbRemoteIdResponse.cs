using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.Services.Tvdb;

/// <summary>
/// A TheTVDB <c>/search/remoteid/{id}</c> response.
/// </summary>
public class TvdbRemoteIdResponse
{
    /// <summary>Gets or sets the matched results.</summary>
    public IReadOnlyList<TvdbRemoteIdResult>? Data { get; set; }
}
