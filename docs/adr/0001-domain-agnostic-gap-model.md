# 1. Gaps are 3 patterns across N media domains

Status: Accepted.

## Context

The plugin began as "collection gaps" (missing movies in a partially-owned franchise). The same shape
kept recurring: an owned actor's unowned films, an owned series' missing episodes, recommended titles,
and later music discographies and book series. Modelling each of these as its own special case would
not scale.

## Decision

Model every gap as one of three patterns across N media domains:

- Patterns: `SetCompletion` (a known member of an owned container is missing), `CreatorWorks` (a work
  by an owned creator is missing), `Recommendation` (a related title for discovery).
- Domains: `MediaDomain` of `Video`, `Music`, `Book`.

`GapItem` is provider-agnostic: a `ProviderIds` map plus `ExternalLink[]`, tagged with its pattern and
domain. Ownership is a single generic check, `OwnershipIndex.Owns(kind, provider, id)`. Each
`IGapSource` declares the `OwnedKinds` it diffs against, and the engine indexes exactly those.

## Consequences

- New sources and whole new domains slot in without touching the engine.
- Configuration and UI organize around the three patterns (for example, which patterns to mint).
- Some sources stay domain-specific internally (the collection source only understands TMDB movie
  collections) but still emit the generic `GapItem`, so the engine and UI never special-case them.
