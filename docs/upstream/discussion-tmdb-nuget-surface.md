# Discussion C (server): expose the shared TMDB client/key to plugins via the published NuGet

## Summary

The server already builds and DI-registers a shared TMDB client with a cache and an API key
(`TmdbClientManager`, registered as a singleton in `ApplicationHost`, plus the embedded key in
`TmdbUtils.ApiKey`). A plugin can receive `TmdbClientManager` by constructor injection at runtime
today. The catch is purely compile-time: the type lives in `MediaBrowser.Providers`, which is not
published as a plugin-consumable NuGet, so a standalone plugin (built against `Jellyfin.Controller`
and friends) cannot name the type to receive the injection. It must instead carry its own TMDB
client and its own key.

I would like to discuss exposing a small, supported surface so plugins can reuse the host's TMDB
client (its cache, rate-limiting, and key) instead of duplicating it.

## Why it matters

A plugin that does TMDB lookups today has two options, both worse than reusing the host:

- Reference `MediaBrowser.Providers` via a project reference to a server checkout (what this plugin
  does now). That blocks a clean, NuGet-only standalone build.
- Hand-roll its own TMDB client (TMDbLib plus its own cache) and bring its own key. This works, but
  it duplicates the cache, doubles TMDB request volume, and either ships a second copy of the public
  key or asks the user to configure one they have already configured for the metadata provider.

`MediaBrowser.Controller` does not reference `MediaBrowser.Providers` (the dependency runs the other
way), so the shared client cannot simply move into the published assembly as-is.

## Options (seeking direction)

- A. Publish `MediaBrowser.Providers` as a reference NuGet (for example `Jellyfin.Providers`).
  Smallest change; plugins compile against the existing `TmdbClientManager`/`TmdbUtils` directly. The
  downside is publishing a large concrete surface as a plugin contract.
- B. Define a narrow interface in `MediaBrowser.Controller` (published as `Jellyfin.Controller`), for
  example `ITmdbClientManager` for the handful of read methods plugins need plus an accessor for the
  configured key/image base, implemented by the existing `TmdbClientManager` in
  `MediaBrowser.Providers`. Plugins depend only on the interface. This is the cleanest contract and
  my preference.
- C. Expose only the shared key and image base (not the whole client) via a tiny published
  abstraction, leaving plugins to use TMDbLib themselves but with the host's key and a shared cache.
  A middle ground if exposing client methods is undesirable.

## Scope note

This is optional plumbing, not a feature. The gap-finder plugin ships standalone without it by
carrying its own TMDB client (see [../standard-plugin.md](../standard-plugin.md)); option B/C would
simply let it drop that and reuse the host's. The same surface would benefit any plugin that wants
TMDB data without re-implementing the client.
