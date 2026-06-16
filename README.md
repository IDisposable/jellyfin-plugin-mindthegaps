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
- **Where to watch**: streaming availability aggregated per item, fetched lazily (never in bulk during
  a scan). Ships with TMDB watch/providers (officially licensed).
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
| Max recommendations per item | Caps how many "similar" titles each owned item contributes. |
| Availability | Turns the on-demand "Where to watch" lookups on or off. |
| Trakt client id | Enables the opt-in Trakt filmography cross-check. |
| TheTVDB API key | Your own v4 key; enables the TheTVDB series-content cross-check. |
| TMDB API key | Optional; falls back to the built-in public key. |
| Mint patterns | Which gap patterns to materialise as virtual placeholders (experimental). |

## Virtual placeholders (experimental, opt-in)

Off by default, the plugin can mint pathless "virtual" placeholder items so a gap renders greyed-out
in place. Today only the **SetCompletion** pattern is materialisable (missing collection movies into a
BoxSet); CreatorWorks and Recommendation have no container to render into yet. This is a deliberately
temporary stand-in for proper server support: the server doesn't reconcile or garbage-collect these,
so the plugin does it itself, and everything minted is tagged and fully removable from the settings
page. Pick the patterns to mint, then use the **Mint now** / **Remove minted movies** buttons; the
**Preview** buttons do a dry run that logs what would happen without writing. The dashboard also has a
per-row **Mint** button (debug) to materialise a single gap; a filmography row mints into a catch-all
collection and attaches the person, so it shows on that person's page.

## How it integrates

Mind the Gaps is self-contained. It carries its own TMDB client (over `TMDbLib`) and hand-rolled
Trakt/TVmaze/TheTVDB HTTP clients, so it has no dependency on `MediaBrowser.Providers` or on any other
plugin. It only *renders* a JustWatch link when an item already has `ProviderIds["JustWatch"]` (set by
the separate [Jellyfin.Plugin.JustWatch](https://github.com/IDisposable/jellyfin-plugin-justwatch)),
with no code dependency between the two.

## Building

```bash
dotnet build Jellyfin.Plugin.MindTheGaps.sln
dotnet test  Jellyfin.Plugin.MindTheGaps.sln
```

The projects reference the published `Jellyfin.Controller` / `Jellyfin.Model` NuGet packages
(compile-only via `ExcludeAssets="runtime"`), so the repo builds standalone, no Jellyfin server
checkout required. The CI matrix in [.github/workflows/build.yaml](.github/workflows/build.yaml) builds
each supported ABI; 12.x is dormant until that NuGet ships.

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
  jellyfin.ruleset
Jellyfin.Plugin.MindTheGaps.Tests/      # xUnit tests + captured API fixtures
```

## Contributing

Build and tests must stay green; StyleCop and the analyzers run as errors. Add tests for new behavior.
Parsers and mappers are tested against real captured API responses under
`Jellyfin.Plugin.MindTheGaps.Tests/TestData/`.

## License

MIT. See [LICENSE](LICENSE).
