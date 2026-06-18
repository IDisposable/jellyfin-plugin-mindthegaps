# 7. Library-aligned media domains (and why not CollectionType)

Status: Accepted.

## Context

A coarse `Video`/`Music`/`Book` domain split lumps movies and shows into one `Video` bucket, so the
dashboard cannot separate "missing movies" from "missing episodes", and it does not line up with how
people organise Jellyfin libraries.

## Decision

Make `MediaDomain` (ADR-0001) library-aligned: `Movies`, `Shows`, `Music`, `Books`, `MusicVideos`. Each
source tags its gaps with the right one (collections and filmographies are Movies; series content and
series recommendations are Shows). The dashboard groups and filters by domain; `TargetKind` keeps the
finer Jellyfin kind (Movie/Series/Episode/...).

A custom enum is kept on purpose rather than reusing the core type.

## Considered: Jellyfin core enums

- `Jellyfin.Data.Enums.CollectionType` (exposed in the referenced NuGet) maps almost 1:1:
  `movies`, `tvshows`, `music`, `musicvideos`, `books`. It is the natural choice if `IGapSource` is
  ever upstreamed into core (ADR-0002). Against it now: its members are lowercase legacy names that
  serialise as `"tvshows"` etc. (the dashboard groups on the serialised name, so it would need a
  display-name map), and it carries values irrelevant to gaps (`trailers`, `homevideos`, `boxsets`,
  `photos`, `livetv`, `playlists`, `folders`).
- `MediaType` (`Video`/`Audio`/`Photo`/`Book`) is the coarse bucket we are moving away from.

So `CollectionType` is the core type to switch to if the model is ever upstreamed; until then the
custom `MediaDomain` stays for display-friendly names and a domain set scoped to exactly the gaps.

## Consequences

- The dashboard shows clean "Movies" / "Shows" groups with no mapping layer.
- If we upstream a gap SPI, revisit this and align to `CollectionType` (a thin map is enough).
