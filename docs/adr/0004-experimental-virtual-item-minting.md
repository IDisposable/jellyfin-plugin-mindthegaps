# 4. Virtual-item minting is experimental, opt-in, reversible

Status: Accepted (temporary; expected to be superseded by core support).

## Context

The most compelling presentation of a gap is a placeholder rendered in place, the way a missing
episode shows inside a series. That needs server-side creation of virtual items plus reconciliation
(flip to real when the file arrives, garbage-collect orphans), which the server only does for
shows. We want both a working demonstration and concrete evidence of the friction for the upstream
proposal.

## Decision

Ship a minter that creates tagged, pathless virtual items via the public library APIs, fully reversible.
It is driven from the report one gap at a time (a per-row Mint button and a multi-select "Mint
selected"), never automatically. It mints any kind the report surfaces as something you could acquire: a
`Movie`, a `Series` shell, a `MusicAlbum`, or a `Book`. Each lands where it belongs: a collection gap
into its BoxSet, a music album under its resolved artist, a book under its author, anything else into a
catch-all collection; a filmography gap also attaches the owned person. The plugin runs its own
reconciliation at the end of every scan (drop a minted item once the real file is owned). Missing
episodes are still left to the server, which already synthesizes them inside a series, so minting a
`Series` mints only the shell, not its episodes. The code and UI state plainly that this belongs in core.

## Consequences

- All library mutation is opt-in and undoable; every minted item carries a marker provider id so it
  can be found and removed.
- The plugin must run its own reconciliation, because the server will not flip or GC these items.
  There is also no per-user display toggle. Both are real costs, and both are exactly the pain the
  upstream discussion argues from (`docs/upstream/discussion-mint-virtual-items.md`).
- This is throwaway by design; if and when core grows a virtual-item API, the minter goes away.
