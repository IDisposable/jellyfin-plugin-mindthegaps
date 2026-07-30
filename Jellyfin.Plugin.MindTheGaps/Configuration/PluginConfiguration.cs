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
        ScanImdbLists = false;
        ScanImdbPeopleLists = false;
        ImdbListIds = string.Empty;
        TmdbSessionId = string.Empty;
        ScanTmdbWatchlist = false;
        ScanTmdbFavorites = false;
        ScanTraktWatchlist = false;
        TraktUsername = string.Empty;
        ScanTvdbFavorites = false;
        TvdbPin = string.Empty;
        ScanMdbListWatchlist = false;
        ScanDiscogsWantlist = false;
        DiscogsUsername = string.Empty;
        ScanOpenLibraryWantToRead = false;
        OpenLibraryUsername = string.Empty;
        ScanJustWatchLists = false;
        ScanJustWatchLikes = false;
        JustWatchToken = string.Empty;
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
    /// Gets or sets a comma-separated list of Trakt lists to surface as discovery (Recommendation) gaps. Each
    /// entry is a list's numeric id or its slug (Trakt accepts either on the lists endpoint).
    /// </summary>
    public string CuratedTraktListIds { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to surface the unowned titles on the IMDb watchlists and lists
    /// in <see cref="ImdbListIds"/> as discovery (Recommendation) gaps. Needs at least one id; IMDb's API
    /// needs no key. Off by default.
    /// </summary>
    public bool ScanImdbLists { get; set; }

    /// <summary>
    /// Gets or sets a comma-separated list of IMDb ids to read. Each entry is a list id ("ls055576446"), a
    /// user id ("ur1000000", meaning that user's watchlist), or a pasted imdb.com URL holding either, parsed
    /// by <see cref="Gaps.Sources.Imdb.ImdbListInput"/>. The list has to be public: IMDb serves nothing
    /// private without the owner's session.
    /// </summary>
    public string ImdbListIds { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether an IMDb <b>people</b> list in <see cref="ImdbListIds"/> is
    /// followed as a filmography seed, surfacing each named person's unowned credits as CreatorWorks gaps.
    /// This is the only creator source not seeded from the library, so it is what tracks a director you own
    /// nothing by. Separate from <see cref="ScanImdbLists"/> because one people list is many filmographies,
    /// which is a much larger result than a titles list of the same length. Off by default.
    /// </summary>
    public bool ScanImdbPeopleLists { get; set; }

    /// <summary>
    /// Gets or sets the TheMovieDb session id, minted by the connect flow on the settings page and used to
    /// read the account's watchlist and favorites. TMDB session ids do not expire, so this is a one-time
    /// setup, but the session can modify the account, so it is treated as a secret.
    /// </summary>
    /// <remarks>
    /// A session belongs to the application whose api key created it. The catalog reader falls back to the
    /// api key Jellyfin ships, which is registered to the Jellyfin project and shared by every install, so
    /// the connect flow refuses to run unless <see cref="TmdbApiKey"/> is set to the user's own key.
    /// </remarks>
    public string TmdbSessionId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to surface the unowned titles on the connected TheMovieDb
    /// account's watchlist as discovery (Recommendation) gaps. Needs <see cref="TmdbApiKey"/> and
    /// <see cref="TmdbSessionId"/>. Off by default.
    /// </summary>
    public bool ScanTmdbWatchlist { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the TheMovieDb pass also reads the account's favorites, not
    /// only its watchlist. Off by default, since a favorite is usually something already owned.
    /// </summary>
    public bool ScanTmdbFavorites { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to surface the unowned titles on a Trakt user's watchlist as
    /// discovery (Recommendation) gaps. Needs <see cref="TraktClientId"/> and <see cref="TraktUsername"/>;
    /// Trakt serves a public profile's watchlist without OAuth. Off by default.
    /// </summary>
    public bool ScanTraktWatchlist { get; set; }

    /// <summary>
    /// Gets or sets the Trakt username (or profile slug) whose watchlist is read. The profile has to be
    /// public: Trakt answers a private profile, an unknown username, and an empty watchlist identically, so a
    /// wrong value reads as "nothing on the list" rather than as an error.
    /// </summary>
    public string TraktUsername { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to surface the unowned series favorited on TheTVDB as
    /// discovery (Recommendation) gaps. Needs <see cref="TvdbApiKey"/> and <see cref="TvdbPin"/>. Yields far
    /// less than the other want-lists, since a favorite is usually something already owned. Off by default.
    /// </summary>
    public bool ScanTvdbFavorites { get; set; }

    /// <summary>
    /// Gets or sets the TheTVDB subscriber PIN. Optional for the episode cross-check, which reads the
    /// catalog, but required to read account data (the favorites), because a key-only token is not scoped to
    /// an account. When set it is sent on every login, and a PIN-scoped token serves the catalog reads too,
    /// so there is one login path either way.
    /// </summary>
    public string TvdbPin { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to surface the unowned titles on the MDBList account's own
    /// watchlist as discovery (Recommendation) gaps. Uses <see cref="MdbListApiKey"/>, which identifies the
    /// account, so it needs no username. Off by default.
    /// </summary>
    public bool ScanMdbListWatchlist { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to surface the unowned releases on a Discogs wantlist as
    /// discovery (Recommendation) gaps. Needs <see cref="DiscogsToken"/> and <see cref="DiscogsUsername"/>.
    /// Off by default.
    /// </summary>
    public bool ScanDiscogsWantlist { get; set; }

    /// <summary>
    /// Gets or sets the Discogs username whose wantlist is read. Discogs addresses a wantlist by username
    /// rather than by token, so the token authenticates and this says whose list to read; another user's is
    /// readable only if they have made it public.
    /// </summary>
    public string DiscogsUsername { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to surface the unowned works on an OpenLibrary "Want to Read"
    /// shelf as discovery (Recommendation) gaps. Needs only <see cref="OpenLibraryUsername"/>; OpenLibrary
    /// serves a public reading log without a key. Off by default.
    /// </summary>
    public bool ScanOpenLibraryWantToRead { get; set; }

    /// <summary>
    /// Gets or sets the OpenLibrary username whose "Want to Read" shelf is read (the part after /people/ in a
    /// profile URL). The reading log has to be public.
    /// </summary>
    public string OpenLibraryUsername { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to surface the unowned titles on the signed-in JustWatch
    /// account's watchlist as discovery (Recommendation) gaps. Needs <see cref="JustWatchToken"/>. Off by
    /// default.
    /// </summary>
    public bool ScanJustWatchLists { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the JustWatch pass also reads the account's liked titles, not
    /// only its watchlist. Off by default, since a like is weaker than a deliberate watchlist entry.
    /// </summary>
    public bool ScanJustWatchLikes { get; set; }

    /// <summary>
    /// Gets or sets the JustWatch account bearer token. JustWatch publishes no account API and issues no
    /// api keys, so the token is copied out of a signed-in browser session; it expires, and the JustWatch
    /// pass logs and skips rather than failing the scan when it does.
    /// </summary>
    public string JustWatchToken { get; set; }

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
