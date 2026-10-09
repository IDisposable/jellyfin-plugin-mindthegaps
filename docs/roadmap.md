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
- **Links on the want-list group headers.** The Discogs wantlist and OpenLibrary want-to-read groups have
  no link to the list itself, since their mappers pass no source ids. Both pages have a fixed address built
  from the configured user name (the Discogs wantlist, the OpenLibrary want-to-read shelf), so this needs no
  new data: a `CreatorLinks` case per source type, or the mappers passing the link themselves.
- **A "because you own X" source for music.** The music counterpart of the TMDB recommendations: seeded by
  owned artists, with gap ids shared across seeds the way `recommendation:movie:` ids are, so several owned
  artists fold onto one gap through `OtherSources` and the deleted-item prune can promote between them.
  MusicBrainz, Discogs and OpenLibrary have no "similar" endpoint. On the 12.0 ABI, core has a similar-items
  API (`ISimilarItemsManager`, `IRemoteSimilarItemsProvider<T>`) and ships a ListenBrainz similar-artists
  provider for it, so the source can ask core rather than carry a client; 10.11 has neither, so there it needs
  its own client (ListenBrainz's similar artists, keyless, or Last.fm's `artist.getSimilar`, which needs a
  key) or goes without. Capture fixtures and confirm the shape and rate limits before building on either.
  Books have no such data source, so there is no book counterpart.

### Ids and links

Links are built from ids and never stored, and the plugin registers OpenLibrary and Discogs providers that
step aside for a dedicated plugin ([ADR-0021](adr/0021-links-are-built-from-ids.md)). What is left:

- **Move the OpenLibrary and Discogs providers to their own plugins.** `ProviderPrecedence` already hands over
  to an installed plugin that provides the same key, so this plugin's providers can stay until those plugins
  ship and then be deleted. Give their id and url provider classes parameterless constructors, which makes the
  takeover exact rather than matched by the plugin's name.
- **Let an author's OpenLibrary id steer the bibliography.** The url provider already links a person's
  OpenLibrary author key, but no external id is registered for a person and `BooksBibliographyGapSource`
  resolves the author from the book's work id, then by name. Registering the field for a person and preferring
  that key would let a user fix a wrong namesake by hand (the author disambiguation rough edge).
- **Derive the images that ids already name.** Images are stored as host tokens (`StoredUrls`), not built from
  ids. Two could be: a Cover Art Archive cover is fully named by the release-group id, and a TMDB poster by its
  path. Doing it means storing the path rather than the address and building the address on read, as links are.

### Acquisition handoff

- **A "Send all" on the Fulfillment queue.** The report's own multi-select bar now calls `SendToArrBulk`/
  `SendToSeerrBulk` (Acquire/Request) alongside Mint and Add to TODO. The Fulfillment queue itself still has
  no bulk send of its own, only Mark fetched and Verify all, which is deliberate for now (an administrator
  using it is explicitly the one fetching things by hand instead of through an arr); revisit only if that
  changes.

### Web UI

- **A TV-network gaps shelf.** The movie-studio half shipped as the studio list page's "Missing from this
  studio" row (`StudioMissingService`, behind `StudioPagePlacement`). The network half is blocked on
  resolution, not on a page: Jellyfin keeps a series' network in the same `Studios` field a movie's studio
  uses, so there is no distinct network entity to key off, and TMDB has no search by network name (only
  `GetNetworkAsync(id)` and discover filtered by a network id already in hand), unlike the company search
  the studio row resolves by. Revisit only once there is a reliable way to turn a library studio name into a
  TMDB network id.

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

### Storage

Measured against a real install's 82,958 gaps in [storage-evaluation.md](storage-evaluation.md). The report is
read and written through streams, and its offers are stored lean (a gap's watch page once, a service's logo once in
the meta file); together those took the real report from about 1.6 GB of heap to 205 MB and its files from 196 MB
to 131 MB. In order:

- **Write the report unindented.** About a quarter smaller and a little faster. The files stay readable through
  any JSON viewer.
- **Write less often.** Scale the mid-scan checkpoint interval to how long the last write took (today a fixed five
  seconds, which on a large report is a whole-report write most of that time), and have a bulk re-check flush its
  swaps once per batch rather than rewriting a domain file per set.
- **Then decide on SQLite for the gap report alone**, if a verify or a re-check is still slow: one row per gap,
  compile-only `Microsoft.Data.Sqlite` from the host pinned per ABI, writes as row deltas, reads unchanged from
  the in-memory report, the JSON imported once. The small stores stay JSON either way.

### Scale and architecture

- **Virtualize the dashboard render.** A group's rows are built only when it is opened and the list itself
  loads as slim rows, but a very large flat tab still renders every row it shows. Windowing would help once a
  library reaches tens of thousands of gaps in one group.
