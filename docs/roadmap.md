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
| Greyed "Missing" badge on minted movies | Needs upstream A merged. |
| Symmetric **book series** as Set completion | OpenLibrary works carry no series and the Jellyfin Book entity has no series field, so there is no reliable series membership to diff. |
| MusicVideos domain | Enum-only; no source. |
| Minting non-movie kinds today | Deferred to post-1.0 on top of the bulk-mint container refactor, where the minter can be made kind-aware and validated against a running server rather than written speculatively (see backlog). |

## Upstream asks (A, B, C)

Three independent upstream changes would let the experience go fully native. The plugin works without any
of them. Drafts in [docs/upstream/](upstream/).

- **A - relax the "Missing" indicator (jellyfin-web).** Let virtual items render the greyed "Missing"
  treatment beyond episodes. Filed as **[jellyfin-web #8049](https://github.com/jellyfin/jellyfin-web/pull/8049)**;
  once merged, the collection movies already mintable get the native greyed badge.
- **B - mint and reconcile virtual items for any type (server),** ideally behind a host
  `IVirtualItemManager` with a `DisplayMissingMovies` gate. The substantive feature, and the home for a
  per-user display gate. Not filed yet; proposal in
  [docs/upstream/discussion-mint-virtual-items.md](upstream/discussion-mint-virtual-items.md).
- **C - expose the shared TMDB client and key via the published NuGet (server),** so a plugin reuses the
  host's cache and key instead of carrying its own. Plumbing cleanup; not filed yet. Proposal in
  [docs/upstream/discussion-tmdb-nuget-surface.md](upstream/discussion-tmdb-nuget-surface.md).

## Priorities (suggested, not committed)

- **Acquisition handoff (Radarr/Sonarr, Jellyseerr/Overseerr).** The report dead-ends at a link; the
  highest-value next feature is a per-row "Send" that hands a gap to an arr or request stack. Two spike
  branches already prototype it and need rebasing onto current main (see backlog).
- **Enable the music and book sources by default.** They ship off by default until validated against real
  data. Books is hardened and its OpenLibrary fixtures are captured; Discogs (labels and artist discography)
  is new; both want real-world validation across a varied library before defaulting on.
- **Upstream ask A** ([jellyfin-web #8049](https://github.com/jellyfin/jellyfin-web/pull/8049)): merged, it
  gives mintable collection movies their native greyed "Missing" badge.
- **Shareable-link size** is a latent bug (links can exceed URL limits); cheap to de-risk.
- If in-place CreatorWorks rendering is wanted, prototype the person-page materialization below.

## Backlog

### Correctness and known limitations

- **Shareable links can exceed URL limits.** `shareUrl` (Copy-link and the Markdown export's summary link)
  encodes the whole `captureView` as URI-encoded JSON in a `cgview` hash param. The bulk is
  `disabledProviders` (100+ names), so a link can run to thousands of characters. Options: store *enabled*
  providers as a short diff (or omit provider state, server-specific anyway), drop default-valued fields,
  compress (deflate plus URL-safe base64), or persist the view server-side under a short token. Likely
  combine the first two, then compress if still large.
- **Collection completion flags owned-but-mistagged movies as missing (deliberately a real gap).**
  `CollectionGapSource` is keyed by provider id, so a movie in the owned BoxSet whose library item has no (or
  a mismatched) TMDB id is reported missing ("Jack Reacher: Never Go Back"). Not "fixed" by fuzzy
  title-and-year matching, which would mask the metadata that should be corrected; the resolution is to
  surface it via Diagnose so the user fixes the id and rescans.
- **Library-source reboot guard.** The TVmaze/TheTVDB cross-checks skip a year-mismatched resolved show
  (`SeriesContentGapSourceBase.LooksLikeDifferentSeries`), but the always-on library source surfaces
  whatever core minted, so a same-named reboot mistag (V 1984 tagged as V 2009) can still appear. A guard
  there comparing a missing episode's year to the owned episodes' year *range* (not the series start year,
  to avoid hiding a legitimate late season) would catch it without the masking risk.

### Diagnose

- **"Fix the id" action.** Diagnose is advisory; a popup button that writes the correct provider id to the
  owned item and queues a refresh would close the loop. Mutates library metadata, so it needs a confirm.
- **Extend the audit to Music and Books.** The per-gap Diagnose covers albums and books; the library-wide
  identification audit stays movies and shows (its duplicate-id section is TheMovieDb-specific). Extending it
  would generalize the duplicate detection per primary provider.

### Sources and curated sets

- **Curated-book gap source, then Books chips.** The chip picker is kind-agnostic and covers studios,
  keywords, and Discogs labels; Books needs a curated-book **gap source** first (none today, only the author
  bibliography from owned books), for example an OpenLibrary subject or author set. Once that source and a
  config field exist, the chip kind is a `CuratedSearch`/`CuratedResolve` branch plus a `setupChips` instance.
- **Discogs: complete an artist's discography across both providers.** `DiscogsArtistGapSource` already
  covers the artists the MusicBrainz sources cannot (no MusicBrainz id), so the two span disjoint artists with
  no duplication. The open piece is *completeness* widening: for an artist MusicBrainz *does* cover, also
  consult Discogs and merge the two release lists (de-duplicated by normalized title) so an album one provider
  lists and the other misses still surfaces. Deferred because the cross-provider, name-keyed release de-dup is
  fuzzy and would change the MusicBrainz gap-id scheme (a persisted-id contract, ADR-0008).
- **Default the Books source on.** The two OpenLibrary endpoints the hardening added (`/works/{key}.json` and
  `/search.json?author_key=`) now have captured fixtures and deserialization tests. What remains before it is
  on by default: real-world validation across a varied library, and optionally a config-time author-to-key
  override for the cases the matcher still gets wrong.
- **People drive Shows, not just movies.** The TMDB people source surfaces a person's missing *films* only:
  `PeopleGapSource` fetches credits with `PersonMethods.MovieCredits`, `FilmographyGapMapper` hardcodes
  `domain: Movies, targetKind: Movie`, and the source's `OwnedKinds` is `{ Movie }`. The input for Shows is
  already there (people are harvested from owned series casts too, and owned movie-plus-series credits rank
  which people to scan), so the gap is the *output*: add `PersonMethods.TvCredits` to the fetch and a
  Shows/Series mapping path (CreatorWorks, `domain: Shows`, `targetKind: Series`) alongside the movie one,
  widening `OwnedKinds` to include `Series`. Net-new Creator works coverage: an owned actor or director's
  unowned series become gaps. Trakt's filmography cross-check could grow the same way.

### Acquisition handoff

- **Send a gap to Jellyseerr/Overseerr.** The lighter first target: keyed purely on TMDB ids, which every
  gap has. One client (`Services/Seerr/SeerrClient`), one endpoint (`POST /api/v1/request` with
  `{mediaType, mediaId}`), one config pair (URL plus API key), covering movies and series with no
  profile/root-folder plumbing.
- **Send a gap to Radarr/Sonarr.** Radarr `POST /api/v3/movie` takes a tmdbId; Sonarr is keyed on the
  series' tvdbId (which a missing-episode gap carries). Needs `Services/Arr/{RadarrClient,SonarrClient}`,
  config for base URL, API key, default quality profile and root folder per service, and movie-vs-series
  routing by `TargetKind`. Both handoffs are opt-in and config-gated (the button appears only when
  configured). Two spike branches already prototype both (`Services/Acquisition|Arr|Seerr`, settings UI, Send
  buttons) and need rebasing onto current main.

### Minting

- **Bulk mint across all enabled patterns.** Minting is per-row and multi-select today. A one-click "mint
  every gap of a pattern/domain" needs the per-cell container strategy promoted out of `MintGapAsync` into a
  small "is this materializable yet, and what is its container" lookup (BoxSet for collections, native for
  episodes, catch-all collection for CreatorWorks/Recommendation).
- **Mint virtual placeholders for non-movie kinds.** Albums (a pathless virtual `MusicAlbum` tagged with the
  `MintedMarker`, in its artist or a catch-all) and books (virtual `Book`), built on the bulk-mint container
  refactor and validated against a running server. Tracks/songs are low value (the album is the unit).
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

- **CreatorWorks on the native person page.** A minted virtual `Movie` with the person attached already
  appears on that person's page and survives scans with no server change (verified against 10.11). The
  one-off filmography Mint button is the live probe. A distinct "Gaps" shelf would need jellyfin-web work; the
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
- **Coverage badges on cross-check-only series.** Coverage shows on BoxSet/collection and library-known
  series groups; cross-check-only series (not in core's missing list) carry none. Counting their owned
  episodes the way the library source does would give them a badge.
