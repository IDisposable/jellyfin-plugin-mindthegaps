# Mind the Gaps: roadmap and status

> A map of what the plugin does today and what it deliberately does not (yet). Written to keep the
> difference between what the plugin *detects* and what it *materializes* clear, since that is the one
> distinction that trips people up.

## The one distinction that explains everything

There are two separate layers, and only one of them is narrow:

| Layer | What it is | Scope today |
|---|---|---|
| Detection | The gap engine plus its sources scan the library and produce the dashboard todo list of what is missing or related | Broad: all three patterns across movies and shows (plus experimental music and books), via six providers (TMDB, Trakt, TVmaze, TheTVDB, MusicBrainz, OpenLibrary) |
| Materialization | The minter creates pathless virtual placeholder items inside the library so a gap renders in place | Narrow: movie gaps only, one at a time from the report (BoxSet for collection gaps, a catch-all collection otherwise) |

Most assumptions that "the plugin only handles BoxSet movies" are really about the materialization
layer. Detection is wide. The minter is the experiment.

## Detection: built and working

Every source is wired in DI and its mapper is covered by captured-data tests.

| Source | Pattern | Detects | Status |
|---|---|---|---|
| CollectionGapSource (TMDB) | SetCompletion | missing movies in a partially-owned BoxSet/collection | Done |
| SeriesContentGapSource (Library) | SetCompletion | missing episodes the server already tracks | Done |
| TvMazeContentGapSource | SetCompletion | missing episodes cross-checked against TVmaze | Done |
| TvdbContentGapSource | SetCompletion | missing episodes cross-checked against TheTVDB | Done |
| PeopleGapSource (TMDB) | CreatorWorks | unowned films from owned actors and directors | Done |
| TraktFilmographyGapSource | CreatorWorks | the same, independently cross-checked via Trakt | Done |
| RecommendationsGapSource (TMDB) | Recommendation | similar movies and series | Done |
| CuratedSetGapSource (TMDB) | SetCompletion | missing films from a studio or keyword set (named, by id, or auto-seeded) | Done (opt-in) |
| MusicDiscographyGapSource (MusicBrainz) | SetCompletion | missing studio albums in the discography of an album artist you collect | Done (experimental) |
| MusicArtistWorksGapSource (MusicBrainz) | CreatorWorks | the wider catalogue of an artist you only own a track by | Done (experimental) |
| BooksBibliographyGapSource (OpenLibrary) | CreatorWorks | other works in an owned author's bibliography | Done (experimental) |
| AvailabilityService (TMDB) | n/a | "where to watch", lazily per item or via the background "Look up where to watch" pass (never during the scan) | Done |

Supporting pieces that are done: the dashboard todo list (pattern tabs, Movies/Shows grouping with
alphabetical sub-grouping for Creator Works and Recommendations, filters for type / specials / upcoming /
no-sources / resolved / search, external links per item, open-in-Jellyfin links), the scheduled scan and
its background runner, the dedupe engine, the background "where to watch" pass (cached, resumable, with a
"checked" flag and an episode-to-series lookup), per-gap resolutions (mark "not really missing" with a
note, persisted across rescans, ADR-0008), and a version-stamped report that nudges for a rescan after an
upgrade. CreatorWorks and Recommendation gaps are detected and shown today, for movies and series; they
render as a list with links, not as in-library placeholders.

External links are extensible without a hard-coded list or a plugin dependency: `ExternalLinkEnricher`
hands each gap's provider ids to a throwaway `BaseItem` and merges whatever the host's registered
`IExternalUrlProvider`s emit (TMDB and IMDb from core, JustWatch from that plugin if it is installed,
anything else later). The hand-built `ProviderLinks` list stays as a fallback for what core ships no
provider for (TheTVDB, and the season/episode urls a bare synthetic item cannot produce).

## Materialization: built and working

| Target | Pattern | Status |
|---|---|---|
| any movie gap, per-row or multi-select Mint from the report | any | Done (experimental, opt-in; dry-run and metrics supported): BoxSet for collection gaps, the catch-all "Mind the Gaps (minted)" collection otherwise (person gaps also attach to the person) |
| missing series episodes | SetCompletion | Not needed here: the server already synthesizes virtual missing episodes |

Minting is request-only and driven entirely from the report's per-row and multi-select Mint buttons;
the settings page fires no minting (it keeps only "Remove minted" and its preview). The minter is
idempotent, tags everything with the `MindTheGapsMinted` provider id, never deletes files, queues a
metadata + image refresh on each minted item, and is fully reversible. Reconciliation (drop a minted
placeholder once the library owns the real file) runs automatically at the end of every scan, on both
the scheduled task and the background runner. A whole collection is minted by selecting its missing
rows and hitting "Mint selected".

## Not built (and why)

| Capability | Where it stands |
|---|---|
| Music, Books, MusicVideos domains (`MediaDomain`; discography, book series, bibliography) | Music and Books have experimental sources (opt-in, off by default). Music: an album artist you collect yields a Set-completion discography, a track-only artist yields Creator-works "artist works". Books: author bibliography as Creator works. The symmetric **book series -> Set completion** is not built: OpenLibrary works carry no series and the Jellyfin Book entity has no series field, so there is no reliable series membership to diff. MusicVideos is still enum-only. |
| Materialize CreatorWorks or Recommendation gaps in bulk | Per-row and multi-select mint do it one batch at a time; no one-click "mint every gap of a pattern" |
| Native virtual items in core (the three upstream asks A/B/C) | A is filed as a PR; B and C are local drafts, not yet filed upstream. See [Upstream asks](#upstream-asks-a-b-c) below. |
| Per-user display gate for minted items | Not possible from a plugin; minted items show for everyone |
| `IGapSource` as a core SPI so third-party gap plugins can exist | Deferred on purpose (ADR-0002); every source ships in this plugin |
| Greyed "Missing" badge on minted movies | Needs upstream ask A ([jellyfin-web #8049](https://github.com/jellyfin/jellyfin-web/pull/8049)) merged |

## Upstream asks (A, B, C)

Three independent upstream changes would let the experience go fully native. The plugin works without
any of them; they are progressive enhancements, tracked in detail under [docs/upstream/](upstream/).

- **A - relax the "Missing" indicator (jellyfin-web).** Let virtual items render the greyed-out "Missing"
  treatment in more contexts than just episodes. This is the only one actually filed upstream: it is open
  as **[jellyfin-web #8049](https://github.com/jellyfin/jellyfin-web/pull/8049)**. Once it merges, the
  collection movies the plugin already mints would show the native greyed badge.
- **B - mint and reconcile virtual items for any type (server).** Not just show seasons and episodes,
  ideally behind a host `IVirtualItemManager` with a `DisplayMissingMovies` gate. This is the substantive
  feature. It is **not filed upstream yet** (there is no GitHub discussion or PR); the worked-out proposal
  lives in [docs/upstream/discussion-mint-virtual-items.md](upstream/discussion-mint-virtual-items.md),
  with the background analysis in [docs/virtual-movies-analysis.md](virtual-movies-analysis.md).
- **C - expose the shared TMDB client and key via the published NuGet (server).** So a plugin can reuse
  the host's cache and key instead of carrying its own. Pure plumbing cleanup; the plugin ships fine
  without it. **Not filed upstream yet**; the proposal is in
  [docs/upstream/discussion-tmdb-nuget-surface.md](upstream/discussion-tmdb-nuget-surface.md).

## Filling CreatorWorks and Recommendation gaps

Detecting these is done, and they materialize one at a time (the per-row and multi-select Mint buttons)
into the catch-all "Mind the Gaps (minted)" collection, with the person attached for filmography gaps.
What is still open is a *nicer home than the catch-all collection* and bulk materialization. SetCompletion is the clean case because a BoxSet is a real owned container with
LinkedChild membership, so a virtual movie renders where it belongs; CreatorWorks (an owned person's
unowned films) and Recommendation (similar titles) have no such natural container, hence the catch-all.

This is a design choice, not a missing API. `ILibraryManager.CreateItem` works for any `Movie` and
`ICollectionManager` can create BoxSets, so the plugin can manufacture a better container. Two
candidate homes for CreatorWorks:

1. A synthetic per-person BoxSet (for example "Robert Zemeckis (gaps)"), tagged and reversible with
   the same machinery the collection minter uses. Downside: it clutters the library with collections
   that are not how people think of a filmography.
2. The person's own filmography view. Jellyfin already renders a person page by querying every item
   that lists that person. If a minted virtual movie carries the person in its People list, it would
   flow into that existing view with no synthetic container at all. This is the nicer option and is
   discussed as a near-term idea below.

Recommendation is the weakest case to materialize: "similar titles" has the least natural home, so it
is best kept as a dashboard-only suggestion.

## Near-term idea: gaps on the native person page

When you open a cast or crew member in the Jellyfin UI, the person page shows the items in your
library that list that person. The plumbing is a query by person, not a hand-built list.

That means CreatorWorks materialization could reuse it: mint each unowned film as a virtual `Movie`
(`IsVirtualItem=true`, `Path=null`, populated from the credit) and attach the person to its People
list. The existing person-page query would then include those films alongside the owned ones, with no
synthetic BoxSet. The person's filmography becomes the container.

Verified against the server source (10.11): a person-items query does not filter virtual items unless
the caller sets `IsVirtualItem`/`ExcludeLocationTypes`, which the person page does not
(`BaseItemRepository.TranslateQuery` only applies the filter when `IsVirtualItem.HasValue`). A library
scan also preserves pathless virtual items (`Folder.ValidateChildrenInternal2` only deletes
`IsFileProtocol` items). So a minted virtual movie with the person attached should appear on that
person's page and survive scans, with no server change.

The one-off Mint button on the dashboard (filmography rows mint into a catch-all collection and attach
the person) is the live probe for this. Remaining dependencies: the greyed "Missing" badge needs
upstream ask A ([jellyfin-web #8049](https://github.com/jellyfin/jellyfin-web/pull/8049)), and there is
still no per-user display gate (minted items show for everyone).
Reconciling a minted film once the real file is owned is handled: every scan ends with a reconcile
pass that drops minted placeholders whose TMDB id the library owns for real.

A fully native treatment (a distinct "Gaps" shelf on the person page) would need a jellyfin-web change
on top of the minting. The plugin-only version leans on the existing query.

## Scaling work (batching and rate limiting)

Confirmed against the server source: `CreateItem` already batches its own save, but
`ICollectionManager.AddToCollectionAsync` saves once per call, so the bulk minter does one DB save per
missing movie. For large collections it should collect a BoxSet's movies and call `CreateItems` plus a
single `AddToCollectionAsync` per BoxSet. The scan's external clients cache TMDB collection and person
lookups but do not throttle; recommendations and the series clients (TVmaze, TheTVDB, Trakt) are
uncached, bounded only by the caps in `GapScanLimits`. Every hand-rolled client (Trakt, TVmaze, TheTVDB,
MusicBrainz, OpenLibrary) and the TMDB availability fetch now send through `Services/Http/HttpRetry`, which
retries 429/502/503/504 and transient connection failures, honours `Retry-After`, and backs off (capped so
a scan never stalls long); TMDB-via-TMDbLib uses that library's own `MaxRetryCount` for 429. On top of that,
`Services/Http/ServiceCircuit` is a per-service breaker: once a service gives up on five requests in a row it
fast-fails for a two-minute cooldown, so a down or hard-rate-limited service stops being retried for every
owned item and the scan moves on to the others (a success closes it again). The scan also checkpoints to disk
as it goes (`GapStore.SaveCheckpoint`): throttled, after each source, and immediately when a circuit first
trips, so a crash or shutdown mid-scan keeps the batch instead of losing everything. That is the **reactive**
half. The **proactive** half (a per-client minimum interval between requests) is still open and matters most
for MusicBrainz, which asks for no more than one request per second and is currently paged with no
inter-request delay; today a burst there is caught only by the reactive retry and circuit. A source could also
consult `ServiceCircuit.IsOpen` to skip its remaining work (and its per-item warnings) entirely once a service
is given up on. Adding a small per-client pacing gate is the remaining fix; not urgent at typical library sizes.

The mint paths run in the background like the scan: `MintRunner` runs multi-select `MintGaps`,
per-row `MintGap`, and "Remove minted" off the request thread (reporting 0-100 progress), and the UIs
poll `MintStatus`. Reconciliation is a cheap synchronous tail on the scan (one owned-movies query plus
targeted deletes), so it does not need its own progress. Availability is its own background pass
(`AvailabilityRunner`), not part of the scan, so the scan stays detection-only and never blocks a request.

## Priorities (suggested, not committed)

- Get upstream ask A ([jellyfin-web #8049](https://github.com/jellyfin/jellyfin-web/pull/8049)) merged so
  the collection movies already mintable today get their native greyed "Missing" badge.
- Harden the experimental Books source against real OpenLibrary data (author disambiguation, missing
  years; see the backlog item) before promoting it out of experimental.
- If in-place CreatorWorks rendering is wanted, prototype the person-page materialization above; it is
  a better container story than synthetic collections.

## Backlog

- **Shareable links are enormous (may exceed URL limits).** `shareUrl` (the Copy-link button and the
  Markdown export's summary link) encodes the whole `captureView` object as URI-encoded JSON in a `cgview`
  hash param. The bulk is `disabledProviders` (a map of every disabled streaming provider, which can be 100+
  names), so a link can run to thousands of characters and risk browser/proxy URL caps. Options to noodle on:
  store *enabled* providers as a short diff (or omit provider state entirely, since it is server-specific and
  a link pasted to a different server has different providers); drop default-valued fields before encoding;
  compress the JSON (deflate plus URL-safe base64) before the param; or persist the view server-side under a
  short token and put only the token in the URL (a real short-link, the most robust against URL limits but it
  adds a store and an endpoint). Likely combine: omit/short-diff providers and drop defaults first, compress
  if still large.
- **Collection completion flags owned-but-mistagged movies as missing (left as a real gap on purpose).**
  `CollectionGapSource` diffs a TMDB collection's parts against the ownership index, which is keyed by
  provider id, so a movie that is in the owned BoxSet but whose library item has no (or a mismatched) TMDB id
  is reported missing even though it is owned (seen with "Jack Reacher: Never Go Back"). This is deliberately
  not "fixed" by fuzzy title-and-year matching against the BoxSet's children: that would mask the bad or
  missing set metadata that should actually be corrected. The intended resolution is to **surface** it, not
  hide it, which the Diagnose action does (it shows the owned item carrying the wrong or no id so you can fix
  the metadata and rescan).
- **Deepen the "Diagnose" action.** Both the per-gap **Diagnose** popup and a **library-wide audit** are
  built (`GapDiagnostics`, the `GET Diagnose` and `GET DiagnoseAudit` endpoints). For a movie or show gap
  the popup finds the owned items that match by title or already carry the gap's id and lays the gap and
  those candidates out as a comparison table with linked TheMovieDb/IMDb/TheTVDB ids, so the mis-tagged
  "Never Go Back" case (an owned item carrying a different film's id) is obvious and fixable; the audit runs
  the same check across the whole library and downloads it as Markdown (the mismatches plus every duplicate
  TheMovieDb id). The library-only check is synchronous; the **Deeper analysis** button adds a networked pass.
  Done in the deeper pass: **cross-provider disagreement** (`ApplyCrossProviderDisagreement`). After resolving
  both the gap's and each candidate's external ids via `TmdbClient.GetExternalIdsAsync`, it compares the IMDb
  ids of same-title candidates: a match confirms the same film under the wrong TheMovieDb id, a mismatch marks
  a different film that merely shares the title, and when every same-title candidate is a different film the
  verdict downgrades to genuinely missing. That tells the two cases apart, which a title-keyed library match
  cannot. Deliberately not done: inspecting the owned BoxSet's children for collection gaps. The library-wide
  title match already includes BoxSet members, so an in-collection mistag with the same title is already
  surfaced as "owned under the wrong id"; matching a differently-titled member would need fuzzy title/year or
  position matching, which is the masking approach rejected above (surface it, do not guess).

- **Diagnose for Music and Books (done; ids permitting).** `GapDiagnostics` now matches on a per-kind
  primary id (`PrimaryProvider`): TheMovieDb for movies and shows, the MusicBrainz release-group for albums,
  the OpenLibrary work for books, plus the normalized-title and year logic shared across kinds. The per-gap
  Diagnose popup is reachable for album and book rows, and its messages name the right provider. The
  library-wide audit stays movies and shows only (its duplicate-id section is TheMovieDb-specific). The
  remaining limit is the ids, not the code: the diff only finds an owned album or book when the library item
  carries the MusicBrainz release-group or OpenLibrary work id, so on thinly-tagged libraries it falls back to
  the title and year match. `CuratedSetGapSource` completes the movies of
  a studio or keyword, chosen in settings with a type-ahead chip picker (only the resolved TMDB id is
  stored) or **auto-seeded** from the studios most common on owned movies and series (`AutoSeedStudios`),
  grouped by the set name with per-set page and gap caps. The one input type it does not cover is TMDB
  Lists: paste a list id and complete it the same way (TMDbLib `GetListAsync`).
  Keyword auto-seeding (from keywords on owned items) is also possible but lower value than studios.
- **Grow the curated-set chip picker (Music done; Books pending).** The settings page picks curated sets
  with a kind-agnostic type-ahead chip control (`setupChips`): you search a provider (`GET CuratedSearch`),
  pick a match, and only the id is stored; `GET CuratedResolve` turns the stored ids back into named chips on
  load, so the numeric id is never shown. Studios and keywords go through `TmdbClient`, and **Discogs labels**
  (the `label` kind) now go through `DiscogsClient.SearchLabelsAsync`/`GetLabelNameAsync`, stored in
  `DiscogsLabelIds`, driving `DiscogsLabelGapSource`. Books remain: they need a curated-book **gap source**
  first (there is no curated-book set today, only the author bibliography from owned books), for example an
  OpenLibrary subject or author set; once that source and a config field exist, adding the chip kind is just
  a `CuratedSearch`/`CuratedResolve` branch plus a `setupChips` instance and markup, the same as the label
  kind.
- **Discogs source for music (label sets done; more to do).** `DiscogsClient` (hand-rolled over
  `Services/Http/HttpRetry`, authenticated with a `DiscogsToken` from config) and `DiscogsLabelGapSource`
  complete a curated record **label** set: for each configured label id, it lists the label's releases and
  emits a `SetCompletion` Music gap per unowned release (`DiscogsLabelMapper`, grouped under "Labels" in the
  report). Opt-in and experimental. Two known limits remain: (1) the ownership diff is by Discogs id, so it
  only recognises an owned release when the library item carries a Discogs provider id; a normalized
  artist-plus-title fallback (which the ownership index does not support today) would make it useful on
  MusicBrainz-tagged libraries. (2) The set is shown as "Label {id}" until the label name is resolved
  (`DiscogsClient.SearchLabelAsync` already does name-to-id for the future chip picker; the reverse, an id
  to name lookup, is a small `/labels/{id}` call). Discogs could also widen
  `MusicDiscographyGapSource`/`MusicArtistWorksGapSource` with richer release/artist matching than
  MusicBrainz alone.
- **Extend fill-up scanning to recommendations and series.** Filmography fills up over runs:
  `PeopleGapSource` orders people most-credited-first (configurable `MaxFilmographyPeople` cap), records
  the people scanned this cycle in `ScanCursorStore`, and advances to the next un-scanned batch each run
  (starting a fresh cycle once everyone is covered); the engine then carries prior CreatorWorks gaps
  forward across scans when they are still unowned and were not re-emitted (bounded, gated on a
  filmography source being enabled), so the whole cast and crew accumulates instead of the un-scanned
  creators' gaps vanishing. The same cursor-plus-carry-forward pattern could be applied to recommendation
  seeds and the series cross-checks so those caps also fill up over runs rather than rescanning the same
  slice. Watch report growth: full filmography coverage of a large library can be tens of thousands of
  gaps (bounded by the 50k accumulation cap), which the flat dashboard render may eventually want
  virtualizing.
- **Send a gap to Radarr/Sonarr.** The report dead-ends at a link; power users running the arr stack
  want one click from "missing" to "queued". Gaps already carry the ids these need: a movie gap has a
  TMDB id (Radarr `POST /api/v3/movie` takes a tmdbId), a missing episode carries its series' TheTVDB id
  (Sonarr is keyed on tvdbId). A `Services/Arr/{RadarrClient,SonarrClient}` (hand-rolled like the
  Trakt/TVmaze clients), config for base URL plus API key and a default quality profile and root folder
  per service, a per-row button next to Mint, and the multi-select bar for bulk. Larger effort: two
  clients, profile/root-folder selection, error surfacing, and movie-versus-series routing by `TargetKind`.
- **Send a gap to Jellyseerr/Overseerr.** The lighter-weight half of the above and the better first
  target: many Jellyfin households already run Jellyseerr as the request front door (with approval
  workflows for non-admins), and it is keyed purely on TMDB ids, which every gap already has. One client
  (`Services/Seerr/SeerrClient`), one endpoint (`POST /api/v1/request` with `{mediaType, mediaId}`), one
  config pair (URL plus API key), and it covers both movies and series with no profile/root-folder
  plumbing. Both integrations are opt-in and config-gated like the Trakt/TheTVDB cross-checks: the
  "Send to ..." button only appears when that service is configured, so nothing assumes one is installed.
- **Bulk mint across all enabled patterns.** Minting today is per-row and multi-select from the report.
  A one-click "mint every gap of a pattern" (or every gap in a domain) needs the per-cell container
  strategy (collections use a BoxSet, episodes are native in core, CreatorWorks/Recommendation use the
  catch-all collection), i.e. promoting `MintGapAsync`'s per-cell guards into a small "is this
  materializable yet, and what's its container" lookup.
- **Background scheduled minting.** Minting is request-only today; a scheduled task could keep a chosen
  set of patterns/domains materialized automatically (mint new gaps, reconcile owned ones) on the same
  cadence as the scan. It would reuse `MintRunner` and the same per-cell container strategy as the bulk
  item above; the open question is the selection UI (which patterns to auto-mint) and guardrails so it
  cannot flood a library.
- **Discover installed providers' credentials.** We already reuse the host's *link* providers
  (`IExternalUrlProvider`) with no credentials. Reusing other plugins' *credentialed* clients (the
  TheTVDB/Trakt metadata plugins' configured keys) the way we lean on the in-box TMDB provider has no
  supported host SPI today: it would mean reflecting into another plugin's configuration (fragile,
  version-coupled, effectively a soft dependency) or standardizing an upstream SPI. This pairs with
  upstream Discussion C (expose the host TMDB client/key via the NuGet); a broader "providers expose a
  credentialed client" ask would cover the rest. Until then the plugin keeps its own keys.
- **Coverage on more sources.** Coverage badges ("6 of 9 owned, 67%") show on
  BoxSet/collection and series groups, and each tab has a domain rollup line summing gap counts, group
  counts, and the owned-of-total aggregate. Cross-check-only series (not in the server's missing list) and
  the open-ended curated studio/keyword sets carry no badge by design; giving cross-check series a count
  would mean counting their owned episodes the same way the library source now does.
- **Native in-page polish via the frontend-customization ecosystem (alternative to the upstream PRs).**
  The community JavaScript Injector and File Transformation plugins (the same stack CineHover uses) let a
  plugin push CSS/JS into jellyfin-web without forking it. That gives a non-upstream path to the two
  things currently gated on jellyfin-web changes: (a) render our minted virtual items as greyed "Missing"
  placeholders (otherwise waiting on jellyfin-web PR A / #8049), and (b) add a dedicated "Gaps" shelf to
  native browse pages, the person page for Creator Works gaps and the studio page (the `#/list?studioId=`
  view) for curated-set gaps, so each appears in context as its own section; and (c) inject a "Gaps" entry
  into a library's overflow ("...") context menu, next to Refresh metadata / Edit images, that jumps to the
  report scoped to that library (its domain, or its studios/series). The minting that creates and
  reconciles the items is already done; this is only the presentation layer. Trade-offs: it adds a
  soft dependency on two third-party plugins the user must install, and injected JS targets jellyfin-web's
  DOM, so it is version-fragile and needs upkeep across web releases. Best shipped as an optional
  enhancement (ship the injectable assets, document the dependency), not a core requirement.
  - **Discovery (2026-06).** Mechanism: the maintainable path is the **File Transformation** plugin
    ([IAmParadox27/jellyfin-plugin-file-transformation](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation),
    2.5.11 as of June 2026), not editing `index.html` on disk. It registers a callback invoked each time
    `index.html` is served and rewrites it in memory (non-destructive, reversible, Docker-friendly; several
    plugins can stack). Integration is **reflection-based and a soft dependency** (the assembly is in an
    isolated load context, so it cannot be a compile reference): find the assembly whose name contains
    `.FileTransformation`, get type `Jellyfin.Plugin.FileTransformation.PluginInterface`, and invoke
    `RegisterTransformation(payload)` where the payload is `{ id, fileNamePattern, callbackAssembly,
    callbackClass, callbackMethod }`; our static callback receives `{ "contents": "<index.html>" }` and
    returns the modified string. We would use it to inject one `<script>`/`<link>` that loads our own bundled
    asset, then our JS runs in the web context. The JavaScript Injector plugin is a convenience layer over
    the same mechanism, not required if we register the transformation ourselves.
  - **Maintainability verdict.** The injection mechanism is stable and reversible, but what the injected JS
    *does* is the fragile part. jellyfin-web is a webpack SPA: its internal modules (`cardBuilder`, the
    shelf/row components, dialogs, the router internals) are **not exposed on `window`**, so an injected
    script cannot import them. A native-looking "Gaps shelf" would have to hand-roll markup mimicking the
    current card DOM and select existing elements by class/structure, which breaks across web releases. The
    n00bcodr plugins confirm the cost: they pin to "Jellyfin 10.11+" and follow strict one-version
    compatibility, shipping an update per web release. Plan for recurring upkeep.
  - **Core JS we could reuse (the standing question).** jellyfin-web ships **no formal, documented, stable
    public JS API** for plugins. The de-facto stable surface is small, and the dashboard already uses all of
    it: `ApiClient` (from jellyfin-apiclient-javascript: `getUrl`, `ajax`, `getCurrentUserId`,
    `serverAddress`) and `Dashboard` (`navigate`, `showLoadingMsg`/`hideLoadingMsg`, `alert`,
    `processPluginConfigurationUpdate`), plus the page lifecycle (`pageshow`/`viewshow`, `data-role="page"`)
    and the `emby-*` custom elements. Everything richer lives in the bundle, unexposed. So there is no
    untapped core library to lean on; for injected in-page work we would be DOM-scraping, not calling stable
    helpers. (This is also why our config and report pages stick to `ApiClient` + `Dashboard`: that is the
    durable contract.)
  - **If pursued (post-1.0, optional).** Prefer File Transformation over `index.html` edits. Sequence by
    fragility: first the cheapest, most durable wins (a library "..." context-menu "Gaps" entry; greying
    minted items via CSS keyed off a stable marker), and only later attempt native-looking shelves (highest
    fragility). Ship the injectable assets as an opt-in, document the third-party dependency, and budget an
    upkeep pass per jellyfin-web release.
- **Modularize the dashboard (`Web/mindthegaps.html`).** The report page has grown into one large file
  with inline CSS and a long inline script (filters, tree render, availability, dismissals, saved views,
  export, acquisition, mint). It works but is hard to navigate. Split the source at least at the
  authoring level: extract the CSS and the script into separate files grouped by concern (filters/state,
  tree render, row actions, availability, views/export), then either concatenate them into the single
  embedded resource at build time with an MSBuild target, or serve them as their own resources the page
  references. Keep it shipping as few artifacts as before; this is purely maintainability, no behavior
  change, and wants a small test/build-pipeline change rather than a code rewrite.
- **Mint virtual placeholders for non-movie kinds.** Today only movie gaps are minted. Per kind: missing
  **episodes** and **seasons** need nothing, the server already synthesizes virtual missing episodes and
  the report links straight to them. **Albums** are the natural unit to mint for Music (a pathless virtual
  `MusicAlbum` tagged with the `MintedMarker`, placed in its artist or a catch-all, mirroring the movie
  minter); **books** likewise as virtual `Book` items. Individual **songs/tracks** are low value (the
  album is the unit). The Music and Books gap sources exist, so the gaps display in the report; the
  minting itself is deferred to post-1.0, to be built on top of the bulk-mint container refactor (branch
  B, a post-1.0 PR) where the minter can be generalized from movie-only to kind-aware and validated
  against a running server, rather than written as speculative, untestable code on main now.
- **Harden the Books source against real OpenLibrary data (largely done).** Real OpenLibrary responses
  have three rough edges. (1) The author search's first result is often the wrong namesake (searching "Frank
  Herbert" returns Frank Herbert Hayward first; the Dune author OL79034A is further down). Now handled two
  ways: `BooksBibliographyGapSource` resolves the author from the **owned work** when the book carries an
  OpenLibrary work id (`OpenLibraryClient.GetWorkAuthorKeyAsync` reads `/works/{key}.json`), skipping the
  name search entirely; otherwise `OpenLibraryAuthorMatcher.Pick` chooses by shortest exactly-matching name
  then work_count (the prolific real author), not docs[0]. (2) The author-works list carried no publish
  date, so book gaps got no year; the source now lists works via the search endpoint
  (`OpenLibraryClient.GetAuthorWorksBySearchAsync` over `/search.json?author_key=...&fields=...,first_publish_year`),
  which returns years in one call. (3) Several works share a title; `OpenLibraryMapper` now de-duplicates by
  normalized title, keeping the earliest-published edition and treating a title as owned when any of its
  works is owned. What remains before promoting Books out of experimental: capture fixtures for the two new
  endpoints (a `/works/{key}.json` and a `/search.json?author_key=` response) so they are tested against
  real data, optionally a config-time author-to-key mapping as a manual override, and real-world validation
  across a varied library.
- **Shard the report by domain (storage and transfer).** The whole report lives in one `gaps.json` and
  is shipped to the browser whole; with more sources and the filmography backfill (toward the 50k cap) it
  can reach multiple MB, which is slow to load, parse, atomically save on every scan and availability
  checkpoint, and transfer/render in the dashboard. Split by `MediaDomain` (Movies/Shows/Music/Books), not
  by source: the dashboard already groups by domain and the cross-source de-dup that matters (the three
  series sources sharing episode ids) stays within the Shows shard, so it is not broken; a per-source
  split would break it. Touches `GapStore` (multi-file atomic writes), `GapEngine` (per-domain de-dup and
  carry-forward), and `AvailabilityRunner`. The **transfer half is already in place**: the API serves the
  report per pattern (`Gaps?pattern=X` plus a lightweight `Summary` for the tab counts), so the browser
  loads only the active tab, and the Type filter is domain-primary with no "All" option. The open part is
  the **storage half**: splitting the persisted `gaps.json` by domain so a scan and each availability
  checkpoint do not rewrite one multi-MB file. Pairs with the report-virtualization note.
