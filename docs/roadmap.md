# Mind the Gaps: roadmap

> Open work and deliberate non-goals only. For what the plugin does today, see the [README](../README.md)
> and the [report](report-guide.md) / [configuration](configuration.md) guides; for why things are the way
> they are, the [ADRs](adr/). This file is the forward-looking list, not a changelog.

## Deliberate non-goals (not built, on purpose)

| Capability                                                    | Why not                                                                                                                                                    |
| ------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `IGapSource` as a core SPI for third-party gap plugins        | Deferred by design (ADR-0002); every source ships in this plugin.                                                                                          |
| Fuzzy "treat an owned-but-mistagged item as owned" matching   | Would mask bad/missing metadata that should be corrected. The Diagnose action surfaces the mistag instead so it can be fixed at the source.                |
| Per-user display gate for minted virtual items                | Not possible from a plugin; minted items show for everyone. Needs upstream B.                                                                              |
| Greyed "Missing" badge on minted items                        | Needs upstream A merged.                                                                                                                                   |
| Symmetric **book series** as Set completion                   | OpenLibrary works carry no series and the Jellyfin Book entity has no series field, so there is no reliable series membership to complete.                 |
| Removing a title from a want-to-watch list when it is watched | Kept manual on purpose; the want-to-watch work below does not do it.                                                                                       |
| A request and approval queue for acquisition                  | Might be a separate plugin. Here every administrator sees and manages every user's todo list, and that is the whole of it.                                 |
| A "Fix the id" action in Diagnose                             | Diagnose stays advisory. Opening the item's own page and using Identify fixes the id and refreshes its images, so a plugin button would only duplicate it. |
| MusicVideos domain                                            | Enum-only; no source.                                                                                                                                      |

## Upstream asks

Independent upstream changes that would let the experience go fully native. The plugin works without any of
them. Drafts in [docs/upstream/](upstream/).

- **A - relax the "Missing" indicator (jellyfin-web).** Let virtual items render the greyed "Missing"
  treatment beyond episodes. The first PR (#8049) was closed and replaced by
  **[jellyfin-web #8094](https://github.com/jellyfin/jellyfin-web/pull/8094)**, which is open; once merged,
  the virtual placeholders the plugin mints get the native greyed badge.
- **B - mint and reconcile virtual items for any type (server),** ideally behind a host
  `IVirtualItemManager` with a `DisplayMissingMovies` gate. The home for a per-user display gate. Not filed
  yet; proposal in [docs/upstream/discussion-mint-virtual-items.md](upstream/discussion-mint-virtual-items.md).
- **C - expose the shared TMDB client and key via the published NuGet (server),** so a plugin reuses the
  host's cache and key instead of carrying its own. Plumbing cleanup; not filed yet. Proposal in
  [docs/upstream/discussion-tmdb-nuget-surface.md](upstream/discussion-tmdb-nuget-surface.md).
- **D - a database column for `Episode.IndexNumberEnd` (server),** so it survives `SkipDeserialization` and
  can be queried. Stands alone; see [docs/upstream/pr-indexnumberend-column.md](upstream/pr-indexnumberend-column.md).
- **A plugin hook for the web client (jellyfin-web).** There is none, so the Web UI adds its script by
  rewriting `index.html` at request time. A supported hook would replace that. Not drafted.

## Priorities (suggested, not committed)

- **Want to watch: the title search.** The bookmark on every card and the home row are built, each user's own
  list. What is left is a search dialog over TMDB, and the optional playlist for titles the library holds. Plan
  under [Web UI](#web-ui-experimental) below.
- **Per-title certification filtering for restricted users,** only if someone asks; see below.
- **Actions on a selection in the report.** The multi-select bar takes Mint, Send to (Radarr, Sonarr,
  Jellyseerr/Overseerr, or the watch list) and a bulk Resolve that asks for the reason once. The server side of
  send and resolve exists (`SendToArrBulk`, `SendToSeerrBulk`, `ResolveBatch`); the report calls none of them.
  Acquisition handoff is preferred over bulk minting in the near term: minting is held as long as possible in
  the hope upstream B makes it native.
- **Upstream ask A** ([jellyfin-web #8094](https://github.com/jellyfin/jellyfin-web/pull/8094)): merged, it
  gives the virtual placeholders the plugin mints across every domain their native greyed "Missing" badge.

## Backlog

### Correctness and known limitations

- **Collection completion flags owned-but-mistagged movies as missing (deliberately a real gap).**
  `CollectionGapSource` is keyed by provider id, so a movie in the owned BoxSet whose library item has no (or
  a mismatched) TMDB id is reported missing ("Jack Reacher: Never Go Back"). Not "fixed" by fuzzy
  title-and-year matching, which would mask the metadata that should be corrected; the resolution is to
  surface it via Diagnose so the user fixes the id and rescans.
- **A Trakt list entry that is not a real list fails quietly.** A slug such as `popular` names a Trakt
  category, not a list; the items request comes back 400, the list request fails to parse as a list, and the
  source logs a JSON error and reports a list with no items. Validating the entry when it is saved would say
  so instead.

### Sources and curated sets

- **Chip pickers for the remaining list sources.** Studios, keywords, MDBList lists, and Discogs labels have a
  type-ahead chip picker. Three sources are still raw text fields: `CuratedTmdbListIds` (a pasted
  `themoviedb.org/list/{id}` URL or a bare id, `TmdbListInput`; TMDB has no list-search API, so a
  paste-and-confirm chip over the existing `tmdblist` `CuratedResolve` branch), `CuratedTraktListIds` (a
  numeric id or a slug; a `SearchListsAsync` over Trakt's list search would make a live type-ahead), and
  `CuratedOpenLibrarySubjects` (the curated-book source; a `CuratedSearch`/`CuratedResolve` branch plus a
  `setupChips` instance over an OpenLibrary subject search). The chips should also record whether a list is public or private: MDBList and IMDb lists cannot be told apart
  today, so their titles stay off the home Discover row until they can.

### Acquisition handoff

- **The dashboard half of batch send.** See Priorities: the endpoints are built, the report and todo list
  need the multi-select "Send" and a "Send all" on the todo list.

### Web UI (experimental)

- **Want to watch, without auto-removal: what is left.** Each user's own list, the bookmark on every card and
  the home row are built. Still open: (2) a title search dialog over TMDB, rehydrating a chosen card from its
  TMDB id, so a title no page lists can be added; (3) optionally, a per-user Jellyfin playlist for titles the
  library already holds, which shows in every client, where a title that arrives in the library moves to that
  user's playlist only. The home row hides a title once the library holds it; the playlist is where it would
  show up instead.
- **Certification filtering by the caller.** A user with a parental rating limit is shown no surface, and a
  page is shown only to a user who can see its item. A finer filter would check each listed title's
  certification against the limit, which costs a TMDB request per title, so it waits until someone asks. See
  ADR-0019.
- **More of the works surfaces.** Artist, book and author pages list what the owner's sources find, with links
  and a want-to-watch bookmark. Still open: an album page (the artist's other albums, or missing tracks, for
  which there is no track-completeness source yet); a richer album or book dialog (a tracklist, a
  description) fetched from MusicBrainz or OpenLibrary; a Lidarr or Readarr handoff to give these cards a
  Send; and a studio page, which needs a TMDB company id (a library studio has none, so it would resolve by
  name, as auto-seed does) and a cap on a large catalogue, and depends on jellyfin-web having a page to add
  it to.

### Scan performance

- **Weight scan progress by source cost.** Progress is the unweighted average of the concurrent sources, so
  once the fast ones finish the bar tracks only the slowest. On a library of about 4,000 items the sources
  other than filmography and series content were done in under 30 seconds, filmography took 110 seconds and
  series content 222, so most of the bar's time is spent in its last stretch. Weighting by expected cost, or
  reporting per-source progress, would make it honest.

### Minting

- **One-click bulk mint across a pattern or domain, and scheduled minting.** Built on the
  `feature/mint-bulk-and-auto` branch (a shared materialization classifier, a bulk mint, and a scheduled
  task) but not merged to `main`. Held as long as possible: minting is experimental and upstream B may make it
  native. When it lands, the config must let the user choose which kinds to bulk-mint (Movies, Seasons and
  Episodes, Albums, Books) rather than minting a whole domain blindly, with guardrails against flooding a
  library.
- **Batch the bulk minter's collection saves.** `ICollectionManager.AddToCollectionAsync` saves once per
  call, so the minter does one DB save per missing movie; collect a BoxSet's movies and call `CreateItems`
  plus a single `AddToCollectionAsync` per BoxSet.
- **Move resolutions onto the minted item (once everything is minted).** Per-gap resolutions ("not really
  missing", with a note) live in `resolutions.json` keyed by gap id (ADR-0008). Once every gap is
  materialized as a virtual item, a resolution could instead ride on that item as a provider id / tag, so the
  host prunes it automatically when the item is removed and it travels with the item, rather than the plugin
  maintaining a separate keyed file that can drift from the report. Gated on minting everything (a resolution
  needs an item to hang on); until then the JSON store stands.

### Native page integration

- **CreatorWorks on the native person page.** A minted virtual item with the person attached already appears
  on that person's page and survives scans with no server change (verified against 10.11). The Web UI's
  "Missing from your library" section covers the same ground without minting anything; a distinct native
  "Gaps" shelf would still need jellyfin-web work. Dependencies: upstream A for the greyed badge, and there is
  still no per-user display gate.
- **A menu entry that opens the report scoped to a library or a person.** A library "..." context-menu "Gaps"
  entry that jumps to the report. Rides on the same `index.html` injection the Web UI uses, and shares its
  fragility: jellyfin-web exposes no stable public JS API beyond `ApiClient` and `Dashboard`, so anything that
  finds its place by DOM shape needs an upkeep pass per web release.

### Scale and architecture

- **Extract the persistence and memoization helpers from `GapStore`.** It now holds the per-domain file I/O,
  the availability and additive merges, the generation counter and validator, and the domain and summary
  indexes. The comments are thorough, but the class is large; the domain-file I/O and the memoization are the
  natural seams.
- **Virtualize the dashboard render.** A group's rows are built only when it is opened and the list itself
  loads as slim rows, but a very large flat tab still renders every row it shows. Windowing would help once a
  library reaches tens of thousands of gaps in one group.
- **Finer dashboard JS split.** The report script is about 4,100 lines, against about 470 for settings and
  about 50 shared. Optionally split the report script into concern-grouped sections (filters/state, tree
  render, row actions, availability, views/export) and concatenate those too; the build wraps the shared kit
  and the page script in one scope, so extra parts share it for free and the build already absorbs extra
  inputs. What makes it a careful, browser-tested change is that nothing but the browser proves the parts
  still see each other.
