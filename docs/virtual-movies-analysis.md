# First-class "Virtual Movies": how much already exists?

> Written to gauge appetite for extending Jellyfin's existing virtual / missing item support
> (today used for missing show episodes and seasons) to Movies, so the missing entries of a
> partially-owned collection/franchise render greyed-out the same way missing episodes do inside
> a series.
>
> Server refs are against `jellyfin/jellyfin` at internal version `12.0.0`; web refs against
> `jellyfin/jellyfin-web` (current `master`). Line numbers will drift, so treat them as anchors.

## Summary

Most of the machinery is already generic on `BaseItem` and needs no change:

- storage (`IsVirtualItem` column + indexes),
- query translation (`IsVirtualItem` / `IsMissing` / `IsUnaired` filters),
- the DTO surface the client consumes (`LocationType`),
- BoxSet membership resolution (`LinkedChild` by `ItemId`).

What is shows-only, and would need new work, is narrow and well-isolated:

1. Creation of virtual items (only show seasons and episodes are ever synthesized today),
2. the display gate (`User.DisplayMissingEpisodes`, which is episode-named and episode-scoped),
3. scan reconciliation/cleanup (the virtual-to-real flip and orphan GC live in the Series metadata services),
4. the web "Missing" badge (two one-line `Type === 'Episode'` conditionals).

So the load-bearing plumbing already exists; the gap is a creation path, a display toggle,
reconciliation, and a small web tweak.

## Prior art: a working out-of-tree proof of concept

The MindTheGaps plugin already detects these gaps end-to-end (diffing a partially-owned TMDB
collection against the library) and surfaces them in a dashboard "todo list". The detection is
proven from a plugin. What a plugin can't do cleanly is the native rendering and reconciliation,
which is the small, well-isolated set of server/web changes cataloged below.

---

## What is already generic (no change required)

### Data model: `MediaBrowser.Controller/Entities/BaseItem.cs`

- `public bool IsVirtualItem { get; set; }` is on `BaseItem`, so it's inherited by `Movie` and `BoxSet`.
- `LocationType` is computed generically: any item with a null/empty `Path` (and not a Channel)
  reports `LocationType.Virtual`. A pathless `Movie` already computes `LocationType.Virtual` today,
  with nothing shows-specific about it.
- `Episode.IsMissingEpisode => LocationType == LocationType.Virtual` is just a shows-named alias for
  the generic check; no `Movie` equivalent is technically needed.

> Aside (not core to this proposal): the same `BaseItem` already exposes `IsUnaired`
> (`PremiereDate >= today`), which is how the system separates Missing (released, not owned) from
> Upcoming (announced, unreleased); they even get different web badges. It matters here only so a
> virtual Movie for an unreleased franchise entry can render as "Upcoming" rather than as an
> actionable "go download this". It's polish, and it needs no new mechanism.

### Persistence: `src/Jellyfin.Database/.../Entities/BaseItemEntity.cs` + `ModelConfiguration/BaseItemConfiguration.cs`

- `IsVirtualItem` is a plain column on the single `BaseItems` table (table-per-hierarchy).
- Multiple composite indexes already include `IsVirtualItem`, all keyed by `Type`, so they already
  serve Movie queries. No schema change or migration needed.

### Query translation: `Jellyfin.Server.Implementations/Item/BaseItemRepository.TranslateQuery.cs`

- `IsMissing` is literally an alias for `IsVirtualItem`; `IsUnaired` maps to `PremiereDate` vs now.
- Both predicates apply to all item rows, with no `Episode`/`Season` guard. Type scoping only
  happens because callers also set `IncludeItemTypes`.

### DTO / API: `MediaBrowser.Model/Dto/BaseItemDto.cs` + `Emby.Server.Implementations/Dto/DtoService.cs`

- `BaseItemDto` has no `IsVirtualItem`/`IsMissing` field; the only signal is `LocationType`, set
  unconditionally for every item. The client already keys "missing" off `LocationType === 'Virtual'`.
  No DTO change needed: a pathless Movie already serializes `LocationType: Virtual`.

### BoxSet membership: `MediaBrowser.Controller/Entities/Folder.cs` (`ResolveLinkedChildren`)

- Modern BoxSets resolve children by `LinkedChild.ItemId` via a generic, type-agnostic `GetItemList`
  query. A virtual `Movie` persisted as a real `BaseItem` row (`IsVirtualItem=true`, `Path=null`)
  and linked by `ItemId` resolves and renders like any other child.

---

## What is shows-only (the actual work)

### 1. Creation of virtual items: `MediaBrowser.Providers/TV/SeriesMetadataService.cs`, `SeasonMetadataService.cs`

The only place virtual content is minted is the Series metadata pipeline: `CreateSeasonAsync()` news up
a `Season` with no `Path` and calls `series.AddChild(...)`. The container's virtual flag is then
derived from its children in `SeasonMetadataService.SaveIsVirtualItem(...)`:

```csharp
var isVirtualItem = item.LocationType == LocationType.Virtual
    && (episodes.Count == 0 || episodes.All(i => i.LocationType == LocationType.Virtual));
```

> The in-tree TMDB/OMDb episode providers skip missing episodes (`if (info.IsMissingEpisode) return ...`)
> for scan-time reasons; virtual episodes arise from the season/episode AddChild flow, not provider
> synthesis.

Movies and BoxSets mint nothing virtual today. A Virtual-Movies feature needs an analogous creation
path, most naturally from the BoxSet/collection refresh, that mints a pathless `Movie` from the
collection's full part list and links it into the BoxSet.

### 2. Display gate: `User.DisplayMissingEpisodes`

- Defined on `User` / `UserConfiguration`, default `false`.
- Enforced in only two query spots: `Series.GetEpisodes` (episode listing) and
  `Folder.MarkPlayed` (a played-status sweep, not a display path), plus the `TvShowsController`
  episodes endpoint. The generic item/children queries do not apply it, so a BoxSet's children are
  not gated by it.
- Consequence: a virtual movie linked into a BoxSet already shows in the BoxSet detail view with no
  gate (once it has the badge from PR A). That is good for visibility but bad for control: there is
  no per-user opt-out the way episodes default to hidden behind `DisplayMissingEpisodes`. Virtual
  Movies should add a sibling preference (for example `DisplayMissingMovies`, or a generalized
  `DisplayMissingItems`) wired into the children query, with a matching web settings checkbox.

### 3. Scan reconciliation / cleanup: `SeriesMetadataService.cs`

`RemoveObsoleteSeasons()` / `RemoveObsoleteEpisodes()` delete virtual items superseded by real files
or left orphaned; the virtual-to-real flip lives in the same services. There is no generic,
type-agnostic reconciler, so Virtual Movies need an analogous one in the BoxSet/collection refresh
path: flip `IsVirtualItem=false` when a real file for that TMDB id is scanned in, and prune orphans.

### 4. Web "Missing" badge: `jellyfin/jellyfin-web`

The greyed/"Missing" treatment is two one-line conditionals, both gating on `Type === 'Episode'`:

- `src/components/indicators/indicators.js` (`getMissingIndicator`)
- `src/components/indicators/useIndicator.tsx` (React `getMissingIndicator`)

```js
if (item.Type === 'Episode' && item.LocationType === 'Virtual') { /* Missing / Unaired pill */ }
```

Broadening these (include `'Movie'`, or drop the type check and rely on `LocationType`) is the
entire web change. Everything else is already generic: the placeholder card icon, the play-button
suppression (`LocationType !== 'Virtual'`, type-agnostic), and the badge CSS (`indicators.scss`).
The BoxSet detail page applies no client-side missing filter, so a virtual Movie child already flows
through; it just lacks the badge until the two lines change.

> Caveat: many generic lists actively strip virtual items via `ExcludeLocationTypes: 'Virtual'`
> (playback, favorites, etc.), so virtual Movies stay out of those views by default, which is what
> we want. The BoxSet detail view is the one that would surface them.

> Don't conflate "virtual" with "missing". A pathless item is not necessarily something to acquire.
> There are at least three orthogonal reasons an item has no local file: Missing (not available to
> you, so acquire), Upcoming (announced/unreleased, so wait), and External (it exists, just not on
> this server, e.g. surfaced from a federated/remote Jellyfin instance, so stream/request it there
> rather than acquire). The badge/treatment must be driven by an explicit reason/state, never a
> blanket "Missing" inferred from `LocationType === 'Virtual'` alone, otherwise this change would
> mislabel external/federated items and rule out that separate, valid use case. Jellyfin already
> separates these axes a little: `LocationType` yields `Remote` (not `Virtual`) for
> non-local-but-reachable content, so a federation feature would likely lean on `Remote` plus its
> own marker rather than `Virtual`. This proposal should add Movies to the Missing path without
> assuming virtual implies missing.

---

## Summary table

| Mechanism | Generic (any `BaseItem`) | Shows-only (needs work for Movies) |
|---|---|---|
| `IsVirtualItem` data model | yes, `BaseItem.cs` | |
| `LocationType == Virtual` (Path null) | yes, `BaseItem.cs` | |
| `IsUnaired` | yes, `BaseItem.cs` | |
| "missing" accessor | | `Episode.IsMissingEpisode` (redundant w/ `LocationType`) |
| DB column + indexes | yes, `BaseItemEntity` / `BaseItemConfiguration` | |
| Query translation | yes, `BaseItemRepository.TranslateQuery` | |
| Creation of virtual items | only LiveTv | seasons/episodes only; no Movie path |
| Container virtual-reconciliation | | `SeasonMetadataService.SaveIsVirtualItem` (Season) |
| Display gate | | `User.DisplayMissingEpisodes` (episode-scoped) |
| Scan cleanup / orphan GC | | `SeriesMetadataService.RemoveObsolete*` |
| DTO to client (`LocationType`) | yes, `BaseItemDto` / `DtoService` | |
| BoxSet membership (LinkedChild by Id) | yes, `Folder.ResolveLinkedChildren` | |
| Web "Missing" badge | | 2 one-line gates (`indicators.js`, `useIndicator.tsx`) |

## What Virtual Movies would require (concrete)

- New creation path: mint a virtual `Movie` (`IsVirtualItem=true`, `Path=null`, populated from TMDB
  collection data) and link it into the BoxSet via `LinkedChild.ItemId`. (The companion MindTheGaps
  plugin already computes this missing-movie diff and could mint the items via the library APIs from a
  plugin, but a server-native creation path is cleaner and is what enables the reconciliation below.)
- Reconciliation: a BoxSet-refresh analog of `SaveIsVirtualItem` + `RemoveObsolete*` (flip on
  real-file scan-in; GC orphans). This is the largest net-new server piece.
- Display gate: add a sibling `DisplayMissingMovies` preference wired into the BoxSet children query.
  Don't overload the episode-scoped `DisplayMissingEpisodes`.
- Pathless-safety review: audit `Movie.GetLookupInfo`/`BeforeMetadataRefresh` and anything assuming
  a non-null movie `Path`.
- Web: broaden the two `getMissingIndicator` conditionals to cover Movies, but key the badge on an
  explicit reason/state (Missing / Upcoming / External), not on `LocationType === 'Virtual'` alone,
  so federated/external items aren't mislabeled as "Missing" (see the callout above).

Bottom line: roughly 80% of the plumbing is already type-agnostic. The ask is a creation path, a
reconciler, and a display toggle on the server, plus a two-line indicator tweak on the web.

---

## Related core ask: a gap-source SPI (`IGapSource`) in a host assembly

> Separate from virtual Movies, but the same kind of core ask, so noting it here.

Jellyfin's extensibility model only lets separate plugins contribute implementations of an interface
when that interface lives in a host assembly (`MediaBrowser.Controller`/`Model`), which is loaded once
in the Default `AssemblyLoadContext`. Each plugin runs in its own isolated, collectible
`PluginLoadContext` (`Emby.Server.Implementations/Plugins/PluginLoadContext.cs`) with a per-plugin
`AssemblyDependencyResolver`; unresolved assemblies fall through to Default. Because plugins reference
`Jellyfin.Controller` with `ExcludeAssets=runtime` (they don't ship the host DLLs), every plugin's
implementation of, say, `IMetadataProvider` binds to the same host-loaded type (shared identity), and
the host discovers them via `GetExports<T>()` across all plugin assemblies.

A contract defined inside a plugin gets no such guarantee: a second plugin implementing it would
either ship a duplicate of the defining DLL (a different type identity, never collected) or fail to
resolve it at all. So there is no first-class plugin-to-plugin SPI today.

Implication for a "gap finder" ecosystem: the MindTheGaps plugin's `IGapSource` / `GapItem` /
`OwnershipIndex` are deliberately provider-agnostic and clean, but as long as they live in the plugin,
every gap source (TMDB, Trakt, library, future Music/Book sources) must ship inside that one plugin.
To let independent gap-provider plugins exist, exactly as metadata-provider plugins do today, the SPI
would need to be upstreamed into a host assembly:

- `IGapSource` plus the small result model (`GapItem`, `GapPattern`, `MediaDomain`) into
  `MediaBrowser.Controller`/`Model`,
- the host collecting them via `GetExports<IGapSource>()` (the existing mechanism),
- the gap engine/UI remaining a plugin that consumes whatever sources the host discovers.

This is a small, well-scoped core PR (an interface plus a few DTOs plus one discovery call), and it's
the only way to get the "infra in core, providers as plugins" architecture under Jellyfin's loader.
It's a natural companion to the virtual-Movies work for anyone interested in first-class gap support.
