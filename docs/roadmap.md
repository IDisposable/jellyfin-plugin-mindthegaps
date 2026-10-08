# Mind the Gaps: roadmap

> Open work and deliberate non-goals only. For what the plugin does today, see the [README](../README.md)
> and the [report](report-guide.md) / [configuration](configuration.md) guides; for why things are the way
> they are, the [ADRs](adr/). This file is the forward-looking list, not a changelog.

## Deliberate non-goals (not built, on purpose)

| Capability                                                                   | Why not                                                                                                                                                                                                                                                                                                                                                                                                          |
| ---------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `IGapSource` as a core SPI for third-party gap plugins                       | Deferred by design (ADR-0002); every source ships in this plugin.                                                                                                                                                                                                                                                                                                                                                |
| Fuzzy "treat an owned-but-mistagged item as owned" matching                  | Would mask bad/missing metadata that should be corrected. The Diagnose action surfaces the mistag instead so it can be fixed at the source.                                                                                                                                                                                                                                                                      |
| Per-user display gate for minted virtual items                               | Not possible from a plugin; minted items show for everyone. Needs upstream B.                                                                                                                                                                                                                                                                                                                                    |
| Greyed "Missing" badge on minted items                                       | Needs upstream A merged.                                                                                                                                                                                                                                                                                                                                                                                         |
| Symmetric **book series** as Set completion                                  | OpenLibrary works carry no series and the Jellyfin Book entity has no series field, so there is no reliable series membership to complete.                                                                                                                                                                                                                                                                       |
| A request-and-approval gate before a title is acquired                       | Might be a separate plugin. The report's Fulfillment queue folds every user's TODO list into one row per title with a Mark fetched action, but nothing approves or denies a request before it lands there; adding a title is still unmediated.                                                                                                                                                                   |
| A "Fix the id" action in Diagnose                                            | Diagnose stays advisory. Opening the item's own page and using Identify fixes the id and refreshes its images, so a plugin button would only duplicate it.                                                                                                                                                                                                                                                       |
| Album tracklists or missing tracks on the Web UI                             | An album's dialog fetches nothing: MusicBrainz carries no description for a release-group, a tracklist would need a new call chain (release-group to a release to its recordings), and no track-completeness source exists.                                                                                                                                                                                      |
| MusicVideos domain                                                           | Enum-only; no source.                                                                                                                                                                                                                                                                                                                                                                                            |
| An acquisition handoff (Radarr/Sonarr/Lidarr/Readarr) on the injected Web UI | Deliberately removed. Every want is already visible to an administrator through the report's own Fulfillment queue (backed by everyone's TODO list); routing acquisition through the pages injected into jellyfin-web's own UI would put arr credentials and calls behind a surface that is not the report, which is the one boundary this plugin keeps deliberately narrow. Send stays on the report page only. |
| Acquisition presence badges/caching on a Web UI card (already requested?)    | Not asked for; revisit only if it comes up. Would need its own cache layer (an arr call per card is too expensive) with no home yet.                                                                                                                                                                                                                                                                             |

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

- **Upstream ask A** ([jellyfin-web #8094](https://github.com/jellyfin/jellyfin-web/pull/8094)): merged, it
  gives the virtual placeholders the plugin mints across every domain their native greyed "Missing" badge.

## Backlog

### Sources and curated sets

- **Chips should record whether a list is public or private.** MDBList and IMDb lists cannot be told apart
  today, so their titles stay off the home Discover row until they can.

### Acquisition handoff

- **A "Send all" on the Fulfillment queue.** The report's own multi-select bar now calls `SendToArrBulk`/
  `SendToSeerrBulk` (Acquire/Request) alongside Mint and Add to TODO. The Fulfillment queue itself still has
  no bulk send of its own, only Mark fetched and Verify all, which is deliberate for now (an administrator
  using it is explicitly the one fetching things by hand instead of through an arr); revisit only if that
  changes.

### Web UI

- **A per-user dismissal.** Only an administrator can dismiss a gap, by resolving it on the report, and that
  hides it for everyone (`ResolutionStore` is server-wide). A signed-in user on the Web UI surfaces has no
  way to say "not interested" to a card, so a title they will never want keeps coming back on every person,
  item, studio and home row. Needs a per-user store beside the want-to-watch list (per user, like `TodoStore`),
  a control on the card or in its dialog, and each surface leaving out what the caller dismissed as well as
  what the report resolved.
- **A studio/network gaps shelf, without a dedicated studio/network page.** Jellyfin core has no studio or
  TV-network page to inject a section into (the earlier blocker on this), but it does route two existing
  pages by the same ids a shelf would need: the generic list page takes a `studioId` query param
  (`#/list?studioId=<id>&serverId=<id>`), and the TV collection page takes a `topParentId`/`collectionType`
  pair (`#/tv?topParentId=<id>&collectionType=tvshows&tab=<n>`). `mindthegaps.webui.js` already parses the
  hash router for the item page's own `#/details?id=` the same way, so reading `studioId` off `#/list` and
  `topParentId` off `#/tv` (scoped to `collectionType=tvshows`) is the same technique aimed at a different
  route, not a new one. Needs: resolving a library studio to a TMDB company id (no library studio carries
  one; auto-seed's own by-name resolution is the precedent) for the movie-studio case, and figuring out
  what a TV network's equivalent id/source even is before building the network half. Sequenced after the
  chip pickers.

### Settings page

- **A scroll-to-top button, matching the report's.** The settings form has grown long enough that the
  report's own back-to-top affordance would help here too.
- **Collapsible sections.** Each settings section (per source, per feature) could collapse like the
  report's groups, so a page of mostly-unused toggles is not one long scroll to reach the one you want.

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

- **A menu entry that opens the report scoped to a library or a person.** A library "..." context-menu "Gaps"
  entry that jumps to the report. Rides on the same `index.html` injection the Web UI uses, and shares its
  fragility: jellyfin-web exposes no stable public JS API beyond `ApiClient` and `Dashboard`, so anything that
  finds its place by DOM shape needs an upkeep pass per web release.

### Scale and architecture

- **Virtualize the dashboard render.** A group's rows are built only when it is opened and the list itself
  loads as slim rows, but a very large flat tab still renders every row it shows. Windowing would help once a
  library reaches tens of thousands of gaps in one group.
