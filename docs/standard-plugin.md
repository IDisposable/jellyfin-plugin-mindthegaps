# Building as a standard, standalone plugin

Mind the Gaps builds as an ordinary Jellyfin plugin: it lives in its own repo, references the published
`Jellyfin.*` NuGet packages (not a server checkout), and installs on a stock server with no core changes,
exactly like jellyfin-plugin-tvdb and jellyfin-plugin-tvmaze.

It follows the standard standalone-plugin build and release pattern, compile-only host references
(`ExcludeAssets="runtime"`), single-source versioning from `build.yaml`, central package versions via
`$(JellyfinVersion)`, the multi-ABI CI matrix, and the `jprm` catalog publish, documented in this gist:
<https://gist.github.com/IDisposable/31b194e3f6dc5acbb0e08009b6c800bd>.

## The one decision that made it possible

A standalone NuGet build is only possible because the plugin does not reference `MediaBrowser.Providers`
(which is not published as a plugin-consumable package). It carries a self-contained TMDB client over the
public `TMDbLib` package instead, with its own `IMemoryCache` and a configurable key that defaults to the
well-known public Jellyfin TMDB key. Every other host type it uses ships in `Jellyfin.Controller` /
`Jellyfin.Model` / `Jellyfin.Common`. The rationale and trade-offs (losing the shared host TMDB cache and
the user's configured metadata key) are in
[adr/0003-self-contained-tmdb-client.md](adr/0003-self-contained-tmdb-client.md). The TVmaze, TheTVDB, and
Trakt clients are hand-rolled over `IHttpClientFactory`, so they need nothing from the host either.

## What still needs core

Everything above is a fully working plugin with no core changes: a scheduled scan plus a dashboard todo
list of missing and related content across collections, series (library plus TVmaze/TheTVDB cross-checks),
filmographies, recommendations, availability, and resolutions.

The one capability that cannot be done cleanly from a plugin is rendering missing items as native,
greyed-out virtual items for any type (the way missing episodes render inside a series): that needs a
creation-plus-reconciliation path in the server. It is optional polish layered on top of the plugin, not a
prerequisite, and is the only piece that belongs in core. Details in
[virtual-movies-analysis.md](virtual-movies-analysis.md), with the upstream drafts in [upstream/](upstream/).
