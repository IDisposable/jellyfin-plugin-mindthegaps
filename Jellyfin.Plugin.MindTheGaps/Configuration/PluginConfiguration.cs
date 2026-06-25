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
        ScanTmdbLists = false;
        CuratedCompanyIds = string.Empty;
        CuratedKeywordIds = string.Empty;
        CuratedTmdbListIds = string.Empty;
        AutoSeedStudios = false;
        ScanMusic = true;
        ScanBooks = true;
        ScanCuratedBooks = false;
        CuratedOpenLibrarySubjects = string.Empty;
        ScanDiscogs = false;
        DiscogsToken = string.Empty;
        DiscogsLabelIds = string.Empty;
        ScanMdbList = false;
        MdbListApiKey = string.Empty;
        MdbListListIds = string.Empty;
        ScanTraktLists = false;
        CuratedTraktListIds = string.Empty;
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
        TvdbApiKey = string.Empty;
        TmdbApiKey = string.Empty;
        WebhookUrl = string.Empty;
        SeerrUrl = string.Empty;
        SeerrApiKey = string.Empty;
        RadarrUrl = string.Empty;
        RadarrApiKey = string.Empty;
        RadarrQualityProfileId = 0;
        RadarrRootFolderPath = string.Empty;
        SonarrUrl = string.Empty;
        SonarrApiKey = string.Empty;
        SonarrQualityProfileId = 0;
        SonarrRootFolderPath = string.Empty;
        SonarrMonitor = "all";
        SearchUrlTemplate = "https://www.google.com/search?q={0}";
        DetailedApiLogging = false;
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
    /// Gets or sets a value indicating whether to surface discovery gaps from the TMDB lists in
    /// <see cref="CuratedTmdbListIds"/>. Separate from <see cref="ScanCuratedSets"/> so a discovery list
    /// can run without also running the studio and keyword set-completion sources.
    /// </summary>
    public bool ScanTmdbLists { get; set; }

    /// <summary>
    /// Gets or sets a comma-separated list of TMDB lists to surface as discovery (Recommendation) gaps. Each
    /// entry is a list id or a pasted themoviedb.org/list/{id} URL (TMDB has no list search), parsed by
    /// <see cref="Gaps.Sources.Tmdb.TmdbListInput"/>.
    /// </summary>
    public string CuratedTmdbListIds { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to scan owned music artists for missing studio-album
    /// release-groups (MusicBrainz discography). On by default.
    /// </summary>
    public bool ScanMusic { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to scan owned books for other entries in the author's
    /// bibliography or the book's series (OpenLibrary). On by default.
    /// </summary>
    public bool ScanBooks { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to complete curated books sets from the OpenLibrary subjects
    /// in <see cref="CuratedOpenLibrarySubjects"/>. Separate from <see cref="ScanBooks"/> so the curated
    /// subject sets can run without the owned-book bibliography walk. Off by default.
    /// </summary>
    public bool ScanCuratedBooks { get; set; }

    /// <summary>
    /// Gets or sets a comma-separated list of OpenLibrary subject slugs to complete as curated sets (for
    /// example "science_fiction,fantasy"). A subject is fetched by slug, so the settings page takes the
    /// slugs directly.
    /// </summary>
    public string CuratedOpenLibrarySubjects { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to surface missing releases from curated Discogs record
    /// labels. Needs a Discogs token and at least one label id. Off by default.
    /// </summary>
    public bool ScanDiscogs { get; set; }

    /// <summary>
    /// Gets or sets the Discogs personal access token used to authenticate Discogs API calls. Without it
    /// the Discogs source stays off (Discogs requires authentication for catalog browsing).
    /// </summary>
    public string DiscogsToken { get; set; }

    /// <summary>
    /// Gets or sets a comma-separated list of Discogs label ids to complete as curated sets.
    /// </summary>
    public string DiscogsLabelIds { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to surface unowned titles from curated MDBList community
    /// lists as discovery (Recommendation) gaps. Needs an MDBList API key and at least one chosen list.
    /// Off by default.
    /// </summary>
    public bool ScanMdbList { get; set; }

    /// <summary>
    /// Gets or sets the MDBList API key used to authenticate MDBList API calls (a free key from
    /// mdblist.com). Without it the MDBList source and its list search stay off.
    /// </summary>
    public string MdbListApiKey { get; set; }

    /// <summary>
    /// Gets or sets a comma-separated list of MDBList list ids to surface as discovery gaps. The settings
    /// page maintains this from its MDBList chip picker; the ids are never shown directly.
    /// </summary>
    public string MdbListListIds { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to surface unowned titles from the Trakt lists in
    /// <see cref="CuratedTraktListIds"/> as discovery (Recommendation) gaps. Needs a Trakt client id and at
    /// least one chosen list. Off by default.
    /// </summary>
    public bool ScanTraktLists { get; set; }

    /// <summary>
    /// Gets or sets a comma-separated list of Trakt list ids to surface as discovery (Recommendation) gaps.
    /// A list is fetched by id, so the settings page takes the ids directly.
    /// </summary>
    public string CuratedTraktListIds { get; set; }

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
    /// Gets or sets the user-supplied TheTVDB v4 API key (the credential for the TheTVDB cross-check; it runs
    /// only when the Shows library lists TheTVDB as a metadata fetcher and this key is set).
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
    /// Gets or sets the Jellyseerr/Overseerr base URL (for example http://localhost:5055). Empty disables
    /// the "Request" handoff.
    /// </summary>
    public string SeerrUrl { get; set; }

    /// <summary>
    /// Gets or sets the Jellyseerr/Overseerr API key. Required, with <see cref="SeerrUrl"/>, for the handoff.
    /// </summary>
    public string SeerrApiKey { get; set; }

    /// <summary>
    /// Gets or sets the Radarr base URL (for example http://localhost:7878). Empty disables the Radarr handoff.
    /// </summary>
    public string RadarrUrl { get; set; }

    /// <summary>
    /// Gets or sets the Radarr API key. Required, with <see cref="RadarrUrl"/>, for the Radarr handoff.
    /// </summary>
    public string RadarrApiKey { get; set; }

    /// <summary>
    /// Gets or sets the Radarr quality profile id a sent movie is added with. Must be set (greater than zero)
    /// for the Radarr handoff.
    /// </summary>
    public int RadarrQualityProfileId { get; set; }

    /// <summary>
    /// Gets or sets the Radarr root folder path a sent movie is added under (for example /movies). Required
    /// for the Radarr handoff.
    /// </summary>
    public string RadarrRootFolderPath { get; set; }

    /// <summary>
    /// Gets or sets the Sonarr base URL (for example http://localhost:8989). Empty disables the Sonarr handoff.
    /// </summary>
    public string SonarrUrl { get; set; }

    /// <summary>
    /// Gets or sets the Sonarr API key. Required, with <see cref="SonarrUrl"/>, for the Sonarr handoff.
    /// </summary>
    public string SonarrApiKey { get; set; }

    /// <summary>
    /// Gets or sets the Sonarr quality profile id a sent series is added with. Must be set (greater than zero)
    /// for the Sonarr handoff.
    /// </summary>
    public int SonarrQualityProfileId { get; set; }

    /// <summary>
    /// Gets or sets the Sonarr root folder path a sent series is added under (for example /tv). Required for
    /// the Sonarr handoff.
    /// </summary>
    public string SonarrRootFolderPath { get; set; }

    /// <summary>
    /// Gets or sets the Sonarr monitor option for a sent series (for example all, future, missing,
    /// firstSeason, latestSeason, pilot, none). Defaults to all.
    /// </summary>
    public string SonarrMonitor { get; set; }

    /// <summary>
    /// Gets or sets the web-search URL template the dashboard builds each todo row's search link from, so
    /// the user can point it at their preferred search engine. The {0} placeholder is replaced with the
    /// URL-encoded query (the title and year).
    /// </summary>
    public string SearchUrlTemplate { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the plugin logs every external API request and response, for
    /// debugging. Off by default to keep the log quiet; turn it on to follow an integration end to end in the
    /// server log. Api keys, tokens, and bearers ride in request headers; the few carried in a query string are
    /// redacted before logging, so no secret reaches the log.
    /// </summary>
    public bool DetailedApiLogging { get; set; }
}
