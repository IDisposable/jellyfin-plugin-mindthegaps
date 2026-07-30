# 17. Serve the dashboard's vocabulary and policy instead of restating them

Status: Accepted.

## Context

The dashboard held its own copy of the report's vocabulary: the pattern list, the media domains and their
display order, and the Set completion group kinds and theirs. None of it was derived from the model, so each
was a retyping of something the server already defined. `CATEGORY_ORDER = { Movies: 0, Shows: 1, Music: 2,
Books: 3 }` is the clearest case: those are `MediaDomain`'s own numeric values, written out again.

Nothing checked the copies against the originals, and three had already drifted:

- `SET_KIND_ORDER` keyed on `'Labels'`, but `setKindLabel` produces `'Record labels'`, so Discogs label sets
  sorted as unknown instead of in their slot.
- `Subject` (OpenLibrary subject sets) had no label at all, so those sets grouped under a heading reading
  **Other**.
- `GapSourceMerge` listed the curated-list source types as `"List"` and `"MdbList"`, omitting `"TraktList"`,
  so a Trakt list did not outrank a per-title recommendation the way the other two did.

A test pinning the copies in sync would have caught all three, but the duplication is what makes the test
necessary.

## Decision

Serve the vocabulary. `GapSummary` gains `Patterns`, `Domains`, and `SetKinds`, so the page renders its tabs,
its Type selector, and its Set completion grouping from the model's own definitions. The summary is already
fetched before anything renders, so this costs no extra request. `PATTERNS`, `ALL_DOMAINS`, `CATEGORY_ORDER`,
and `SET_KIND_ORDER` are deleted from the dashboard.

`SourceItemTypes` gives `GapItem.SourceItemType` a home. It stays a string on the wire, because the values
are a mix of Jellyfin item kinds and the plugin's own set kinds with no single enum behind them, but the
spellings are now named once and the mappers use them. It also owns the two orderings that were loose in the
dashboard and in `GapSourceMerge`: `SetKindsInOrder` and `CuratedListKinds`.

`MediaDomains` splits the enum into `Implemented` and `NotYetImplemented`, and the summary serves only the
first. `MediaDomain.MusicVideos` is named by the model but no source fills it; the dashboard's hand-written
list had quietly omitted it, and this makes that a stated fact rather than an accident.

The same applies to policy the page was inferring rather than being told. `RecheckPrefixes` (from
`GapEngine`, over the sources actually enabled) replaces a rule that guessed re-checkability from a gap's
pattern and domain, and could not see config at all: with music scanning off, the page still offered a
re-check on an artist that the batch would then skip. `MintableKinds` (from `VirtualItemMinter`) replaces a
`switch` transcribed from the minter's own, which would have gone stale the moment a mintable kind was added.

What stays in the dashboard is wording: `PATTERN_LABELS`, and the values (not the keys) of
`SET_KIND_LABELS`. Those are presentation, and are where localization would land. So are the checks that
route to external services (Radarr takes a movie, Sonarr the owning series): those describe what those tools
accept, not what this plugin decides, and moving them to the server would gain nothing.

## Consequences

- A new domain, set kind, re-checkable source, or mintable kind reaches the dashboard by being defined, not
  by being defined and then copied. The three drifts above are no longer expressible.
- Two controls now follow config, not just code: a source switched off stops offering its re-check, where the
  page used to offer one regardless and let the batch quietly skip it.
- One duplication is left on purpose: how each set kind is worded. A test pins that every served kind has a
  label, and an unworded kind falls back to its own name rather than being pooled into "Other", so the
  failure is visible instead of silent. Nothing guards against the deleted lists being reintroduced: a test
  asserting those names stay absent would pass the moment someone spelled them differently, which is not a
  guarantee worth the line.
- `GapSummary` is a wire contract, so this widens it. The page could not render tabs before the summary
  loaded anyway (the tab counts come from it), so nothing new is gated on it.
- String comparisons in the dashboard (`it.PatternName === 'SetCompletion'`) are untouched. Sharing cannot
  help there, since the literal stays a literal wherever it is written, and the risk is negligible: renaming
  an enum member breaks a whole tab loudly rather than subtly.
- The client is now wrong if it renders before the summary arrives, where before it had defaults. The
  vocabulary helpers return empty lists in that window, which yields an empty tab strip rather than a wrong
  one, and the existing load order means it does not happen.
