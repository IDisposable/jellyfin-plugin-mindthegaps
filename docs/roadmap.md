# Mind the Gaps: roadmap and status

> A promised-versus-actual map of the plugin. Written to keep the difference between what the plugin
> detects and what it materializes from drifting into tribal knowledge. Status reflects the code as it
> stands, not the marketing.

## The one distinction that explains everything

There are two separate layers, and only one of them is narrow:

| Layer | What it is | Scope today |
|---|---|---|
| Detection | The gap engine plus its sources scan the library and produce the dashboard todo list of what is missing or related | Broad: all three patterns, movies and series episodes, four providers |
| Materialization | The minter creates pathless virtual placeholder items inside the library so a gap renders in place | Narrow: only SetCompletion collection movies, into BoxSets |

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
| AvailabilityService (TMDB) | n/a | "where to watch", fetched lazily per item, or (opt-in) for every gap during the scan | Done |

Supporting pieces that are done: the dashboard todo list (filter, search, external links per item),
the scheduled scan, the dedupe engine, and the per-item availability lookup. CreatorWorks and
Recommendation gaps are detected and shown today, for movies and series. They render as a list with
links, not as in-library placeholders.

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
targeted deletes), so it does not need its own progress. Scan-time availability enrichment runs inside
the already-backgrounded scan, so it never blocks a request either.

## Priorities (suggested, not committed)

- Get jellyfin-web PR A (8049) merged so the collection movies already mintable today get their badge.
- Decide whether Music and Book are real roadmap or should be trimmed from the docs to stop
  over-promising.
- If in-place CreatorWorks rendering is wanted, prototype the person-page materialization above; it is
  a better container story than synthetic collections.

## Backlog

- **Batch availability past the lookup cap.** Scan-time enrichment stops at `MaxAvailabilityLookups`, so
  beyond that gaps stay un-enriched and "Hide items with no sources" cannot see them. Plan: make
  enrichment a standalone pass over the *persisted* report (no rescan) that loads `gaps.json`, enriches
  the next batch of un-enriched watchable gaps, saves, and reports how many remain; run it repeatedly (a
  "look up more sources" action and/or an auto-continuing background loop with rate-limit delays) to
  drain. Preserve enrichment across full rescans by carrying `Availability` forward by `GapItem.Id` so a
  rescan does not wipe it (and so the scan only needs to look up genuinely new gaps). Add a "refresh all"
  mode to re-fetch for staleness. Reuse the background-runner + status pattern (`GapScanRunner`/`MintRunner`).
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
- **Show every recommending source per target.** The engine dedupes recommendations by target, so each
  recommended title keeps only the one seed that first surfaced it; the report pivot shows that single
  source. Listing all owned titles that recommend a target needs the engine to aggregate sources per
  target instead of deduping them away.

Shipped from earlier backlog: the per-provider availability filter, multi-select mint, and the
streamable filter (opt-in scan-time availability, surfaced as "Hide items with no sources").
