# 1. Gaps are 3 patterns across N media domains

Status: Accepted.

## Context

Many different "gaps" share one shape: a known member of an owned container is missing (a franchise
movie, a series episode), a work by an owned creator is unowned (a filmography film), or a related title
is worth discovering. Modeling each as its own special case, with more added for music discographies or
book series, would not scale.

## Decision

Model every gap as one of three patterns across N media domains:

- Patterns: `SetCompletion` (a known member of an owned container is missing), `CreatorWorks` (a work
  by an owned creator is missing), `Recommendation` (a related title for discovery).
- Domains: `MediaDomain` of `Movies`, `Shows`, `Music`, `Books`, `MusicVideos` (library-aligned).
  The finer Jellyfin item kind lives in `TargetKind`.

`GapItem` is provider-agnostic: a `ProviderIds` map plus `ExternalLink[]`, tagged with its pattern and
domain. Ownership is a single generic check, `OwnershipIndex.Owns(kind, provider, id)`. Each
`IGapSource` declares the `OwnedKinds` it diffs against, and the engine indexes exactly those.

## Consequences

- New sources and whole new domains slot in without touching the engine.
- Configuration and UI organize around the three patterns (for example, which patterns to mint).
- Some sources stay domain-specific internally (the collection source only understands TMDB movie
  collections) but still emit the generic `GapItem`, so the engine and UI never special-case them.
