# CLAUDE.md — Mind the Gaps

Guidance for Claude working in the **Jellyfin.Plugin.MindTheGaps** repo. Place this at the repo root.
(Marc may choose to gitignore it, as he did for the JustWatch plugin.)

## What this is

A Jellyfin plugin that finds what's **missing** and **related** across the library and builds a
dashboard "todo list" to fill the gaps: movies absent from a collection, episodes absent from a
series, films an owned actor/director made that aren't owned, and recommendations. It also has an
experimental, opt-in mode that mints greyed-out virtual placeholders in place.

It was formerly "CollectionGaps"; renamed because that collided with Jellyfin's Collection (BoxSet)
concept and undersold the scope. Plugin GUID `8c2a93cc-6cc5-493a-880a-2e67ae50e454` (keep it).

Read [README.md](README.md) for the feature tour and [docs/adr/](docs/adr/) for why things are the
way they are. The design rationale (virtual items, the SPI question) and the upstream asks live in
[docs/virtual-movies-analysis.md](docs/virtual-movies-analysis.md) and [docs/upstream/](docs/upstream/).

## START HERE: the standalone build switch

This code was developed inside a Jellyfin server checkout, so the `.csproj` files reference the
server projects directly:

```xml
<ProjectReference Include="..\MediaBrowser.Controller\MediaBrowser.Controller.csproj" Private="false" />
<ProjectReference Include="..\MediaBrowser.Model\MediaBrowser.Model.csproj" Private="false" />
<ProjectReference Include="..\MediaBrowser.Common\MediaBrowser.Common.csproj" Private="false" />
```

In this standalone repo those paths do not exist, so the **first task is to switch to the published
`Jellyfin.*` NuGet packages**, exactly like jellyfin-plugin-tvdb / jellyfin-plugin-tvmaze. The plan
and rationale are in [docs/standard-plugin.md](docs/standard-plugin.md). Target shape:

```xml
<PackageReference Include="Jellyfin.Controller" Version="10.*-*" />
<PackageReference Include="Jellyfin.Model" Version="10.*-*" />
<PackageReference Include="Jellyfin.Common" Version="10.*-*" />
<PackageReference Include="Microsoft.Extensions.Http" Version="9.*" />
<PackageReference Include="Microsoft.Extensions.Caching.Memory" Version="9.*" />
<PackageReference Include="TMDbLib" Version="3.0.0" />
```

Decision to make with Marc before doing it:

- **Target 10.11 (net9.0) now** — the only published ABI today. Lets the plugin build and install on a
  stock 10.11 server immediately. Risk: this code was written against the unreleased internal `12.0.0`
  ABI, so a few host APIs may differ on 10.11 and need adjusting. Watch especially: `ICollectionManager`
  (AddToCollectionAsync), `ILibraryManager.CreateItem`/`GetNewItemId`/`DeleteItem` overloads,
  `InternalItemsQuery` fields (`HasAnyProviderId`, `AncestorIds`, `IsVirtualItem`), and `BaseItemKind`.
- **Stay on a server checkout until a 12.x NuGet ships** — zero API risk, but not a standalone build.

The minimal-risk path: switch the csproj, build, and fix whatever 10.11 API deltas the compiler flags.
`build.yaml` also pins the ABI (`targetAbi: 12.0.0.0`, `framework: net10.0`) — bring both in line with
whichever ABI you target.

The test project (`Jellyfin.Plugin.MindTheGaps.Tests`) also uses project references plus `TMDbLib`
and `Newtonsoft.Json` via `VersionOverride`; move it to the same NuGet feed (a repo-level
`Directory.Packages.props` with the versions is cleanest, then drop the `VersionOverride`s).

## Build & test

```bash
dotnet build Jellyfin.Plugin.MindTheGaps/Jellyfin.Plugin.MindTheGaps.csproj
dotnet test  Jellyfin.Plugin.MindTheGaps.Tests/Jellyfin.Plugin.MindTheGaps.Tests.csproj
```

Currently 50 tests (49 pass + 1 Trakt skip). `build.yaml` is the plugin manifest (name "Mind the
Gaps", the GUID, `Jellyfin.Plugin.MindTheGaps.dll` artifact); `meta.json` is generated from it.

## Conventions (please honour)

- **No "AI wrote this" tells.** No em-dashes, en-dashes, arrows (-> / =>), or emoji anywhere in code,
  comments, or docs. Use plain punctuation. Marc actively checks for this. Match the surrounding
  comment density and tone; do not over-comment.
- **Terminology.** "Shows / Series / Seasons / Episodes" = episodic library content. "TV / Live TV" =
  the tuner/EPG feature, which this plugin has nothing to do with. Never write "TV" to mean episodic
  content. Use "Series" in code identifiers (matching the `Series` entity), "shows/series" in prose.
  Real refs like `MediaBrowser.Controller.Entities.TV` and the brands TVmaze/TheTVDB are fine.
  `CollectionGapSource`/`CollectionGapMapper` keep "Collection" on purpose (they handle TMDB
  collections).
- **Strict analysis.** StyleCop + .NET analyzers + `TreatWarningsAsErrors`, via `jellyfin.ruleset`,
  `AnalysisMode=AllEnabledByDefault`, nullable enabled. Gotchas hit during development: SA1210
  orders usings case-INSENSITIVELY; one type per file (SA1402); `ConfigureAwait(false)` everywhere
  (CA2007); a field holding `SemaphoreSlim`/`IDisposable` forces the class to be `IDisposable`
  (CA1001); public collection properties must be `IReadOnlyList`/`IReadOnlyDictionary` (CA2227/CA1002)
  or a get-only `Collection<T>` (which also dodges CA1819); the test project enforces analyzers too
  (CA1307 wants a `StringComparison` on `Assert.Contains`/`StartsWith`); use `string.Create(CultureInfo.InvariantCulture, $"...")` not raw interpolation (CA1305).
- **Don't commit or push** unless Marc explicitly asks. Leave work in the working tree for his review.

## Architecture

Gaps are **3 patterns across N media domains** (see ADR-0001):

- Patterns (`GapPattern`): `SetCompletion`, `CreatorWorks`, `Recommendation`.
- Domains (`MediaDomain`): `Video`, `Music`, `Book` (only Video implemented).
- `GapItem` is provider-agnostic (`ProviderIds` map + `ExternalLink[]`), tagged with pattern + domain.
- Ownership is one generic check: `OwnershipIndex.Owns/OwnsAny(kind, provider, id)`. Each `IGapSource`
  declares the `OwnedKinds` it diffs against; `GapEngine` unions them and indexes exactly those.

Pieces:

- Engine: `Gaps/{IGapSource, GapScanContext, OwnershipIndex, GapEngine, GapItemFactory, GapScanLimits,
  GapStore, SeriesGapKey}`. `ScheduledTasks/GapScanTask`. DI in `ServiceRegistrator`.
- Sources (`Gaps/Sources/`): `Tmdb/CollectionGapSource`, `Tmdb/PeopleGapSource`,
  `Tmdb/RecommendationsGapSource`, `Trakt/TraktFilmographyGapSource`, `Library/SeriesContentGapSource`,
  `Series/{SeriesContentGapSourceBase, TvMazeContentGapSource, TvdbContentGapSource}`. The three
  series-content sources share `SeriesGapKey.Episode(...)` ids so the engine de-dupes across them.
- Each source delegates parsing/mapping to a **pure public mapper** (`CollectionGapMapper`,
  `FilmographyGapMapper`, `RecommendationGapMapper`, `Series/{TvMazeMapper, TvdbMapper}`,
  `Trakt/TraktFilmographyMapper`, `Availability/TmdbWatchMapper`). These are what the tests exercise.
- TMDB access is the plugin's own `Services/Tmdb/TmdbClient.cs` over `TMDbLib` (no
  `MediaBrowser.Providers`): injected `IMemoryCache`, key via `TmdbClient.ResolveApiKey()` (config
  `TmdbApiKey`, default the public key), poster URLs against the image CDN.
- Hand-rolled HTTP clients (no SDKs): `Services/{Trakt/TraktClient, TvMaze/TvMazeClient, Tvdb/TvdbClient}`.
- Availability: `Services/Availability/{IAvailabilitySource, AvailabilityService, TmdbAvailabilitySource}`.
- API: `Api/GapsController` (route `MindTheGaps`, RequiresElevation). UI: `Web/mindthegaps.html`
  (dashboard), `Configuration/configPage.html` (settings).
- Experimental minting: `VirtualItems/VirtualMovieMinter` (see ADR-0004) — opt-in via config
  `MintPatterns` (a `Collection<GapPattern>`), only `SetCompletion` materializable today, tagged and
  fully reversible. Loudly temporary; belongs in core.

## Testing

Parsers/mappers are tested against **real captured API responses** under
`Jellyfin.Plugin.MindTheGaps.Tests/TestData/` (ADR-0006). TMDB fixtures deserialize through TMDbLib
(Newtonsoft) exactly as the runtime does. To refresh a fixture, re-run the documented capture (the
TVmaze/TheTVDB/TMDB captures use the public keys noted in the test headers and in
`docs/standard-plugin.md`). The **Trakt** test is `[Fact(Skip=...)]` because Trakt requires a private
client id; the exact `curl` to capture `trakt_person_movies.json` is in that test's header, after
which the Skip is removed.

The minter has no unit test (it mutates the library; same reason the live HTTP clients aren't unit-tested).

## Upstream (separate from this repo)

Three independent asks let the experience go fully native; the plugin works without them. Drafts in
[docs/upstream/](docs/upstream/):

- **PR A** (jellyfin-web): relax the Missing/Unaired indicator to non-episode virtual items. **Filed
  as jellyfin-web #8049.**
- **Discussion B** (server): a supported way to mint + reconcile virtual items for any type (preferred:
  a host `IVirtualItemManager`); includes a `DisplayMissingMovies`/`DisplayMissingItems` gate.
- **Discussion C** (server): expose the shared TMDB client/key via the published NuGet so plugins can
  reuse the host cache/key instead of carrying their own.

## Other context

- "Missing" vs "External" are orthogonal: a pathless item can be Missing (acquire), Upcoming
  (unreleased), or External (on a federated server, which reports `LocationType.Remote`, not Virtual).
  Don't infer Missing from Virtual alone.
- JustWatch is a separate plugin (Jellyfin.Plugin.JustWatch); this plugin only renders a "JustWatch"
  link from `ProviderIds["JustWatch"]` via `ProviderLinks`, with no code dependency.
