# Discussion B (server): mint virtual items for any type, not just show episodes

## Summary

Jellyfin already models, stores, queries, and serves virtual ("missing") items generically on
`BaseItem`. The only place virtual items are ever created is the Series metadata pipeline (seasons and
episodes). I would like to gauge appetite for a generic creation-and-reconciliation path so other
types, starting with movies inside a collection, can be minted as virtual items and render
greyed-out the way missing episodes already do.

The concrete motivating case: a partially-owned movie collection (for example three of the four
Matrix films) should be able to show the missing entry as a greyed-out placeholder, exactly like a
missing episode inside a series.

## What already exists and needs no change

All of this is generic on `BaseItem` today (refs against internal `12.0.0`; line numbers drift):

- Data model: `IsVirtualItem`, and `LocationType.Virtual` computed from a null/empty `Path`. A
  pathless `Movie` already reports `LocationType.Virtual`.
- Persistence: `IsVirtualItem` is a plain column with composite indexes keyed by `Type`. No schema
  change.
- Query translation: `IsMissing`/`IsVirtualItem`/`IsUnaired` predicates apply to all rows; type
  scoping only happens because callers set `IncludeItemTypes`.
- DTO/API: the client keys off `LocationType`, which is set for every item. A pathless movie already
  serializes `LocationType: Virtual`.
- BoxSet membership: `ResolveLinkedChildren` resolves children by id, type-agnostically. A virtual
  movie linked by id resolves and renders like any other child.

## What is shows-only (the actual work)

1. Creation. The only minting path is `SeriesMetadataService`/`SeasonMetadataService` (it news up a
   pathless `Season`/`Episode` and `AddChild`s it). Movies/BoxSets mint nothing virtual. A generic
   path, or a movie-specific one driven by the collection refresh, would create a pathless `Movie`
   from collection data and link it into the BoxSet.
2. Reconciliation. `RemoveObsoleteSeasons`/`RemoveObsoleteEpisodes` and the virtual-to-real flip live
   in the Series services. There is no generic reconciler. A new path must flip `IsVirtualItem=false`
   when a real file for that id is scanned in, and prune orphans. This is the largest net-new piece.
3. Display gate. `User.DisplayMissingEpisodes` is episode-named and episode-scoped (it is applied in
   `Series.GetEpisodes` and the played-status sweep, not to generic children queries). A BoxSet's
   children are not gated by it, so minted virtual movies would show for everyone with no per-user
   opt-out, unlike episodes which default to hidden. This wants a sibling preference (for example
   `DisplayMissingMovies`, or a generalized `DisplayMissingItems`) wired into the children query,
   plus a matching settings checkbox in jellyfin-web. This is part of this work, not a separate ask.

## Possible shapes (seeking direction)

- A. A core feature: the BoxSet/collection refresh mints and reconciles virtual movies, mirroring the
  Series services. Self-contained, no plugin needed, but bakes the "fill collections from TMDB" policy
  into core.
- B. A host API: expose a small, supported way for a plugin to create/reconcile virtual items
  (`IVirtualItemManager` or similar), so policy (what to mint, from which source) lives in plugins
  while the lifecycle (creation, the virtual-to-real flip, orphan GC) stays correct and centralized.
  This is my preference: it keeps core generic and lets a gap-finder plugin own the "what".
- C. Do nothing in core; plugins mint via existing library APIs. Possible today but fragile: the
  reconciliation/GC semantics are exactly what plugins get wrong, which is why those live in the Series
  services.

## Out of scope but worth stating

Do not conflate "virtual" with "missing". A pathless item can be Missing (acquire), Unaired (wait),
or External (exists on another reachable server, which already reports `LocationType.Remote`). Any
display/treatment should be driven by an explicit reason, never a blanket "Missing" inferred from
`LocationType == Virtual` alone, so this does not preclude a federation/remote use case.

The web half (showing the badge for non-episode virtual items) is a separate two-line change tracked
in PR A.

## Prior art

A working out-of-tree plugin already computes these gaps end to end (diffing a partially-owned TMDB
collection against the library) and presents them as a dashboard todo list. The detection is proven
from a plugin; the native rendering and reconciliation are the part that wants core support.
