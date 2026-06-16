using System.Collections.Generic;
using Jellyfin.Data.Enums;

namespace Jellyfin.Plugin.MindTheGaps.Services.Availability;

/// <summary>
/// A request for streaming-availability offers for a single title. Provider-agnostic, mirroring
/// <see cref="Model.GapItem"/>, with no presumed provider.
/// </summary>
public sealed class AvailabilityQuery
{
    /// <summary>
    /// Gets or sets what the title is (Movie, Series, ...). Callers set this explicitly.
    /// </summary>
    public BaseItemKind TargetKind { get; set; }

    /// <summary>
    /// Gets or sets the provider ids used to look the title up (e.g. {"Tmdb":"603","Imdb":"tt0133093"}).
    /// </summary>
    public IReadOnlyDictionary<string, string> ProviderIds { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// Gets or sets the title, for sources that search by name.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the release year.
    /// </summary>
    public int? Year { get; set; }

    /// <summary>
    /// Gets or sets the country code (ISO 3166-1 alpha-2).
    /// </summary>
    public string Country { get; set; } = string.Empty;
}
