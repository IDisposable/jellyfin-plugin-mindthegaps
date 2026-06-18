<p align="center">
  <img src="assets/social.png" alt="Mind the Gaps Jellyfin Plugin" width="820">
</p>

<h1 align="center">Mind the Gaps</h1>

<p align="center">
Finds what's <b>missing</b> and what's <b>related</b> across your Jellyfin library and builds an easy
todo list for filling the gaps: movies absent from a collection, episodes absent from a series, films
your cast and crew made that you don't own, and related titles worth adding.
</p>

<p align="center">
<img alt="Build" src="https://img.shields.io/github/actions/workflow/status/IDisposable/jellyfin-plugin-mindthegaps/build.yaml?branch=main">
<img alt="License" src="https://img.shields.io/badge/license-MIT-blue">
<img alt="Jellyfin 10.11" src="https://img.shields.io/badge/Jellyfin-10.11-blueviolet">
</p>

## What it does

A scheduled task scans your library; a dashboard page (**Dashboard > Mind the Gaps**) shows the
results: filterable by pattern and domain, searchable, with TMDB/IMDb/etc. links and an on-demand
"Where to watch" for each item.

Gaps are modelled as three patterns across N media domains, so new sources slot in without engine
changes (only Video is implemented today):

| Pattern | Today (Video) | Future (Music / Book) |
|---|---|---|
| **SetCompletion** (a known member of an owned container is missing) | missing collection/franchise parts, missing series episodes | discography, book series |
| **CreatorWorks** (a work by an owned creator is missing) | actor/director filmography | discography, bibliography |
| **Recommendation** (related/discovery) | TMDB similar | n/a |

## Features

- **Collection gaps**: missing movies in a partially-owned TMDB collection or BoxSet. Movie-franchise
  only by design (TMDB collections don't model shows).
- **Filmography gaps (TMDB)**: an owned person's movie credits (cast plus directing/writing crew) that
  aren't in the library.
- **Filmography gaps (Trakt)**: an independent cross-check; opt-in (needs a Trakt client id).
- **Series content gaps**: surfaces the missing episodes Jellyfin already tracks, and (opt-in)
  cross-checks each owned series against **TVmaze** and **TheTVDB** to catch episodes the series'
  configured metadata provider doesn't list. TVmaze is keyless; TheTVDB needs your own v4 API key.
- **Recommendations**: TMDB "similar" titles; opt-in.
- **Where to watch**: streaming availability per item (TMDB watch/providers, officially licensed),
  looked up on demand or via a background "Look up where to watch" pass; never during the scan. For a
  missing episode it shows where to watch the show.
- **A usable report**: grouped by movies/shows and source, with alphabetical sub-grouping for creator
  works and recommendations; filter by type, specials, upcoming, streamable, or resolved; search; links
  to TMDB/IMDb/TheTVDB/JustWatch (extended by any external-link provider the host has, including the
  JustWatch plugin) plus an "open in Jellyfin" jump to items you already hold.
- **Resolve a gap** you don't consider missing (for example two listed episodes that are a single
  combined file): mark it with a note and it drops off the list, recoverable via a "Show resolved" filter.
- **Virtual placeholders** (experimental, opt-in): mint greyed-out "missing" placeholders in place,
  the way a missing episode renders inside a series. See below.

## Installation

### From the plugin catalog (recommended)

1. In the dashboard: **Plugins > Repositories > +**.
2. Add the repository (any name) with this URL:

   ```
   https://raw.githubusercontent.com/IDisposable/jellyfin-plugin-mindthegaps/main/manifest.json
   ```

3. Open the **Catalog** tab, find **Mind the Gaps** under *General*, and click **Install**.
4. Restart Jellyfin.

New releases show up in the catalog automatically.

### Manual

Download the `.zip` from the [latest release](https://github.com/IDisposable/jellyfin-plugin-mindthegaps/releases),
extract it into a folder under your server's `config/plugins/` directory (e.g.
`config/plugins/MindTheGaps/`), and restart Jellyfin.

Requires a server matching the plugin's `targetAbi` (currently `10.11.0.0`, `net9.0`).

## Usage

Open **Dashboard > Mind the Gaps** and click **Rescan now**. For collection gaps, your BoxSets need a
TMDB id (from the TMDB box-set provider). The scan also runs on a schedule (editable under
**Dashboard > Scheduled Tasks**).

## Configuration

In the dashboard, go to **Plugins > Mind the Gaps**. The settings page has per-source toggles plus:

| Setting | Description |
|---|---|
| Metadata country / language | Locale for TMDB lookups and availability. |
| Max related per item | Caps how many "similar" titles each owned item contributes. |
| Availability | Turns "Where to watch" on or off (the per-item lookups and the background pass). |
| Trakt client id | Enables the opt-in Trakt filmography cross-check. |
| TheTVDB API key | Your own v4 key; enables the TheTVDB series-content cross-check. |
| TMDB API key | Optional; falls back to the built-in public key. |

## Virtual placeholders (experimental, opt-in)

Off by default, the plugin can mint pathless "virtual" placeholder items so a gap renders greyed-out in
place. It is an experimental stand-in for proper server support: the server does not reconcile or
garbage-collect these, so the plugin does it itself, and everything minted is tagged and fully reversible.

Minting is driven from the report, one gap at a time: each movie row has a **Mint** button, and you can
checkbox several rows and **Mint selected**. Both run in the background with progress. A collection gap
mints into its BoxSet; anything else mints into a catch-all "Mind the Gaps (minted)" collection, and a
filmography gap also attaches the person so it shows on that person's page. Every minted item queues a
metadata refresh, and at the end of every scan a reconcile pass drops any minted movie the library now
owns for real. The settings page keeps only **Remove minted movies** (with a dry-run preview) to undo
everything at once. Missing episodes are not minted here: the server already synthesizes those.

## How it integrates

Mind the Gaps is self-contained. It carries its own TMDB client (over `TMDbLib`) and hand-rolled
Trakt/TVmaze/TheTVDB HTTP clients, so it has no dependency on `MediaBrowser.Providers` or on any other
plugin. External links are also extensible without a hard dependency: it merges in whatever the host's
own link providers emit, so a TMDB/IMDb link comes from core and a JustWatch link lights up if the
separate [Jellyfin.Plugin.JustWatch](https://github.com/IDisposable/jellyfin-plugin-justwatch) is
installed, with no code dependency between the two.

## Building

```bash
dotnet build Jellyfin.Plugin.MindTheGaps.sln
dotnet test  Jellyfin.Plugin.MindTheGaps.sln
```

The projects reference the published `Jellyfin.Controller` / `Jellyfin.Model` NuGet packages
(compile-only via `ExcludeAssets="runtime"`), so the repo builds standalone, no Jellyfin server
checkout required. The CI matrix in [.github/workflows/build.yaml](.github/workflows/build.yaml) builds
each supported ABI; a 12.x row is ready to enable once a 12.x Jellyfin NuGet ships. The full build and
release pattern is documented in
[this gist](https://gist.github.com/IDisposable/31b194e3f6dc5acbb0e08009b6c800bd).

### Releasing

1. Set `version` in `build.yaml` to the new version (e.g. `10.11.0.1`).
2. Publish a GitHub Release with the matching tag (`v10.11.0.1`).

The `publish` workflow packages the plugin, attaches the `.zip` to the release, and updates
`manifest.json`, which the catalog picks up automatically.

## Repository layout

```
Jellyfin.Plugin.MindTheGaps.sln         # solution (both projects)
build.yaml                              # plugin manifest (catalog metadata)
manifest.json                           # catalog/repository manifest
Directory.Packages.props                # central NuGet versions (single source)
LICENSE
assets/                                 # social card
docs/                                   # ADRs + upstream drafts + design notes
Jellyfin.Plugin.MindTheGaps/            # the plugin
  Plugin.cs, ServiceRegistrator.cs, ProviderLinks.cs
  Gaps/                                 # engine + IGapSource + sources
  Services/                             # TMDB, Trakt, TVmaze, TheTVDB, availability clients
  ScheduledTasks/GapScanTask.cs
  Api/GapsController.cs
  Web/mindthegaps.html                  # dashboard
  Configuration/PluginConfiguration.cs, configPage.html
  VirtualItems/VirtualMovieMinter.cs    # experimental, opt-in
.editorconfig                           # code style + analyzer severities
Jellyfin.Plugin.MindTheGaps.Tests/      # xUnit tests + captured API fixtures
```

## Contributing

Build and tests must stay green; StyleCop and the analyzers run as errors. Add tests for new behavior.
Parsers and mappers are tested against real captured API responses under
`Jellyfin.Plugin.MindTheGaps.Tests/TestData/`.

## License

MIT. See [LICENSE](LICENSE).
