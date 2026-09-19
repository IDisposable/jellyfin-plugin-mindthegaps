# 19. The web UI surfaces are an API, and the script is one client of it

Status: Accepted.

## Context

Jellyfin has no hook for a plugin to extend its web client, so the plugin adds a client script to
jellyfin-web's index.html at request time and the script draws sections on the person, movie/series, and home
pages. Those sections read their data from endpoints the plugin serves.

The endpoints were gated on the same master switch as the script. Turning the script off turned the data off,
which tied a decision about where a surface is drawn to a decision about whether its data exists. Nothing in
an endpoint needs the script, and a client other than jellyfin-web (a native app, a companion tool) has no
other way to ask for a person's missing titles or a title's related suggestions.

## Decision

The master switch (`WebUiEnabled`) governs only whether the script is added and served. Each surface's data
endpoint is governed by that surface's own toggle (`PersonPageEnabled`, `ItemPageEnabled`, `HomeRowEnabled`)
and nothing else, so another client can use a surface without the script being injected. The rules are in one
place, `WebUiGate`, which reads the configuration per request so a change needs no restart, and is tested
without a server.

`WebUi/Detail` (a proxied TMDB lookup for one title, from its id) is open to any signed-in user whatever the
toggles say: it carries nothing about the library. The write endpoints (Send, TODO) and `WebUi/Profiles`
stay administrators only.

The audience of the surface reads is any signed-in user. The shapes are experimental and may change; an API
with no consumers cannot be shaped by them, so it is offered before there are any.

## Consequences

- A surface turned on is available to any signed-in user through the API, not only to whoever sees the
  script's sections, and the settings help and the configuration reference say so.
- The reads are not filtered by the caller. The ownership index is one shared cache for the whole server, so a
  title is listed if the library does not hold it, regardless of the caller's library access or parental
  rating. Filtering by the caller means resolving the user on each request and applying their restrictions to
  data that is TMDB's, not a library item's, and is deferred.
- The home Discover row ranks every `Recommendation`-pattern gap in the report. The personal-list sources
  (watchlists and lists from JustWatch, IMDb, MDBList, Trakt, TMDB accounts, and TheTVDB favorites) emit that
  pattern too, so titles from them can appear. Restricting the row to the recommendation source, or letting
  the administrator choose, is deferred.
- Each read can spend a TMDB request and a library read on the server's behalf. Both are cached, and this is
  accepted.
- Answering 404 while a surface's own toggle is off is kept, so a surface is an explicit opt-in.
