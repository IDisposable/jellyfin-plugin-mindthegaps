# 11. Series-content gaps respect eras and verify episode identity

Status: Accepted.

## Context

A missing-episode gap is only useful if it is genuinely missing and genuinely something you would want.
Two failure modes showed up against real libraries:

- A long-running show you partly own (say you have 2008 onward of a series that started in 1974) would
  list every episode from the original run as "missing," burying the few you actually lack. The reverse,
  a true reboot or revival years after the owned run ended, should not be folded into that run.
- An episode you do own can be reported missing when the providers number it differently from your files:
  a two-part story your files split as two episodes at one number, or an off-by-one season numbering, so
  the same episode by title sits at a different code.

## Decision

Episode identity is checked at two layers, against the library's numbering as the authority:

- At scan time, two things. `EpisodeEra` scopes an owned series to its era: `Expand` grows the owned year
  range through the contiguous missing episodes around it and `IsOutside` drops a candidate that falls
  outside it, so a partly-owned continuous run stays one era while a detached reboot does not pull its
  episodes in. And the cross-check diff (`OwnedEpisodes`) reconciles a candidate against the owned
  episodes by air date and folded title, not just by number, before calling it missing, so a provider
  that renumbers, reorders, or splits a two-part episode the library merged is recognized as already
  owned rather than reported as a wall of false gaps.
- In the diagnosis (explaining one gap, not scanning), the episode check goes past the air window to
  identity: it confirms whether you own the gap's season and number, and, failing that, whether you own
  an episode with the same title at a different number. That reports "you already own this, renumbered"
  instead of a bare "missing."

The reconciliation and the diagnosis fold a title the same way (`EpisodeTitleKey`: a trailing part marker
like "(1)" or "Part 1" stripped, then normalized) and treat an air date as the cross-numbering invariant.
None of it feeds `SeriesGapKey`: a gap id stays derived from the season and number alone, because the id
is a persistence contract (ADR-0008) and a fuzzy date or title match is not stable enough to key on. The
fuzzy signals decide whether an episode is already owned, never what a genuinely missing one is called.

## Consequences

- The partly-owned long-running show lists only the episodes you truly lack, a reboot stays a separate
  concern, and a season a cross-check provider numbers differently from your library (a renumber, a
  reorder, or a merged two-parter) no longer reads as entirely missing.
- The diagnosis can still say "owned under a different number" for a single gap, which a year-range check
  alone misses.
- Air date and the folded title are heuristics; an unusual numbering, or an episode genuinely missing
  that happens to share an air date with an owned one, can still mislead them. Keeping all of it out of
  the gap id means a wrong guess only ever suppresses or explains a row, never corrupts the persisted ids
  or the resolutions keyed on them.
