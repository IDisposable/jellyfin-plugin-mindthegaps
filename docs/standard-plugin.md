# Making MindTheGaps a standard, installable plugin

This is the plan for turning MindTheGaps into an ordinary Jellyfin plugin: one that lives in its
own repo, builds against the published `Jellyfin.*` NuGet packages (like jellyfin-plugin-tvdb and
jellyfin-plugin-tvmaze), and installs on a stock server with no core changes. The one capability
that genuinely needs core support (minting native virtual movie items) is called out separately and
can stay a core feature without holding the rest back.

## Where it stands today

The plugin only ever calls public host APIs: `ILibraryManager`, `IScheduledTask`, `IHasWebPages`,
`IPluginServiceRegistrator`, `IHttpClientFactory`, the controller/auth attributes, and the domain
types in `MediaBrowser.Controller`/`Model`/`Common`. All of those ship in the published Jellyfin
NuGet packages.

The reason it currently builds with `ProjectReference`s to a local server checkout (rather than
NuGet) is twofold:

1. The server in this checkout is the unreleased internal `12.0.0`, and there is no `12.x` Jellyfin
   NuGet yet. A standard plugin references `Jellyfin.Controller`/`Jellyfin.Model`/etc. at a published
   version (today `10.*-*`, net9.0). This is a target-version choice, not an architecture problem.
2. It references `MediaBrowser.Providers` for TMDB (`TmdbClientManager`, `TmdbUtils.ApiKey`).
   `MediaBrowser.Providers` is not published as a plugin-consumable NuGet, so depending on it is what
   actually prevents a standalone NuGet build.

So the only architectural blocker is the `MediaBrowser.Providers` dependency. Remove that and the
project references collapse to published packages plus `TMDbLib` (which is a public NuGet we already
use).

## The exact non-standard references

| Reference | Used by | Status |
|---|---|---|
| `MediaBrowser.Providers` -> `TmdbClientManager` | CollectionGapSource, PeopleGapSource, RecommendationsGapSource | Replace |
| `MediaBrowser.Providers` -> `TmdbUtils.ApiKey` | TmdbAvailabilitySource | Replace |
| `Jellyfin.Database.Implementations` | nothing (no `JellyfinDbContext`/`DbContext` usage) | Dead, remove now |
| `MediaBrowser.Controller` / `Model` / `Common` | throughout | Keep (published as `Jellyfin.Controller`/`Model`/`Common`) |

The `Jellyfin.Database.Implementations` project reference has zero usages and can be deleted today
with no other change.

`TmdbClientManager` is called in exactly five ways:

- `GetCollectionAsync` (CollectionGapSource)
- `GetPersonAsync` with movie credits (PeopleGapSource)
- `GetMovieSimilarPageAsync` / `GetSeriesSimilarPageAsync` (RecommendationsGapSource)
- `GetPosterUrl` (all three, to turn a poster path into a URL)

`TmdbUtils` is referenced for one thing in code: the embedded `ApiKey` constant in the availability
source. (FilmographyGapMapper mentions `TmdbUtils.MapCrewToPersonType` in a comment only; the code
filters by department itself and does not call it.)

## The plan: a self-contained TMDB client

`TmdbClientManager` is a thin wrapper that the host uses to share an API key and an in-memory cache
around `TMDbLib`. `TMDbLib` itself is a public NuGet. So the replacement is a small plugin-owned
client that uses `TMDbLib` directly:

- `new TMDbClient(apiKey)` for the API surface we need (collection, person+credits, similar movies,
  similar series).
- A plugin-owned cache (`Microsoft.Extensions.Caching.Memory`, a standard package) keyed by request,
  matching what `TmdbClientManager` did for us.
- An API key from plugin configuration, defaulting to the well-known public Jellyfin TMDB key value
  (it is a constant in the open-source server, so copying the literal is fine), with the option for a
  user to supply their own.
- Poster URLs built against the stable image CDN base (`https://image.tmdb.org/t/p/<size>`), or by
  fetching `/configuration` once and caching `images.base_url` if we want to track TMDB exactly.

Each of the five calls maps one-to-one onto a `TMDbClient` method, so the gap mappers (already
extracted and unit-tested against captured responses) do not change. Only the source constructors
swap `TmdbClientManager` for the new client.

The availability source already hand-rolls its own HTTP and JSON; it only needs the API key value,
so it reads the same configured key instead of `TmdbUtils.ApiKey`.

### Tradeoffs

- Cost: we no longer share the host's TMDB cache or the key the user configured for the TMDB metadata
  provider. The plugin keeps its own cache and key.
- Benefit: the plugin builds against published NuGets only, so it can live in its own repo and install
  on a stock server, exactly like the tvdb/tvmaze plugins.
- Mitigation: defaulting the key to the public value means it works out of the box; power users can
  set their own key (and their own request budget) in config.

## Resulting project shape

After decoupling, the csproj mirrors the reference plugins:

```xml
<PackageReference Include="Jellyfin.Controller" Version="10.*-*" />
<PackageReference Include="Jellyfin.Data" Version="10.*-*" />
<PackageReference Include="Jellyfin.Model" Version="10.*-*" />
<PackageReference Include="Jellyfin.Common" Version="10.*-*" />
<PackageReference Include="Microsoft.Extensions.Http" Version="9.*" />
<PackageReference Include="Microsoft.Extensions.Caching.Memory" Version="9.*" />
<PackageReference Include="TMDbLib" Version="3.0.0" />
```

No `ProjectReference`, no local server checkout. The TVmaze and TheTVDB clients are already
hand-rolled with `IHttpClientFactory`, so they need nothing.

## ABI reality

A standard plugin targets a published ABI. Today that is `10.11.x` (net9.0). Building against
`10.*-*` produces a plugin that installs on a 10.11 server. Running a 10.11-built plugin on the
unreleased 12.x server is ABI-risky (the surface this plugin touches is small, but it must be
verified). When a `12.x` Jellyfin NuGet ships, bump the package versions and the plugin is standard
on 12.x with no other change. The net9.0-vs-net10.0 target follows whatever the chosen NuGet ABI
requires.

The decoupling from `MediaBrowser.Providers` is worth doing regardless of which ABI we target,
because it is the thing that makes a NuGet-only build possible at all.

## What stays in core

Everything described above leaves a fully working plugin: a scheduled scan plus a dashboard todo
list of missing and related content across collections, series (library plus TVmaze/TheTVDB
cross-checks), filmographies, recommendations, and availability. None of that needs core changes.

The one capability that cannot be done cleanly from a plugin is rendering missing movies as native,
greyed-out virtual items inside a BoxSet (the way missing episodes render inside a series). That
needs a creation path plus reconciliation in the server, detailed in
[virtual-movies-analysis.md](virtual-movies-analysis.md). It is optional polish layered on top of the
plugin, not a prerequisite, and it is the only piece that belongs in core.

## Related: the gap-source SPI

If the goal ever extends to letting independent plugins contribute gap sources (a TMDB gap plugin, a
Trakt gap plugin, and so on, the way metadata providers work), the `IGapSource` contract would have
to move into a host assembly. Jellyfin has no plugin-to-plugin SPI, so today every gap source must
ship inside this one plugin. That is also covered in
[virtual-movies-analysis.md](virtual-movies-analysis.md). It is independent of the standalone-plugin
work here: this plan makes the whole thing one standard plugin; that one would make the sources
separately pluggable.

## Suggested order of work

1. Delete the dead `Jellyfin.Database.Implementations` reference (no code change). DONE.
2. Add the self-contained `TmdbClient` (TMDbLib + `IMemoryCache` + configured key defaulting to the
   public value, poster URLs built against the stable image CDN base). DONE
   (`Services/Tmdb/TmdbClient.cs`).
3. Point the three TMDB sources and the availability source at the new client/key; delete the
   `MediaBrowser.Providers` usings and the project reference. DONE. The plugin now references only
   `MediaBrowser.Controller`/`Model`/`Common` plus `TMDbLib`, and builds with no `MediaBrowser.Providers`
   dependency. An optional "TMDB API key" config field was added (blank uses the default key).
4. REMAINING: switch the csproj from the local project references to the `Jellyfin.*` NuGet packages
   and split the plugin into its own repo (the tests, captured fixtures, and CI come along unchanged).
   This is the only step gated on a published ABI.
5. Leave virtual-movie minting as a separate core proposal (Discussion B).
