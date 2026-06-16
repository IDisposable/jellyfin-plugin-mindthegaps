# 4. Virtual-item minting is experimental, opt-in, reversible

Status: Accepted (temporary; expected to be superseded by core support).

## Context

The most compelling presentation of a gap is a placeholder rendered in place, the way a missing
episode shows inside a series. That needs server-side creation of virtual items plus reconciliation
(flip to real when the file arrives, garbage-collect orphans), which the server only does for
shows. We want both a working demonstration and concrete evidence of the friction for the upstream
proposal.

## Decision

Ship a minter that creates tagged, pathless virtual `Movie` items into BoxSets via the public library
APIs. It is off by default, gated by a per-pattern selection (`MintPatterns`), and fully reversible
from the settings page. Only the `SetCompletion` pattern is materializable today (missing collection
movies into a BoxSet); `CreatorWorks` and `Recommendation` have no container to render into yet. The
code and UI state plainly that this belongs in core.

## Consequences

- All library mutation is opt-in and undoable; every minted item carries a marker provider id so it
  can be found and removed.
- The plugin must run its own reconciliation, because the server will not flip or GC these items.
  There is also no per-user display toggle. Both are real costs, and both are exactly the pain the
  upstream discussion argues from (`docs/upstream/discussion-mint-virtual-items.md`).
- This is throwaway by design; if and when core grows a virtual-item API, the minter goes away.
