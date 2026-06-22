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
        ScanCuratedSets = false;
        CuratedCompanyIds = string.Empty;
        CuratedKeywordIds = string.Empty;
        AutoSeedStudios = false;
        ScanMusic = false;
        ScanBooks = false;
        ScanDiscogs = false;
        DiscogsToken = string.Empty;
        DiscogsLabelIds = string.Empty;
        IncludeAvailability = true;
        AvailabilityCacheHours = 24;
        MaxRelatedPerItem = 20;
        MinRecommendationVotes = 100;
        MaxMissingEpisodesPerShow = 200;
        MaxFilmographyPeople = 1000;
        MinFilmographyVotes = 100;
        MaxCastBillingOrder = 0;
        MetadataCountryCode = "US";
        MetadataLanguage = "en";
        TraktEnabled = false;
        TraktClientId = string.Empty;
        TvMazeEnabled = false;
        TvdbEnabled = false;
        TvdbApiKey = string.Empty;
        TmdbApiKey = string.Empty;
        WebhookUrl = string.Empty;
        BulkMintCap = 200;
        AutoMint = false;
        AutoMintSetCompletion = true;
        AutoMintCreatorWorks = false;
        AutoMintRecommendations = false;
        AutoMintCap = 50;
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
    /// Gets or sets a value indicating whether to surface missing movies from curated TMDB sets (studios
    /// and keywords listed in <see cref="CuratedCompanyIds"/> / <see cref="CuratedKeywordIds"/>).
    /// </summary>
    public bool ScanCuratedSets { get; set; }

    /// <summary>
    /// Gets or sets a comma-separated list of TMDB company (studio) ids to track as curated sets. The
    /// settings page maintains this from its studio chip picker; the ids are never shown directly.
    /// </summary>
    public string CuratedCompanyIds { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to auto-seed curated studio sets from the studios most
    /// common on owned movies and series, so studios are tracked without entering anything.
    /// </summary>
    public bool AutoSeedStudios { get; set; }

    /// <summary>
    /// Gets or sets a comma-separated list of TMDB keyword ids to track as curated sets. The settings page
    /// maintains this from its keyword chip picker; the ids are never shown directly.
    /// </summary>
    public string CuratedKeywordIds { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to scan owned music artists for missing studio-album
    /// release-groups (MusicBrainz discography). Off by default.
    /// </summary>
    public bool ScanMusic { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to scan owned books for other entries in the author's
    /// bibliography or the book's series (OpenLibrary). Off by default.
    /// </summary>
    public bool ScanBooks { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to surface missing releases from curated Discogs record
    /// labels. Needs a Discogs token and at least one label id. Off by default.
    /// </summary>
    public bool ScanDiscogs { get; set; }

    /// <summary>
    /// Gets or sets the Discogs personal access token used to authenticate Discogs API calls. Without it
    /// the Discogs source stays off (Discogs requires authentication for catalogue browsing).
    /// </summary>
    public string DiscogsToken { get; set; }

    /// <summary>
    /// Gets or sets a comma-separated list of Discogs label ids to complete as curated sets.
    /// </summary>
    public string DiscogsLabelIds { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to enrich gaps with streaming-availability data ("where
    /// to watch"), both the per-item lookups and the background enrichment pass.
    /// </summary>
    public bool IncludeAvailability { get; set; }

    /// <summary>
    /// Gets or sets how many hours a cached "where to watch" lookup stays fresh before it is refreshed.
    /// A stale entry is still served immediately while a refresh runs behind the scenes, so this trades
    /// how current the data is against how often TMDB is hit, never against responsiveness. Minimum 1.
    /// </summary>
    public int AvailabilityCacheHours { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of related titles to surface per source item.
    /// </summary>
    public int MaxRelatedPerItem { get; set; }

    /// <summary>
    /// Gets or sets the minimum TMDB vote count a recommended ("similar") title must have to surface as a
    /// gap, which trims the obscure long tail of the discovery feed. 0 disables the gate (every result
    /// surfaces). Raise it to keep only well-known suggestions.
    /// </summary>
    public int MinRecommendationVotes { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of missing episodes listed per show. 0 means no limit (list
    /// them all). Keeps a single prolific show from flooding the todo list.
    /// </summary>
    public int MaxMissingEpisodesPerShow { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of owned people whose filmography is scanned per run. People are
    /// scanned most-credited-first, so a lower cap keeps the creators the library has the most work from.
    /// Raise it to cover more of a large cast/crew (each person is a single cached TMDB call).
    /// </summary>
    public int MaxFilmographyPeople { get; set; }

    /// <summary>
    /// Gets or sets the minimum TMDB vote count a filmography credit must have to surface as a gap, which
    /// keeps Creator works actionable for a large library by dropping obscure and unreleased titles. 0
    /// disables the gate (every credit surfaces). Raise it to trim the list to only well-known films.
    /// </summary>
    public int MinFilmographyVotes { get; set; }

    /// <summary>
    /// Gets or sets the deepest cast billing order a filmography role may have to surface as a gap, so a
    /// minor (deeply billed) appearance is not treated as the person's work. 0 disables the limit (any
    /// billing). Does not affect directing/writing credits, which are gated on votes only.
    /// </summary>
    public int MaxCastBillingOrder { get; set; }

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
    /// Gets or sets an optional webhook URL posted to when a scan or the background availability pass
    /// finishes. The payload leads with a Discord-friendly "content" string. Empty disables it.
    /// </summary>
    public string WebhookUrl { get; set; }

    /// <summary>
    /// Gets or sets the most gaps a single "Mint all in this tab" click materializes; the rest are left for
    /// the next click. A guardrail against flooding a library in one action.
    /// </summary>
    public int BulkMintCap { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the scheduled auto-mint task mints new materializable gaps in
    /// the selected patterns on the scan cadence. Off by default; reconciliation runs regardless.
    /// </summary>
    public bool AutoMint { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether auto-mint includes Set completion (collection) gaps.
    /// </summary>
    public bool AutoMintSetCompletion { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether auto-mint includes Creator works (filmography) gaps.
    /// </summary>
    public bool AutoMintCreatorWorks { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether auto-mint includes Recommendation gaps.
    /// </summary>
    public bool AutoMintRecommendations { get; set; }

    /// <summary>
    /// Gets or sets the most gaps one unattended auto-mint run materializes; the rest fill in over later
    /// runs. A guardrail against flooding a library.
    /// </summary>
    public int AutoMintCap { get; set; }
}
