# Contributing to Mind the Gaps

Thanks for taking a look. This is the developer-facing guide: how to build, test, release, and find your
way around the code. For what the plugin does and how to use it, start at the [README](README.md).

## Build and test

```bash
dotnet build Jellyfin.Plugin.MindTheGaps.sln
dotnet test  Jellyfin.Plugin.MindTheGaps.sln
```

You need the .NET 9 SDK. No Jellyfin server checkout is required: the projects reference the published
`Jellyfin.Controller` / `Jellyfin.Model` / `Jellyfin.Common` NuGet packages, so the repo builds standalone.

Both projects build clean with StyleCop and the .NET analyzers running as **errors**
(`TreatWarningsAsErrors`), so a warning fails the build. Keep it green.

## How the build is put together

- **Standalone references.** The Jellyfin NuGet packages are referenced **compile-only**
  (`ExcludeAssets="runtime"`), so the host's own copies load at runtime and the plugin does not ship them.
- **Runtime dependencies do ship.** `TMDbLib` (and its `Newtonsoft.Json`) are genuine runtime dependencies
  the host does not provide, so they are listed in `build.yaml`'s `artifacts` and jprm bundles them into the
  zip alongside `Jellyfin.Plugin.MindTheGaps.dll`. If you add a runtime NuGet dependency, add its DLL to
  `artifacts` too, or the plugin fails to load with a `ReflectionTypeLoadException`.
- **Versions are centralised.** `Directory.Packages.props` pins every package version from a single
  `$(JellyfinVersion)` property, so the CI matrix can build each ABI by passing `-p:JellyfinVersion=`.
- **One ABI today.** The plugin targets `net9.0` / Jellyfin ABI `10.11.0.0` (the only published Jellyfin
  NuGet ABI). The [CI matrix](.github/workflows/build.yaml) has a `12.x` row ready to uncomment once a 12.x
  NuGet ships. Keep the `.csproj` `TargetFramework` and `build.yaml`'s `targetAbi` in step.

The full standalone build-and-package pattern is written up in
[this gist](https://gist.github.com/IDisposable/31b194e3f6dc5acbb0e08009b6c800bd), and the conventions this
repo follows are in [docs/standard-plugin.md](docs/standard-plugin.md).

## Releasing

Versioning is single-source from `build.yaml`.

1. Set `version` in `build.yaml` to the new version (for example `10.11.0.16`) and update its `changelog`.
2. Push, and let CI go green.
3. Publish a GitHub Release with the matching tag (`v10.11.0.16`).

The `publish` workflow then packages the plugin with jprm, attaches the `.zip` to the release, and commits
the `manifest.json` update, which the catalog serves over its raw URL and clients pick up automatically. A
CI guard fails the release if the tag and `build.yaml`'s `version` disagree.

## Project layout

```
Jellyfin.Plugin.MindTheGaps.sln         # solution (both projects)
build.yaml                              # plugin manifest (catalog metadata + changelog)
manifest.json                           # catalog/repository manifest (served to clients)
Directory.Packages.props                # central NuGet versions (single source)
CONTRIBUTING.md                         # this file
assets/                                 # social card (social.png / social.svg)
docs/                                   # user guides, ADRs, design notes, screenshots
  configuration.md                      # every setting and its implications (user guide)
  report-guide.md                       # how to read and work the report (user guide)
  roadmap.md                            # status, backlog, and what is intentionally not built
  adr/                                  # architecture decision records
  upstream/                             # drafts of the asks that would let this go fully native
  screenshots/                          # README and doc screenshots
Jellyfin.Plugin.MindTheGaps/            # the plugin
  Plugin.cs, ServiceRegistrator.cs, ProviderLinks.cs
  Gaps/                                 # engine + IGapSource + the gap sources
  Services/                             # TMDB, Trakt, TVmaze, TheTVDB, MusicBrainz, OpenLibrary, availability
  ScheduledTasks/                       # the gap scan and the availability refresh tasks
  Api/GapsController.cs                 # the dashboard's HTTP endpoints
  Web/mindthegaps.html                  # the dashboard (the gear toggles an inline settings panel)
  Configuration/PluginConfiguration.cs
  VirtualItems/VirtualMovieMinter.cs    # experimental, opt-in
.editorconfig                           # code style + analyzer severities
Jellyfin.Plugin.MindTheGaps.Tests/      # xUnit tests + captured API fixtures
```

## Architecture in one paragraph

A gap is **one of three patterns across N media domains**: `SetCompletion`, `CreatorWorks`, or
`Recommendation` (the dashboard's three tabs), tagged with a domain (Movies, Shows, Music, Books). Each
gap source implements `IGapSource` and diffs an external catalogue against an `OwnershipIndex` built from
your library; `GapEngine` runs the enabled sources, de-duplicates by a stable gap id, carries enrichment
forward across scans, and persists a versioned report. The TMDB, Trakt, TVmaze, TheTVDB, MusicBrainz, and
OpenLibrary clients are self-contained (no dependency on `MediaBrowser.Providers`), and all the hand-rolled
ones share `Services/Http/HttpRetry` for retry/backoff and a per-service circuit breaker. The reasoning
behind these choices lives in the [ADRs](docs/adr/); the current status and backlog are in the
[roadmap](docs/roadmap.md).

## Conventions

- **Tests for new behaviour.** Parsers and mappers are pure and tested against **real captured API
  responses** under `Jellyfin.Plugin.MindTheGaps.Tests/TestData/` (see
  [ADR-0006](docs/adr/0006-captured-data-testing.md)). The live HTTP clients and the library-mutating minter
  are not unit-tested; everything they delegate to is.
- **Analyzers are errors.** StyleCop + .NET analyzers via `.editorconfig` with
  `AnalysisMode=AllEnabledByDefault`, nullable enabled, and `ConfigureAwait(false)` on every await.
- **Terminology.** "Shows / Series / Seasons / Episodes" is episodic content; "Live TV" is the unrelated
  tuner feature. Use "Series" in code identifiers, "shows/series" in prose.
- **Plain prose.** No em-dashes, en-dashes, arrows, or emoji in code, comments, or docs.
- Open a pull request against `main`, and keep each commit focused.

## Further reading

- [Architecture decision records](docs/adr/) and their [index](docs/adr/README.md)
- [Roadmap and status](docs/roadmap.md)
- [Upstream asks](docs/upstream/) that would let the experience go fully native
- [Virtual items analysis](docs/virtual-movies-analysis.md)
