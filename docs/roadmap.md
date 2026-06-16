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
| AvailabilityService (TMDB) | n/a | "where to watch" per item, fetched lazily | Done |

Supporting pieces that are done: the dashboard todo list (filter, search, external links per item),
the scheduled scan, the dedupe engine, and the per-item availability lookup. CreatorWorks and
Recommendation gaps are detected and shown today, for movies and series. They render as a list with
links, not as in-library placeholders.

## Materialization: built and working

| Target | Pattern | Status |
|---|---|---|
| missing collection movies, minted into a BoxSet | SetCompletion | Done (experimental, opt-in; dry-run and metrics supported) |
| missing series episodes | SetCompletion | Not needed here: the server already synthesizes virtual missing episodes |
| CreatorWorks gaps | CreatorWorks | Not built: no container to render into (see below) |
| Recommendation gaps | Recommendation | Not built: no container to render into (see below) |

The minter is idempotent, tags everything with the `MindTheGapsMinted` provider id, never deletes
files, and is fully reversible. Its config checkboxes for CreatorWorks and Recommendation exist but
are reserved; the mint path only acts on SetCompletion.

## Promised but not built

| Promise (README / CLAUDE / ADRs) | Status |
|---|---|
| Music, Books, MusicVideos domains (`MediaDomain`; discography, book series, bibliography) | Enum values exist, zero sources. Movies and Shows are implemented. |
| Materialize CreatorWorks or Recommendation gaps | Reserved checkboxes only |
| Native virtual items in core (the three upstream asks) | PR A filed as jellyfin-web 8049; Discussions B and C are drafts |
| Per-user display gate for minted items | Not possible from a plugin; minted items show for everyone |
| `IGapSource` as a core SPI so third-party gap plugins can exist | Deferred on purpose (ADR-0002); every source ships in this plugin |
| Greyed "Missing" badge on minted movies | Needs jellyfin-web PR A merged |

## Filling CreatorWorks and Recommendation gaps

Detecting these is done. Materializing them as virtual placeholders is blocked on one thing: a
container to render into. SetCompletion works because a BoxSet is a real owned container with
LinkedChild membership, so a virtual movie has a home that already renders. CreatorWorks (an owned
person's unowned films) and Recommendation (similar titles) have no equivalent owned container.

This is a design decision, not a missing API. `ILibraryManager.CreateItem` works for any `Movie` and
`ICollectionManager` can create BoxSets, so the plugin can manufacture a container. Two candidate
homes for CreatorWorks:

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
the person) is the live probe for this. Remaining dependencies, unchanged: the greyed "Missing" badge
needs jellyfin-web PR A (8049); there is still no per-user display gate (minted items show for
everyone); and the plugin must reconcile a minted film once the real file is owned.

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

## Priorities (suggested, not committed)

- Get jellyfin-web PR A (8049) merged so the collection movies already mintable today get their badge.
- Decide whether Music and Book are real roadmap or should be trimmed from the docs to stop
  over-promising.
- If in-place CreatorWorks rendering is wanted, prototype the person-page materialization above; it is
  a better container story than synthetic collections.
