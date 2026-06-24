# 10. Explore kinds self-describe via IExploreSource

Status: Accepted.

## Context

Besides the scheduled scan, the dashboard has an "Explore a source" action: pick a kind (a studio, a
keyword, a Discogs label, a TMDB or MDBList list), choose the specific source, and merge its unowned
titles into the report right now without touching saved settings. Each kind needs a label, a way to
search for a source by name (or, for a list, a raw-id box), and a way to run one source on demand. Wiring
that as a switch in the engine, a parallel switch in the controller, and a third in the dashboard meant
three places to edit per new kind, easy to get out of step.

## Decision

A gap source that is explorable declares it. `IExploreSource` exposes one or more `ExploreDescriptor`s,
each carrying its `Kind`, `Label`, an optional `Search` delegate (its absence means a raw-id kind like a
TMDB list), and a `Run` delegate. `ExploreRegistry` collects the descriptors from every registered source
and is the single lookup; `ExploreRunner` runs one on demand; the dashboard builds its kind dropdown and
its search-versus-raw-id behavior from the `Explore/Kinds` endpoint. Adding an explorable kind is now one
descriptor on the source, with nothing to change in the engine, controller, or UI.

## Consequences

- One source owns the definition of its explore kinds; the registry, runner, controller, and dashboard
  are generic over them.
- The dashboard no longer hardcodes the kind list, so a new kind appears in the UI as soon as its source
  ships a descriptor.
- The descriptor is a small indirection (delegates rather than direct calls), and a source must opt in by
  implementing the interface; a source that never declares a descriptor is simply not explorable.
