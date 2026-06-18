# Configuration reference

Every setting on the **Dashboard > Plugins > Mind the Gaps** page, what it does, and what happens when
you set or clear it. The page is grouped into the sections below in the same order. Nothing here is
required to get a useful report: the defaults scan collections, series, and filmographies against the
built-in TMDB key. Each setting is saved when you press **Save**; most take effect on the next scan
(press **Rescan now** on the report, or wait for the scheduled task).

For how to read the results, see the [report guide](report-guide.md).

## What to scan

These toggles decide which gap sources run. Turning one off removes its gaps from the next report; it
does not delete anything from your library. Leaving everything off produces an empty report.

| Setting | Default | When set | When cleared |
|---|---|---|---|
| **Collections / franchises** (`ScanCollections`) | On | For each owned movie that belongs to a TMDB collection (box set), lists the other films in that collection you do not own. Your BoxSets need a TMDB id for this to fire. | No collection-completion gaps. |
| **Series (missing seasons / episodes)** (`ScanSeries`) | On | Lists seasons and episodes a series should have but the library is missing, from the series' own metadata. Capped per show by **Max missing episodes per show**. | No missing-episode gaps from the library source (the TVmaze/TheTVDB cross-checks below are separate). |
| **People (filmographies)** (`ScanPeople`) | On | For each owned actor/director/writer, lists films from their TMDB filmography you do not own. People are scanned stalest-first in batches capped by **Max creators scanned per run**, so coverage accumulates over repeated runs. | No filmography gaps. |
| **Recommendations (similar titles)** (`ScanRecommendations`) | Off | For each owned movie/series, surfaces TMDB "similar" titles you do not own. Can be noisy; this is discovery, not completion. Owned titles are used as seeds stalest-first, capped per run. | No recommendation gaps. |
| **Curated studio / keyword sets** (`ScanCuratedSets`) | Off | Treats the studios and keywords below as sets to complete: lists films from those TMDB companies/keywords you do not own. | The curated lists and auto-seed below are ignored. |
| **Music (artist discographies)** (`ScanMusic`) | Off | Experimental. For each owned music artist, lists missing studio-album release-groups from the MusicBrainz discography. | No music gaps. |
| **Books (author bibliographies)** (`ScanBooks`) | Off | Experimental. For each owned book, lists other entries in the author's bibliography (OpenLibrary). Known rough edges: author disambiguation, missing publish years, and duplicate titles (see the roadmap). | No book gaps. |

### Curated set inputs

These only matter when **Curated studio / keyword sets** is on.

| Setting | When set | When cleared |
|---|---|---|
| **Studio names** (`CuratedCompanyNames`) | Comma-separated studio names, for example `A24, Studio Ghibli`. Each is resolved to a TMDB company at scan time. Convenient but a name can resolve to the wrong company; prefer ids when in doubt. | No name-resolved studios. |
| **TMDB studio (company) ids** (`CuratedCompanyIds`) | Comma-separated TMDB company ids, for example `41077` (A24), `10342` (Studio Ghibli). Exact, no resolution guesswork. | No id-specified studios. |
| **TMDB keyword ids** (`CuratedKeywordIds`) | Comma-separated TMDB keyword ids to complete (for example a franchise keyword). | No keyword sets. |
| **Auto-seed studios from your library** (`AutoSeedStudios`) | Tracks the studios most common across your owned movies and series without you listing anything. Combine with the explicit lists or use alone. | Only the studios/keywords you listed are tracked. |

## Where to watch

| Setting | Default | When set | When cleared |
|---|---|---|---|
| **Availability ("where to watch")** (`IncludeAvailability`) | On | Enables streaming-availability lookups: the per-row **Where to watch** button, the report's background **Look up where to watch** pass, and the report's **Hide items with no sources** / per-provider filters. Lookups use TMDB `watch/providers` (cached 12h) and never run during the scan itself. | The button and the availability filters do nothing; no provider data is fetched. |

## Data sources

TMDB is always on (it powers collections, people, recommendations, and availability). The rest are
opt-in cross-checks that need your own credentials.

| Setting | Default | When set | When cleared |
|---|---|---|---|
| **TMDB API key** (`TmdbApiKey`) | Empty (built-in key) | Uses your own TMDB v3 key, so lookups draw on your request budget instead of the shared default. Get one at [themoviedb.org/settings/api](https://www.themoviedb.org/settings/api). | Falls back to the built-in public key. |
| **Webhook URL** (`WebhookUrl`) | Empty | Posts a summary (Discord-compatible `content` payload) when a scan or the availability pass finishes. | No webhook is sent. |
| **Trakt cross-check** (`TraktEnabled` + `TraktClientId`) | Off | Adds a Trakt filmography cross-check alongside TMDB, catching credits TMDB misses. Requires a free Trakt app **Client ID** from [trakt.tv/oauth/applications](https://trakt.tv/oauth/applications); opt-in per Trakt's terms. | No Trakt cross-check. |
| **TVmaze cross-check** (`TvMazeEnabled`) | Off | Keyless. Catches episodes a series' configured metadata provider does not list. Shares episode ids with the library and TheTVDB sources, so duplicates are de-duped. | No TVmaze cross-check. |
| **TheTVDB cross-check** (`TvdbEnabled` + `TvdbApiKey`) | Off | Adds a TheTVDB series-content cross-check. Requires your own v4 key from [thetvdb.com](https://thetvdb.com/dashboard/account/apikey). | No TheTVDB cross-check. |

> Note: API keys are sensitive. If a key ever ends up in a URL or browser history, rotate it.

## Region

| Setting | Default | Effect |
|---|---|---|
| **Country code** (`MetadataCountryCode`) | `US` | ISO 3166-1 alpha-2 (e.g. `US`, `GB`, `DE`). Drives release dates and which country's streaming providers "where to watch" reports. |
| **Language** (`MetadataLanguage`) | `en` | ISO 639-1 (e.g. `en`, `de`). Language of titles and overviews fetched from TMDB. |

## Limits

These bound how much each scan produces, so one prolific show or a huge cast does not flood the list.

| Setting | Default | Effect |
|---|---|---|
| **Max related per item** (`MaxRelatedPerItem`) | 20 | Caps how many "similar" titles each owned item contributes to recommendations. |
| **Max missing episodes per show** (`MaxMissingEpisodesPerShow`) | 200 | Caps missing episodes listed per show. `0` lists them all. |
| **Max creators scanned per run** (`MaxFilmographyPeople`) | 1000 | Caps how many owned people have their filmography scanned per run. People are scanned stalest-first (never-scanned first, then longest-ago), so a lower cap still eventually covers everyone over successive runs; raise it to cover a large cast/crew faster (each person is one cached TMDB lookup). |

## Experimental: virtual items

Off by default and clearly marked. Lets the plugin mint pathless "virtual" placeholder items so a gap
renders greyed-out in place, and reconcile/remove them. This is a stand-in for proper server support;
everything minted is tagged and fully reversible. See the
[virtual placeholders section of the README](../README.md#virtual-placeholders-experimental-opt-in)
and [ADR-0004](adr/) for the rationale, and the [report guide](report-guide.md) for the per-row Mint
controls. The settings page itself keeps only **Remove minted movies** (with a dry-run preview) to undo
everything at once.

## How settings reach a report

Configuration changes apply to the **next** scan, not the report already on screen. The persisted
report carries the plugin version it was generated with, so after a plugin upgrade the dashboard nudges
you to rescan (the gap ids and links are a stable contract; see [ADR-0008](adr/)). Press **Rescan now**
on the report to apply changes immediately, or let the scheduled task pick them up.
