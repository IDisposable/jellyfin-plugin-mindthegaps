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
        CuratedCompanyNames = string.Empty;
        CuratedCompanyIds = string.Empty;
        CuratedKeywordIds = string.Empty;
        AutoSeedStudios = false;
        ScanMusic = false;
        ScanBooks = false;
        IncludeAvailability = true;
        MaxRelatedPerItem = 20;
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
    /// Gets or sets a comma-separated list of studio names to track as curated sets, for example
    /// "A24, Studio Ghibli". Each is resolved to a TMDB company at scan time.
    /// </summary>
    public string CuratedCompanyNames { get; set; }

    /// <summary>
    /// Gets or sets a comma-separated list of TMDB company (studio) ids to track as curated sets, for
    /// example "41077" for A24 or "10342" for Studio Ghibli (an alternative to entering names).
    /// </summary>
    public string CuratedCompanyIds { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to auto-seed curated studio sets from the studios most
    /// common on owned movies and series, so studios are tracked without entering anything.
    /// </summary>
    public bool AutoSeedStudios { get; set; }

    /// <summary>
    /// Gets or sets a comma-separated list of TMDB keyword ids to track as curated sets.
    /// </summary>
    public string CuratedKeywordIds { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to scan owned music artists for missing studio-album
    /// release-groups (MusicBrainz discography). Experimental; defaults off.
    /// </summary>
    public bool ScanMusic { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to scan owned books for other entries in the author's
    /// bibliography or the book's series (OpenLibrary). Experimental; defaults off.
    /// </summary>
    public bool ScanBooks { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to enrich gaps with streaming-availability data ("where
    /// to watch"), both the per-item lookups and the background enrichment pass.
    /// </summary>
    public bool IncludeAvailability { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of related titles to surface per source item.
    /// </summary>
    public int MaxRelatedPerItem { get; set; }

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
}
