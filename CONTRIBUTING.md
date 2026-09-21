# Contributing to Mind the Gaps

Thanks for taking a look. This is the developer-facing guide: how to build, test, release, and find your
way around the code. For what the plugin does and how to use it, start at the [README](README.md).

## Build and test

```bash
dotnet build Jellyfin.Plugin.MindTheGaps.sln
dotnet test  Jellyfin.Plugin.MindTheGaps.sln
```

You need the .NET 10 SDK (12.0 ABI, the default) and, if you want to build the 10.11 ABI too, the .NET 9
SDK. No Jellyfin server checkout is required: the projects reference the published `Jellyfin.Controller` /
`Jellyfin.Model` / `Jellyfin.Common` NuGet packages, so the repo builds standalone.

Both projects build clean with StyleCop and the .NET analyzers running as **errors**
(`TreatWarningsAsErrors`), so a warning fails the build. Keep it green.

The dashboard JavaScript (`Jellyfin.Plugin.MindTheGaps/Web/`) has its own tooling, over Node:

```bash
npm ci
npm run lint          # oxlint
npm run test:ui:install   # once, fetches Chromium
npm run test:ui       # Playwright, see e2e/
```

`e2e/` drives the real `mindthegaps.report.js`/`.css` against a mocked `ApiClient`/`Dashboard`
(`e2e/support/build-harness.js`), wrapped in the same page div jellyfin-web itself uses (CSS
containment and all, see the next section), so a dashboard interaction bug shows up here instead of
only in a live install.

## How the build is put together

- **Standalone references.** The Jellyfin NuGet packages are referenced **compile-only**
  (`ExcludeAssets="runtime"`), so the host's own copies load at runtime and the plugin does not ship them.
- **Runtime dependencies do ship.** `TMDbLib` (and its `Newtonsoft.Json`) are genuine runtime dependencies
  the host does not provide, so they are listed in `build.yaml`'s `artifacts` and jprm bundles them into the
  zip alongside `Jellyfin.Plugin.MindTheGaps.dll`. If you add a runtime NuGet dependency, add its DLL to
  `artifacts` too, or the plugin fails to load with a `ReflectionTypeLoadException`.
- **`JellyfinVersion` drives both packages and framework.** `Directory.Packages.props` pins every package
  version from `$(JellyfinVersion)` (default `12.0`); the repo-root `Directory.Build.props` derives
  `TargetFramework` from the same property (major `10` -> `net9.0`, else `net10.0`), so a plain local
  `dotnet build` targets the 12.0 ABI with no flags, and `-p:JellyfinVersion=10.11.0` alone switches the
  whole solution to `net9.0`. `Directory.Build.props` also exists once per project
  (`Jellyfin.Plugin.MindTheGaps/` and `.Tests/`, both pulling `Version` from `build.yaml`); each explicitly
  imports the repo-root one first, since MSBuild only auto-imports the closest `Directory.Build.props` to a
  project, not every one on the way up.
- **Two ABIs today.** The [CI matrix](.github/workflows/build.yaml) builds `net9.0` / Jellyfin ABI
  `10.11.0.0` and `net10.0` / ABI `12.0.0.0`, passing `-p:JellyfinVersion=` (and, for the single-project
  `Build` step only, `-p:TargetFramework=` too). The solution-level `Test` step passes `JellyfinVersion`
  alone, deliberately never `-p:TargetFramework=`: MSBuild strips the literal `TargetFramework` global
  property when Tests' `ProjectReference` asks the plugin project for its compatible framework, which would
  silently revert it to `Directory.Build.props`'s default. `JellyfinVersion`, not being that property,
  survives the same query and re-derives the same framework on both sides. The `Package` step's `jprm plugin
build` runs its own internal `dotnet build` of the whole solution this same way, driven only by the
  `JellyfinVersion` environment variable it's given, for the same reason.

The full standalone build-and-package pattern is written up in
[this gist](https://gist.github.com/IDisposable/31b194e3f6dc5acbb0e08009b6c800bd), and the conventions this
repo follows are in [docs/standard-plugin.md](docs/standard-plugin.md).

## Releasing

Versioning is single-source from `build.yaml`.

1. Set `version` in `build.yaml` to the new version and update its `changelog`.
2. Push, and let CI go green.
3. Publish a GitHub Release with the matching tag (`v<version>`). Tick **"Set as a pre-release"** for a beta.

The `publish` workflow then packages the plugin with jprm, attaches the `.zip` to the release, and commits
the manifest update, which the catalog serves over its raw URL and clients pick up automatically.

### Two channels (stable and beta)

There are two catalog manifests: `manifest.json` (stable) and `manifest-beta.json` (beta). The release's
GitHub **pre-release** flag routes which gets the new version:

- **Stable release** (pre-release unticked): added to _both_ manifests.
- **Pre-release**: added to `manifest-beta.json` only.

So the beta channel is a superset (every release); the stable channel carries only stable releases.

**Version convention (important).** Plugin versions are 4-part numeric (`10.11.B.R`), and Jellyfin always
offers the highest version it sees, so the channels must stay strictly ordered. A **stable** release uses
revision `.0` (`10.11.1.0`); a **beta** uses a non-zero revision building toward the _next_ stable's `.0`
(betas `10.11.1.1`, `10.11.1.2`, ... lead to stable `10.11.2.0`). That way each stable supersedes the betas
before it, and each beta supersedes the last stable. CI guards both halves: the tag must match
`build.yaml`'s `version`, a stable release must be a `.0` revision, and a pre-release must not be.

## Project layout

```
Jellyfin.Plugin.MindTheGaps.sln         # solution (both projects)
build.yaml                              # plugin manifest (catalog metadata + changelog)
manifest.json                           # catalog/repository manifest, stable channel (served to clients)
manifest-beta.json                      # catalog/repository manifest, beta channel (every release)
Directory.Packages.props                # central NuGet versions (single source)
CONTRIBUTING.md                         # this file
assets/                                 # social card (social.png / social.svg)
docs/                                   # user guides, ADRs, design notes, screenshots
  configuration.md                      # every setting and its implications (user guide)
  report-guide.md                       # how to read and work the report (user guide)
  roadmap.md                            # status, backlog, and what is intentionally not built
  custom-css.md                         # targeting a provider's links from your own stylesheet
  adr/                                  # architecture decision records
  upstream/                             # drafts of the asks that would let this go fully native
  screenshots/                          # README and doc screenshots
Jellyfin.Plugin.MindTheGaps/            # the plugin
  Plugin.cs, ServiceRegistrator.cs, ProviderLinks.cs
  Gaps/                                 # engine + IGapSource + the gap sources
  Services/                             # TMDB, Trakt, TVmaze, TheTVDB, MusicBrainz, OpenLibrary, availability
  ScheduledTasks/                       # the gap scan and the availability refresh tasks
  Api/GapsController.cs                 # the dashboard's HTTP endpoints
  Web/                                  # two dashboard pages (the gaps report and the settings form),
                                        # each a shell + body + script over the shared css/common.js
  Configuration/PluginConfiguration.cs
  VirtualItems/VirtualItemMinter.cs     # temporary, opt-in
.editorconfig                           # code style + analyzer severities
Jellyfin.Plugin.MindTheGaps.Tests/      # xUnit tests + captured API fixtures
e2e/                                     # Playwright dashboard UI tests (npm run test:ui)
playwright.config.js
package.json                            # npm tooling: oxlint + Playwright
```

## Architecture in one paragraph

A gap is **one of three patterns across N media domains**: `SetCompletion`, `CreatorWorks`, or
`Recommendation` (the dashboard's three tabs), tagged with a domain (Movies, Shows, Music, Books). Each
gap source implements `IGapSource` and diffs an external catalog against an `OwnershipIndex` built from
your library; `GapEngine` runs the enabled sources, de-duplicates by a stable gap id, carries enrichment
forward across scans, and persists a versioned report. The TMDB, Trakt, TVmaze, TheTVDB, MusicBrainz, and
OpenLibrary clients are self-contained (no dependency on `MediaBrowser.Providers`), and all the hand-rolled
ones share `Services/Http/HttpRetry` for retry/backoff and a per-service circuit breaker. The reasoning
behind these choices lives in the [ADRs](docs/adr/); the current status and backlog are in the
[roadmap](docs/roadmap.md).

## Conventions

- **Tests for new behavior.** Parsers and mappers are pure and tested against **real captured API
  responses** under `Jellyfin.Plugin.MindTheGaps.Tests/TestData/` (see
  [ADR-0006](docs/adr/0006-captured-data-testing.md)). The live HTTP clients and the library-mutating minter
  are not unit-tested; everything they delegate to is. New interactive dashboard behavior (a popover, a
  filter, anything with its own click/hover wiring) gets a Playwright spec under `e2e/`, the JS/CSS
  equivalent of a mapper test.
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
