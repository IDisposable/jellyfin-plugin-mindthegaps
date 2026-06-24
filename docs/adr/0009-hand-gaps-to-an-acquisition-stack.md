# 9. Hand a gap off to an external acquisition stack

Status: Accepted.

## Context

The dashboard tells you what is missing; the obvious next click is "go get it." Most users already run
an acquisition stack: Radarr for movies, Sonarr for series, or a Jellyseerr/Overseerr request portal in
front of both. Reproducing any of that (indexers, download clients, quality decisions) inside the plugin
would be a second application living in the wrong place.

## Decision

The plugin does not acquire anything itself; it hands a gap to whatever the user already runs.
`Services/Acquisition/AcquisitionService` sends a movie to Radarr (by TMDB id), a series or its missing
episodes to Sonarr (by the owning series' TheTVDB id, resolved from the library), or any title to
Jellyseerr/Overseerr as a request. Each target is opt-in and only offered once it is fully configured
(URL, key, and for Radarr and Sonarr a quality profile and root folder). A send is a per-row button (and
a bulk form), rehydrated server-side from the gap id, never automatic.

## Consequences

- The plugin stays a "what is missing" tool and leaves downloading to the tools built for it. No indexer,
  client, or quality logic lives here.
- It is fire-and-forget: a send adds the title to the remote and reports success or the remote's error,
  but the plugin does not track the download or flip the gap. The next scan drops the gap once the file
  lands, the same reconciliation as everything else.
- Each user wires their own endpoints and keys into config; nothing is assumed or discovered. A gap with
  no usable id for a target (no TMDB id for Radarr, no resolvable TheTVDB id for Sonarr) simply cannot
  offer that send.
