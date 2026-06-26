# Mind the Gaps: roadmap

> Open work and deliberate non-goals only. For what the plugin does today, see the [README](../README.md)
> and the [report](report-guide.md) / [configuration](configuration.md) guides; for why things are the way
> they are, the [ADRs](adr/). This file is the forward-looking list, not a changelog.

## Deliberate non-goals (not built, on purpose)

| Capability | Why not |
|---|---|
| `IGapSource` as a core SPI for third-party gap plugins | Deferred by design (ADR-0002); every source ships in this plugin. |
| Fuzzy "treat an owned-but-mistagged item as owned" matching | Would mask bad/missing metadata that should be corrected. The Diagnose action surfaces the mistag instead (see backlog). |
| Per-user display gate for minted virtual items | Not possible from a plugin; minted items show for everyone. Needs upstream B. |
| Greyed "Missing" badge on minted items | Needs upstream A merged. |
| Symmetric **book series** as Set completion | OpenLibrary works carry no series and the Jellyfin Book entity has no series field, so there is no reliable series membership to diff. |
| MusicVideos domain | Enum-only; no source. |

## Upstream asks (A, B, C)

Three independent upstream changes would let the experience go fully native. The plugin works without any
of them. Drafts in [docs/upstream/](upstream/).

- **A - relax the "Missing" indicator (jellyfin-web).** Let virtual items render the greyed "Missing"
  treatment beyond episodes. Filed as **[jellyfin-web #8049](https://github.com/jellyfin/jellyfin-web/pull/8049)**;
  once merged, the virtual placeholders the plugin mints get the native greyed badge.
- **B - mint and reconcile virtual items for any type (server),** ideally behind a host
  `IVirtualItemManager` with a `DisplayMissingMovies` gate. The home for a per-user display gate. Not filed
  yet; proposal in [docs/upstream/discussion-mint-virtual-items.md](upstream/discussion-mint-virtual-items.md).
- **C - expose the shared TMDB client and key via the published NuGet (server),** so a plugin reuses the
  host's cache and key instead of carrying its own. Plumbing cleanup; not filed yet. Proposal in
  [docs/upstream/discussion-tmdb-nuget-surface.md](upstream/discussion-tmdb-nuget-surface.md).

## Priorities (suggested, not committed)

- **Upstream ask A** ([jellyfin-web #8049](https://github.com/jellyfin/jellyfin-web/pull/8049)): merged, it
  gives the virtual placeholders the plugin mints across every domain their native greyed "Missing" badge.
- **Batch send to an arr/Seerr stack.** A multi-select selection (or the whole todo list) handed to
  Radarr/Sonarr/Jellyseerr in one pass. Preferred over bulk minting in the near term: minting is held as long
  as possible in the hope upstream B makes it native, so acquisition handoff is the better place to spend
  effort now.

## Backlog

### Correctness and known limitations

- **Collection completion flags owned-but-mistagged movies as missing (deliberately a real gap).**
  `CollectionGapSource` is keyed by provider id, so a movie in the owned BoxSet whose library item has no (or
  a mismatched) TMDB id is reported missing ("Jack Reacher: Never Go Back"). Not "fixed" by fuzzy
  title-and-year matching, which would mask the metadata that should be corrected; the resolution is to
  surface it via Diagnose so the user fixes the id and rescans.

### Diagnose

- **"Fix the id" action.** Diagnose is advisory; a popup button that writes the correct provider id to the
  owned item and queues a refresh would close the loop. Mutates library metadata, so it needs a confirm.

### Sources and curated sets

- **Chip pickers for the list sources.** All three list sources are reachable from settings as raw text
  fields: `CuratedTmdbListIds` accepts a pasted `themoviedb.org/list/{id}` URL or a bare id (`TmdbListInput`),
  and `CuratedTraktListIds` accepts a list's numeric id or its slug. The open polish is a chip picker each: a
  paste-and-confirm chip over the existing `tmdblist` `CuratedResolve` branch (TMDB has no list-search API),
  and for Trakt a `SearchListsAsync` over its list search (`/lists/popular`, `/lists/trending`, list search)
  would make a live type-ahead.
- **A chip picker for the curated-book source.** The curated-book source (`BooksSubjectGapSource`, completing
  an OpenLibrary subject via the `ScanCuratedBooks` toggle and the `CuratedOpenLibrarySubjects` raw field) is
  reachable from settings; the open polish is a type-ahead chip (a `CuratedSearch`/`CuratedResolve` branch
  plus a `setupChips` instance) over an OpenLibrary subject search.

### Acquisition handoff

- **Send a selection (or the whole todo list) to an arr/Seerr stack in one pass.** The per-row Send hands one
  gap to Radarr, Sonarr, or Jellyseerr/Overseerr, and the todo list collects what you want; the open piece is
  a batch path that sends a multi-select selection, or the whole todo list, at once, reporting per-item
  success the way the multi-select mint does.

### Minting

- **One-click bulk mint across a pattern or domain.** Held as long as possible: minting is experimental and
  upstream B may make it native, so this waits on that. When it is built, the config must let the user choose
  which kinds to bulk-mint (Movies, Seasons and Episodes, Albums, Books) rather than minting a whole domain
  blindly. Minting is per-row and multi-select today, and the per-kind container strategy (a BoxSet for
  collections, the owning artist for albums, the catch-all collection otherwise) already lives in
  `MintGapAsync` / `ResolveContainerAsync`. A "mint every gap of a pattern or domain" lifts that into a small
  one-shot pass over the report.
- **Background scheduled minting.** A scheduled task that keeps a chosen set of patterns/domains
  materialized (mint new, reconcile owned) on the scan cadence, reusing `MintRunner` and the container
  strategy. Open: the selection UI and guardrails against flooding a library.
- **Move resolutions onto the minted item (once everything is minted).** Per-gap resolutions ("not really
  missing", with a note) live in `resolutions.json` keyed by gap id (ADR-0008). Once every gap is
  materialized as a virtual item, a resolution could instead ride on that item as a provider id / tag, so the
  host prunes it automatically when the item is removed and it travels with the item, rather than the plugin
  maintaining a separate keyed file that can drift from the report. Gated on minting everything (a resolution
  needs an item to hang on); until then the JSON store stands.

### Native page integration

- **CreatorWorks on the native person page.** A minted virtual item with the person attached already appears
  on that person's page and survives scans with no server change (verified against 10.11). The one-off
  filmography Mint button is the live probe. A distinct "Gaps" shelf would need jellyfin-web work; the
  plugin-only version leans on the existing person-items query. Dependencies: upstream A for the greyed badge,
  and there is still no per-user display gate.
- **In-page polish via the frontend-customization ecosystem (optional, post-1.0).** The **File
  Transformation** plugin rewrites `index.html` in memory (reflection-based soft dependency: find the
  `.FileTransformation` assembly, call `PluginInterface.RegisterTransformation`) to inject one
  `<script>`/`<link>` that loads our bundled asset. Use it for: greying minted items via CSS keyed off the
  `MintedMarker`; a library "..." context-menu "Gaps" entry that jumps to the report scoped to that library;
  and, highest fragility, native-looking "Gaps" shelves on the person and studio pages. Caveat: jellyfin-web
  exposes no stable public JS API beyond `ApiClient` and `Dashboard` (which our pages already use), so
  injected shelves DOM-scrape and break across web releases; budget an upkeep pass per release. Sequence by
  fragility, cheapest/most-durable first.

### Scale and architecture

- **Batch the bulk minter's collection saves.** `ICollectionManager.AddToCollectionAsync` saves once per
  call, so the minter does one DB save per missing movie; collect a BoxSet's movies and call `CreateItems`
  plus a single `AddToCollectionAsync` per BoxSet.
- **Shard the persisted report by domain.** The transfer half is done (the API serves per pattern plus a
  lightweight summary). The open half is storage: `gaps.json` is one file rewritten on every scan and
  availability checkpoint, and toward the 50k accumulation cap it reaches multiple MB. Split by `MediaDomain`
  (not by source, which would break the three series sources sharing episode ids). Touches `GapStore`
  (multi-file atomic writes), `GapEngine` (per-domain de-dup and carry-forward), and `AvailabilityRunner`.
- **Virtualize the dashboard render.** Full filmography coverage of a large library can reach tens of
  thousands of gaps; the flat render may eventually want windowing. Pairs with the report sharding.
- **Finer dashboard JS split.** The dashboard is authored as `Web/mindthegaps.{html,css,js}` concatenated at
  build time. Optionally split `mindthegaps.js` into concern-grouped sections (filters/state, tree render,
  row actions, availability, views/export) and concatenate them too; the shared IIFE scope makes that a
  careful, browser-tested change, and the build pipeline already absorbs extra inputs.
