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

Two checks, at two layers:

- At scan time, `EpisodeEra` scopes an owned series to its era. `Expand` grows the owned year range
  through the contiguous missing episodes around it, and `IsOutside` filters any candidate episode that
  falls outside that era, so a partly-owned continuous run stays one era while a detached reboot does not
  pull its episodes in.
- In the diagnosis (explaining one gap, not scanning), the episode check goes past the air window to
  identity: it confirms whether you own the gap's season and number, and, failing that, whether you own
  an episode with the same title at a different number (stripping a trailing part marker like "(1)" or
  "Part 1" for the comparison only). That reports "you already own this, renumbered" instead of a bare
  "missing."

The part-marker stripping and the title comparison live only in the diagnosis. They never feed
`SeriesGapKey` or the scan's identity, because a gap id is a persistence contract (ADR-0008) and a fuzzy
title match is not stable enough to key on.

## Consequences

- The common partly-owned long-running show lists only the episodes you truly lack, not its entire back
  catalogue, and a reboot stays a separate concern.
- The diagnosis can say "owned under a different number" for the renumbering cases, which a year-range
  check alone misses.
- The era boundary and the title match are heuristics; an unusual numbering can still mislead them.
  Keeping the fuzzy parts out of the gap id means a wrong guess in the diagnosis never corrupts the
  persisted ids or the resolutions keyed on them.
