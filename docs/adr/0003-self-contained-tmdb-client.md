# 3. Self-contained TMDB client (no MediaBrowser.Providers)

Status: Accepted.

## Context

The host's `TmdbClientManager` (a shared key and cache around TMDbLib) lives in
`MediaBrowser.Providers`, which is not published as a plugin-consumable NuGet. Depending on it forces a
build against a local server checkout and blocks shipping the plugin as a normal, standalone,
NuGet-built plugin.

## Decision

Use a small plugin-owned `TmdbClient` over the public `TMDbLib` package, with its own injected
`IMemoryCache`, an API key from config (defaulting to the well-known public Jellyfin key), and poster
URLs built against the stable image CDN base. The availability source reads the same configured key. The
plugin does not reference `MediaBrowser.Providers`.

## Consequences

- The plugin compiles against only published host assemblies plus `TMDbLib`, which is the thing that
  makes a standalone NuGet build possible (see `docs/standard-plugin.md`).
- It does not share the host's TMDB cache or the user's configured metadata key; it keeps its own cache
  and falls back to the public key. A user can set their own key for their own request budget.
- The gap mappers stay independent of this: they consume TMDbLib types, which the client returns directly.
