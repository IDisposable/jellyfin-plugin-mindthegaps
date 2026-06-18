# Mind the Gaps: roadmap and status

> A promised-versus-actual map of the plugin. Written to keep the difference between what the plugin
> detects and what it materializes from drifting into tribal knowledge. Status reflects the code as it
> stands, not the marketing.

## The one distinction that explains everything

There are two separate layers, and only one of them is narrow:

| Layer | What it is | Scope today |
|---|---|---|
| Detection | The gap engine plus its sources scan the library and produce the dashboard todo list of what is missing or related | Broad: all three patterns, movies and series episodes, four providers |
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
the settings page no longer fires any minting (it keeps only "Remove minted" and its preview). The
minter is idempotent, tags everything with the `MindTheGapsMinted` provider id, never deletes files,
queues a metadata + image refresh on each minted item, and is fully reversible. Reconciliation (drop a
minted placeholder once the library owns the real file) runs automatically at the end of every scan,
on both the scheduled task and the background runner. A whole collection is minted by selecting its
missing rows and hitting "Mint selected"; there is no separate SetCompletion-only bulk button anymore.

## Promised but not built

| Promise (README / CLAUDE / ADRs) | Status |
|---|---|
| Music, Books, MusicVideos domains (`MediaDomain`; discography, book series, bibliography) | Enum values exist, zero sources. Movies and Shows are implemented. |
| Materialize CreatorWorks or Recommendation gaps in bulk | Per-row and multi-select mint do it one batch at a time; no one-click "mint every gap of a pattern" |
| Native virtual items in core (the three upstream asks) | PR A filed as jellyfin-web 8049; Discussions B and C are drafts |
| Per-user display gate for minted items | Not possible from a plugin; minted items show for everyone |
| `IGapSource` as a core SPI so third-party gap plugins can exist | Deferred on purpose (ADR-0002); every source ships in this plugin |
| Greyed "Missing" badge on minted movies | Needs jellyfin-web PR A merged |

## Filling CreatorWorks and Recommendation gaps

Detecting these is done, and they are now materialized one at a time (the per-row and multi-select
Mint buttons) into the catch-all "Mind the Gaps (minted)" collection, with the person attached for
filmography gaps. What is still open is a *nicer home than the catch-all collection* and bulk
materialization. SetCompletion is the clean case because a BoxSet is a real owned container with
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
jellyfin-web PR A (8049), and there is still no per-user display gate (minted items show for everyone).
Reconciling a minted film once the real file is owned is now handled: every scan ends with a reconcile
pass that drops minted placeholders whose TMDB id the library now owns for real.

A fully native treatment (a distinct "Gaps" shelf on the person page) would need a jellyfin-web change
on top of the minting. The plugin-only version leans on the existing query.

## Scaling work (batching and rate limiting)

Confirmed against the server source: `CreateItem` already batches its own save, but
`ICollectionManager.AddToCollectionAsync` saves once per call, so the bulk minter does one DB save per
missing movie. For large collections it should collect a BoxSet's movies and call `CreateItems` plus a
single `AddToCollectionAsync` per BoxSet. The scan's external clients cache TMDB collection and person
lookups but do not throttle; recommendations and the series clients (TVmaze, TheTVDB, Trakt) are
uncached and unthrottled, bounded only by the caps in `GapScanLimits`. A large library could burst past
the Trakt and TVmaze limits. Adding a per-client delay or backoff is the fix. Neither is urgent at
typical library sizes; both matter before a wide release.

The mint paths run in the background like the scan: `MintRunner` runs multi-select `MintGaps`,
per-row `MintGap`, and "Remove minted" off the request thread (reporting 0-100 progress), and the UIs
poll `MintStatus`. Reconciliation is a cheap synchronous tail on the scan (one owned-movies query plus
targeted deletes), so it does not need its own progress. Availability is its own background pass
(`AvailabilityRunner`), not part of the scan, so the scan stays detection-only and never blocks a request.

## Priorities (suggested, not committed)

- Get jellyfin-web PR A (8049) merged so the collection movies already mintable today get their badge.
- Decide whether Music and Book are real roadmap or should be trimmed from the docs to stop
  over-promising.
- If in-place CreatorWorks rendering is wanted, prototype the person-page materialization above; it is
  a better container story than synthetic collections.

## Backlog

- **Curated TMDB Lists (continues the studio/keyword work).** Studio and keyword widening shipped:
  `CuratedSetGapSource` completes the movies of a studio or keyword, configured by **name** (resolved to
  TMDB via `SearchCompanyAsync`), by TMDB id, or **auto-seeded** from the studios most common on owned
  movies and series (`AutoSeedStudios`), grouped by the set name with per-set page and gap caps. The one
  remaining input type is TMDB Lists: paste a list id and complete it the same way (TMDbLib `GetListAsync`).
  Keyword auto-seeding (from keywords on owned items) is also possible but lower value than studios.
- **Extend fill-up scanning to recommendations and series.** Filmography now fills up over runs:
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
- **Coverage on more sources (mostly done).** Coverage badges ("6 of 9 owned, 67%") show on
  BoxSet/collection and series groups, and each tab has a domain rollup line summing gap counts, group
  counts, and the owned-of-total aggregate. Cross-check-only series (not in the server's missing list) and
  the open-ended curated studio/keyword sets carry no badge by design; giving cross-check series a count
  would mean counting their owned episodes the same way the library source now does.
- **Shareable view URLs.** Named saved views shipped: a "Saved views" control snapshots the active tab
  and every filter (type, specials, upcoming, dismissed, streamable, monetization, providers, search) as
  a named preset in `localStorage`, re-applied in one click. What remains is the shareable part: encode a
  view into the page URL so it survives a bookmark or a paste to another browser/user, which the saved
  presets (per browser) do not. Fiddly inside Jellyfin's hash router, hence deferred.
- **Native in-page polish via the frontend-customization ecosystem (alternative to the upstream PRs).**
  The community JavaScript Injector and File Transformation plugins (the same stack CineHover uses) let a
  plugin push CSS/JS into jellyfin-web without forking it. That gives a non-upstream path to the two
  things currently gated on jellyfin-web changes: (a) render our minted virtual items as greyed "Missing"
  placeholders (otherwise waiting on jellyfin-web PR A / #8049), and (b) add a dedicated "Gaps" shelf to
  native browse pages, the person page for Creator Works gaps and the studio page (the `#/list?studioId=`
  view) for curated-set gaps, so each appears in context as its own section. The minting that creates
  and reconciles the items is already done; this is only the presentation layer. Trade-offs: it adds a
  soft dependency on two third-party plugins the user must install, and injected JS targets jellyfin-web's
  DOM, so it is version-fragile and needs upkeep across web releases. Best shipped as an optional
  enhancement (ship the injectable assets, document the dependency), not a core requirement.
- **Modularize the dashboard (`Web/mindthegaps.html`).** The report page has grown into one large file
  with inline CSS and a long inline script (filters, tree render, availability, dismissals, saved views,
  export, acquisition, mint). It works but is hard to navigate. Split the source at least at the
  authoring level: extract the CSS and the script into separate files grouped by concern (filters/state,
  tree render, row actions, availability, views/export), then either concatenate them into the single
  embedded resource at build time with an MSBuild target, or serve them as their own resources the page
  references. Keep it shipping as few artifacts as before; this is purely maintainability, no behavior
  change, and wants a small test/build-pipeline change rather than a code rewrite.
Shipped from earlier backlog: the per-provider availability filter, multi-select mint, the "Hide items
with no sources" filter, and the background "Look up where to watch" pass (the old "batch availability
past the lookup cap" item: a standalone, resumable pass over the persisted report, grouped by title, with
availability and resolved ids carried forward across rescans by gap id). Also shipped since: per-gap
resolutions, the "Hide upcoming" filter, alphabetical grouping for Creator Works and Recommendations,
open-in-Jellyfin links, the version-stamped report with a rescan nudge, and the settings-page reorg.
