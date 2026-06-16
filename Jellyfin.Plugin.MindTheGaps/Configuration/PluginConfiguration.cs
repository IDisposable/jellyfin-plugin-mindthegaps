using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using Jellyfin.Plugin.MindTheGaps.Model;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.MindTheGaps.Configuration;

/// <summary>
/// Which gap categories the engine should scan for.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        ScanCollections = true;
        ScanSeries = true;
        ScanPeople = true;
        ScanRecommendations = false;
        IncludeAvailability = true;
        MaxRelatedPerItem = 20;
        MetadataCountryCode = "US";
        MetadataLanguage = "en";
        TraktEnabled = false;
        TraktClientId = string.Empty;
        TvMazeEnabled = false;
        TvdbEnabled = false;
        TvdbApiKey = string.Empty;
        TmdbApiKey = string.Empty;
    }

    /// <summary>
    /// Gets or sets a value indicating whether to scan partially-owned collections/franchises for missing movies.
    /// </summary>
    public bool ScanCollections { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to scan series for missing seasons/episodes.
    /// </summary>
    public bool ScanSeries { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to scan actor/director filmographies for unowned credits.
    /// </summary>
    public bool ScanPeople { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to include TMDB recommendations/similar titles as discovery gaps.
    /// </summary>
    public bool ScanRecommendations { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to enrich gaps with streaming-availability data.
    /// </summary>
    public bool IncludeAvailability { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of related titles to surface per source item.
    /// </summary>
    public int MaxRelatedPerItem { get; set; }

    /// <summary>
    /// Gets or sets the metadata country code (ISO 3166-1 alpha-2) used for releases and availability.
    /// </summary>
    public string MetadataCountryCode { get; set; }

    /// <summary>
    /// Gets or sets the metadata language (ISO 639-1) used for titles and overviews.
    /// </summary>
    public string MetadataLanguage { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the Trakt filmography cross-check is enabled.
    /// </summary>
    public bool TraktEnabled { get; set; }

    /// <summary>
    /// Gets or sets the user-supplied Trakt application client id (required for Trakt; opt-in per Trakt ToS).
    /// </summary>
    public string TraktClientId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the TVmaze series-completeness cross-check is enabled.
    /// </summary>
    public bool TvMazeEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the TheTVDB series-completeness cross-check is enabled.
    /// </summary>
    public bool TvdbEnabled { get; set; }

    /// <summary>
    /// Gets or sets the user-supplied TheTVDB v4 API key (required for the TheTVDB cross-check).
    /// </summary>
    public string TvdbApiKey { get; set; }

    /// <summary>
    /// Gets or sets an optional TMDB API key. When empty, the public default key is used.
    /// </summary>
    public string TmdbApiKey { get; set; }

    /// <summary>
    /// Gets or sets the gap patterns to mint as pathless virtual items (the same three patterns the
    /// engine models: <see cref="GapPattern.SetCompletion"/>, <see cref="GapPattern.CreatorWorks"/>,
    /// <see cref="GapPattern.Recommendation"/>). Empty means mint nothing. Today only SetCompletion is
    /// materializable in bulk (missing collection movies into a BoxSet). Experimental and temporary
    /// (this belongs in core); everything minted is tagged and fully removable.
    /// </summary>
    /// <remarks>
    /// Must have a setter: System.Text.Json (the plugin-config API deserializer) silently skips
    /// get-only collection properties, so a get-only collection here never receives the saved selection.
    /// </remarks>
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Settable so the plugin-config API (System.Text.Json) can deserialize the saved selection; a get-only collection is silently skipped.")]
    public Collection<GapPattern> MintPatterns { get; set; } = new();
}
