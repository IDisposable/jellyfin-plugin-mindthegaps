# 16. Clear down a filled gap anywhere: verify first, re-check on request

Status: Accepted.

## Context

[ADR-0013](0013-recheck-one-series.md) gave a series a refresh icon that re-runs its sources in place, so a
metadata fix could be verified without a full rescan. Nothing equivalent existed for the rest of the report:
having actually acquired a movie from a collection, an album from a discography, or a book from a
bibliography, the only way to get the row off the list was a full scan (and for the rotating patterns, a scan
that happened to revisit that seed).

Two different operations were being asked for under one name. "I filled this gap, clear it" only needs to
know what the library holds now. "Has this set changed?" needs the provider asked again. They cost very
different amounts: the first is a local, indexed query, the second is a live API call plus, for the sources
that diff against the whole library, an ownership index.

## Decision

Split them, and lead with the cheap one.

`LibraryVerifier` answers "does the library hold this one gap now?" with a focused `HasAnyProviderId` query
per gap, rather than the library-wide read `GapEngine.BuildOwnershipIndex` does. It mirrors the scan's
ownership rules, including the album artist-and-title name fallback, and deliberately no wider: clearing a
row the next scan would only re-report is worse than leaving it listed. It is kind-agnostic, so it works for
every domain, pattern, and target kind with no per-source code. `POST Verify` takes gap ids and rehydrates
each server-side.

Confirming one title owned then clears every gap about that title, not only the row that was clicked. A
missing film is normally several gaps with several ids: a hole in its collection, in a studio set, in its
director's filmography, in a curated list, and a recommendation. `GapTargetKey` gives a gap the identity of
its subject rather than of itself, keyed the way `OwnershipIndex` keys the library (any shared provider id
under the same kind, plus the album name key), and `GapStore.RemoveGaps` drops everything matching.

`ISetContentSource` is the per-owning-item seam, the set-shaped sibling of `ISeriesContentSource`: a
`Claims(owner)` predicate, a `CheckOneAsync(owner, ...)` step, and the `GapIdPrefix` its gaps carry.
`CollectionGapSource`, `MusicArtistGapSourceBase` (both music providers), and `BooksBibliographyGapSource`
implement it, in each case by exposing the per-entity step their scan loop already had.
`GapEngine.RecheckManyAsync` dispatches per owner on what it is (set claimants, else a series through the
series-content sources) and `GapStore.ReplaceSourceGaps` swaps out exactly those sources' gaps for that
owner. ADR-0013's `RecheckSeries` endpoint and `ReplaceSeriesGaps` are folded into these rather than left
beside them: two endpoints differing only by the owner's kind was an accident of the order they were written,
and gave no answer for a music artist.

The dashboard exposes one control for both, the refresh icon, at every level of the tree and on every row:
the domain rollup, a set-kind heading, a group header, a season header, and a row. A labelled "Clear what I
have" button in the toolbar is the same routine over the whole filtered tab, spelled as a button because the
toolbar has no row to hang an icon on. All of them run one routine at a different width, so there is one
behaviour to learn and nothing to choose between; a level is a `data-scope`, not a code path. ADR-0013's
per-series refresh icon is that same control now, not a second one beside it.

The multi-owner half is a batch: `GapEngine.RecheckManyAsync` builds the ownership index once rather than per
item, runs behind `RecheckRunner` (the background-runner shape the scan and the explore use), and swaps each
item into the report as it finishes. Every re-check goes through it, a single group being a batch of one, so
the API is one pair, `RecheckSources` and `RecheckStatus`, rather than a single-item endpoint and a batch
one. It re-checks only the sources that still had gaps after the verify, and it dispatches on what the owning
item is, so a series routes to the series-content sources and the Shows tab works the same as the Movies one.

## Consequences

- Filling a gap can be cleared from anywhere in the report, in any domain, with no provider call and no
  rescan. The expensive half is never spent on the common case.
- Acquiring one title clears it from every tab it appeared on at once, including tabs the client has not
  loaded, which is why the sweep is server-side: the client cannot prune what it has not fetched. The verify
  response therefore reports a removal count higher than the number of rows in scope, and the dashboard says
  how many went elsewhere rather than letting the totals move unexplained.
- The scopes are one behaviour at six widths, so there is nothing new to learn moving up the tree, and the
  batch stage never runs without a count in the prompt first.
- The clear-down is offered in places its second stage cannot serve (a studio, a curated list, a film
  filmography), where it degrades to the verify half. That is deliberate: a control that appears and
  disappears by source would be harder to learn than one that is always there and sometimes offers less.
- A batch re-check writes per item, so cancelling it (or a shutdown) leaves a partially refreshed report
  rather than an all-or-nothing one. That is the right trade for a report that is already a running estimate,
  but it does mean two sets under one heading can carry different freshness.
- A source that cannot answer returns null rather than an empty list, and only a source that answered has
  its gaps replaced. Without that distinction a transient provider outage reads as "this collection is
  complete" and deletes its real gaps, which a re-check has no way to restore. Cancellation propagates for
  the same reason: an interrupted owner must not be committed as a successful empty re-check.
- Gap identity is transitive across provider ids. One title's rows do not all carry the same ids (the
  availability pass resolves an IMDb id onto some and not others), so a TMDB-only row and an IMDb-only row
  are the same film linked only through a row carrying both. `GapTargetKey` grows its key set through what
  it matches until nothing new appears, so clearing any one representation clears them all rather than only
  those the clicked row happened to overlap.
- Verify clears filled gaps but never discovers new ones, which is why the re-check prompt exists where a
  source supports it. The filmography and recommendation sources were left without the seam: they scan a
  slice of their seeds per run and accumulate across scans (ADR-0012), so a per-creator re-check would cut
  across that model, and their value is discovery rather than completing a set you can point at. Those groups
  get the verify half only. Nothing structural stops the seam being added later.
- The verify name fallback is an exact-title query plus a normalized compare, slightly narrower than the
  index's fully normalized key. Like `OwnershipIndex.OwnsByName`, it can only fail toward leaving a gap
  listed. It stays album-only because that is all `BuildOwnershipIndex` name-keys.
- Both are partial updates: neither bumps the report's scan time or version, so neither clears the
  post-upgrade rescan nudge. An owning item nothing claims is skipped before the batch starts rather than
  re-checked to an empty result, so a mis-aimed call cannot silently wipe that item's gaps; the client also
  only offers the prompt for owners it knows can be re-run.
- The dashboard prunes cleared ids from every cached tab, then re-reads the summary. It cannot count what it
  never loaded, and the sweep's whole point is clearing rows on tabs the browser has not opened, so the
  counts have to come from the server rather than from local arithmetic.
