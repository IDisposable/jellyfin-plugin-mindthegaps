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

- **Studio, keyword, and curated-list collection widening (top priority).** SetCompletion today is
  anchored to formal TMDB BoxSets, which only cover named franchises. Users think in broader sets: "every
  A24 film", "every Studio Ghibli film", "the films in this themed list". TMDB exposes the inputs already:
  `discover/movie` with `with_companies` (studio) or `with_keywords`, and TMDB Lists. Seed the queries
  from what is already owned (collect the studios and keywords present on owned items, or let the user
  paste a TMDB list id), diff the result against ownership exactly like a BoxSet, and emit SetCompletion
  gaps tagged with the set name. This is a large widening of detection with zero engine or report change:
  it is another `IGapSource` producing the existing `GapItem` shape, grouped like collections. Needs a
  config surface for which studios/keywords/lists to track and a per-set cap so a broad studio does not
  flood the list.
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
- **Export the report to Markdown.** A single "Export" action that renders the current (filtered) report
  as Markdown with embedded links: each gap as a bullet with its provider links (TMDB/IMDb/TheTVDB/
  JustWatch), its "where to watch" providers, and an open-in-Jellyfin link for items already held, grouped
  the way the dashboard groups them. Pure serialization of the in-memory report, no new data, so it is
  small; it just needs a controller route returning `text/markdown` and a download button. Good for
  pasting a wishlist into a forum, an issue, or a notes app.
- **Better dismissal: not-interested and snooze-until-release.** The resolve feature marks a gap "not
  really missing"; dismissal is the sibling for "real gap, do not want it" and "want it, but not yet".
  Two states are enough (no free-form taxonomy of reasons): *not interested* permanently drops a gap you
  deliberately do not want, and *snooze until release* hides an upcoming/unreleased gap and
  auto-resurfaces it after its release date. They differ because snooze expires on a date; everything
  else is covered by the existing optional note. Small extension of `ResolutionStore` (add a state and an
  optional snoozed-until date to the stored record) and the report filter; the persistence and overlay
  plumbing already exist.
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
- **Synthesize parent hierarchy for season/episode host links.** `ExternalLinkEnricher` hands the host a
  throwaway `BaseItem` carrying the gap's own provider ids. That satisfies the movie/series/person
  branches of core's url providers, but their season/episode branches reach through `season.Series` /
  `episode.Series` to read the *series* id, which a bare synthetic item lacks, so those yield nothing and
  the `ProviderLinks` fallback covers them. We could set a minimal parent (a `Series` with the owning
  item's ids, plus index numbers) so the host emits proper season/episode urls, but the series ids live
  on the owning item (`SourceItemId`), not the gap's `ProviderIds`, so it needs a library lookup per
  episode gap. Low value (the fallback already builds these), so deferred.
- **Discover installed providers' credentials.** We already reuse the host's *link* providers
  (`IExternalUrlProvider`) with no credentials. Reusing other plugins' *credentialed* clients (the
  TheTVDB/Trakt metadata plugins' configured keys) the way we lean on the in-box TMDB provider has no
  supported host SPI today: it would mean reflecting into another plugin's configuration (fragile,
  version-coupled, effectively a soft dependency) or standardizing an upstream SPI. This pairs with
  upstream Discussion C (expose the host TMDB client/key via the NuGet); a broader "providers expose a
  credentialed client" ask would cover the rest. Until then the plugin keeps its own keys.
- **Preserve scroll and expansion across row actions.** Resolving/clearing (and any action that calls
  `applyAndRender`) rebuilds `#cgList`, so scroll position and which groups were expanded are lost. Two
  ways: snapshot the scroll offset and the set of collapsed group keys before re-render and restore them
  after, or (cleaner) update just the affected row in place (remove it, or grey it and swap Resolve for
  Clear plus the note) without a full re-render. The in-place update also avoids the resolutions re-fetch.
- **Show every recommending source per target.** The engine dedupes recommendations by target, so each
  recommended title keeps only the one seed that first surfaced it; the report pivot shows that single
  source. Listing all owned titles that recommend a target needs the engine to aggregate sources per
  target instead of deduping them away.
- **Coverage scoring.** A per-container completeness readout: "this BoxSet is 6 of 9 owned (67%)", "this
  series is missing 3 of 40 episodes", a domain rollup at the top of each tab. The data is already in the
  report (gaps are counted against owned containers); this is a presentation layer that turns the todo
  list into a progress view, and it gives the dashboard a headline number per group rather than only a
  flat list. Small, report-only.
- **Finer alphabetical hierarchy (low priority).** Single-letter buckets get large on big libraries (an
  "A" group can hold 100+ creators or titles). A second level (for example "Ab", "Ad", "Al") or a sticky
  A-Z jump bar would make a long group navigable. Dashboard-only grouping change, low priority.
- **Saved views.** Remember named filter/sort combinations (for example "movies with sources, hide
  upcoming, sorted by year") and let the report deep-link to one via the URL, so a particular slice is one
  click or one bookmark away instead of re-toggling filters each visit. The filters already live in
  `localStorage`; this promotes them to named, shareable presets. Small, dashboard-only.
- **Webhook and notification on scan or availability completion (bottom, luxury).** Fire a webhook
  (Discord/Gotify/generic JSON) when a scan or the background "where to watch" pass finishes, with a small
  summary payload (counts, new gaps). Hangs off the existing `MintRunner`/`AvailabilityRunner` completion
  points; config is a URL plus which events. Genuinely a nice-to-have, lowest priority.

Shipped from earlier backlog: the per-provider availability filter, multi-select mint, the "Hide items
with no sources" filter, and the background "Look up where to watch" pass (the old "batch availability
past the lookup cap" item: a standalone, resumable pass over the persisted report, grouped by title, with
availability and resolved ids carried forward across rescans by gap id). Also shipped since: per-gap
resolutions, the "Hide upcoming" filter, alphabetical grouping for Creator Works and Recommendations,
open-in-Jellyfin links, the version-stamped report with a rescan nudge, and the settings-page reorg.
