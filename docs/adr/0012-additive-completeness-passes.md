# 12. Completeness passes are additive and freeze the contracted ids

Status: Accepted.

## Context

The music sources find missing albums for an owned artist two ways: MusicBrainz, the primary, which keys
the gap ids, and, for an artist MusicBrainz already covers, a Discogs "completeness" pass that can
surface releases MusicBrainz did not. Folding a second provider into an artist already keyed by the first
risks re-keying gaps that the resolutions and the availability carry-forward depend on (ADR-0008).

## Decision

A completeness pass is strictly additive. The Discogs pass runs only for an artist MusicBrainz already
covered, contributes only releases not already present by normalized title, and never re-keys or replaces
a MusicBrainz-sourced gap. The MusicBrainz ids are frozen: the completeness pass adds new gaps under its
own ids and leaves the existing ones exactly as the primary source minted them. The shared
`MusicArtistGapSourceBase` was made provider-agnostic so the Discogs and MusicBrainz halves reuse one
owned-artist walk without either redefining the other's keys.

## Consequences

- Existing resolutions and carry-forward keep matching, because the ids the primary source owns do not
  move when a second provider is added.
- A release both providers know, titled slightly differently, can in principle appear twice; the
  normalized-title exclusion catches the common cases, and the cost of an occasional duplicate is far
  smaller than silently orphaning a resolution.
- Completeness is gated (a Discogs token, the circuit closed, the artist already MusicBrainz-covered), so
  it is extra coverage when available, never a dependency.
