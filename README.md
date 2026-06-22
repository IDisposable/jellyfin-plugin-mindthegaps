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
results: filterable by tab and media type, searchable, with links out (TMDB, IMDb, and more) and an
on-demand "Where to watch" for each item.

<p align="center">
  <a href="docs/screenshots/report-movies-set-completion.png"><img src="docs/screenshots/report-movies-set-completion.png" alt="The Mind the Gaps report: movie collections laid out in columns with their missing parts" width="860"></a>
</p>

More screenshots throughout the [report guide](docs/report-guide.md) and the
[configuration reference](docs/configuration.md).

Every gap is one of three kinds, surfaced as the report's three tabs:

| Tab | What it finds | Examples |
|---|---|---|
| **Set completion** | a missing piece of something you partly own | a movie missing from a collection or franchise; a missing season or episode; a music artist's missing albums |
| **Creator works** | other work by a person or artist you own | a film an owned actor or director made; a music artist's wider catalogue; an author's other books |
| **Recommendations** | related titles worth adding (discovery; off by default) | TMDB "similar" titles for things you own |

Movies and shows work out of the box; music and books are opt-in sources.

## Features

- **Collection gaps**: missing movies in a partially-owned TMDB collection or BoxSet. Movie-franchise
  only by design (TMDB collections don't model shows).
- **Filmography gaps (TMDB)**: an owned actor or director's films that aren't in your library. A big cast
  and crew is covered a bit at a time across repeated scans, so the list builds up rather than arriving all
  at once. A relevance filter (a minimum-votes threshold, plus an optional cast-billing limit) drops obscure
  and bit-part credits so the list stays useful on a large library.
- **Filmography gaps (Trakt)**: an independent cross-check; opt-in (needs a Trakt client id).
- **Series content gaps**: surfaces the missing episodes Jellyfin already tracks, and (opt-in)
  cross-checks each owned series against **TVmaze** and **TheTVDB** to catch episodes the series'
  configured metadata provider doesn't list. TVmaze is keyless; TheTVDB needs your own v4 API key.
- **Curated sets (studio / keyword / label)**: complete the movies of a studio ("every A24 film", "every
  Studio Ghibli film") or a TMDB keyword, beyond what a formal BoxSet covers, plus a record label's releases
  via Discogs. Opt-in: pick studios, keywords, and labels with a type-ahead chip picker
  (search, pick a match, it becomes a removable chip), no id-hunting required.
- **Music and books (off by default)**: complete an album artist's discography and discover a
  track-only artist's wider catalogue (MusicBrainz, with Discogs covering artists MusicBrainz cannot resolve),
  and surface other books in an owned author's bibliography (OpenLibrary). Some need a key or token.
- **Recommendations**: TMDB "similar" titles; opt-in. Each result lists every owned title that recommends
  it, not just the first; a TMDB vote floor trims the obscure long tail.
- **Where to watch**: streaming availability per item (TMDB watch/providers, officially licensed),
  looked up on demand or via a background "Look up where to watch" pass; never during the scan. For a
  missing episode it shows where to watch the show.
- **A usable report**: grouped by movies/shows and source, with an A-Z jump bar for the creator-works and
  recommendation tabs and a coverage badge ("6 of 9 owned, 67%") on collection groups. Each tab loads on
  demand, and Set completion lays its collapsed series and collections out in responsive columns so a big
  library is not one very tall list. Filter by type, specials, upcoming, streamable, or dismissed; search
  (matches the creator/source too); save named view presets or copy a shareable link to the exact view;
  export the current view to Markdown. Links to TMDB/IMDb/TheTVDB/JustWatch (extended by any external-link
  provider the host has, including the JustWatch plugin), an "open in Jellyfin" jump to items you already
  hold, and a search icon that opens a scoped Jellyfin search for any title, series, collection, or creator.
- **Diagnose why something is "missing"**: a per-gap popup explains the verdict, laying the gap beside the
  owned items that look like it (owned under the wrong id, an owned item already holds this id, a same-named
  reboot like V 1984 versus V 2009, or genuinely missing). A "Deeper analysis" confirms against the source
  provider, and a library-wide identification audit runs the same check across everything and downloads as
  Markdown.
- **Batch and whole-set dismissals**: resolve or mark "not interested" every episode under a series or
  season at once, or dismiss a whole creator or recommendation source so it stops being scanned.
- **Dismiss a gap**: mark it **resolved** (not really missing, for example two listed episodes that are a
  single combined file), **not interested** (a real gap you do not want), or **snooze until release** (an
  upcoming title, which resurfaces on its own once released). Dismissed gaps drop off the list, recoverable
  via a "Show dismissed" filter.
- **Webhook**: optionally post a summary to a webhook URL (Discord-compatible, carries the server name)
  when a scan or the "where to watch" pass finishes.
- **Virtual placeholders** (opt-in): mint greyed-out "missing" placeholders in place,
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

### Beta channel (optional)

To get pre-release builds before they reach the stable channel, add this repository URL instead of the one
above:

```
https://raw.githubusercontent.com/IDisposable/jellyfin-plugin-mindthegaps/main/manifest-beta.json
```

The beta channel carries every release (stable and pre-release); the stable channel carries only stable
releases. Both publish the same plugin, so Jellyfin always offers the highest version it sees and a stable
release supersedes the betas that led up to it. Use one channel or the other, not both.

### Manual

Download the `.zip` from the [latest release](https://github.com/IDisposable/jellyfin-plugin-mindthegaps/releases),
extract it into a folder under your server's `config/plugins/` directory (e.g.
`config/plugins/MindTheGaps/`), and restart Jellyfin.

Requires a server matching the plugin's `targetAbi` (currently `10.11.0.0`, `net9.0`).

## Usage

Open **Dashboard > Mind the Gaps** and click **Rescan now**. For collection gaps, your BoxSets need a
TMDB id (from the TMDB box-set provider). The scan also runs on a schedule (editable under
**Dashboard > Scheduled Tasks**).

See the [report guide](docs/report-guide.md) for the three pattern tabs (Set completion, Creator works,
Recommendations), the filters and saved views, and the per-row actions (where to watch, mint, dismiss).

## Configuration

In the dashboard, go to **Plugins > Mind the Gaps**. For every setting, what it does, and what changes
when you set or clear it, see the [configuration reference](docs/configuration.md). In brief, the
settings page has per-source toggles plus:

| Setting | Description |
|---|---|
| Metadata country / language | Locale for TMDB lookups and availability. |
| Max related per item | Caps how many "similar" titles each owned item contributes. |
| Max creators scanned per run | Caps the filmography scan; people are scanned stalest-first, so coverage accumulates over runs and a higher cap covers a large cast and crew faster. |
| Relevance floors | Minimum TMDB votes for filmography and recommendation gaps (plus an optional cast-billing limit), so Creator works and Recommendations stay actionable on a large library. |
| Curated studio / keyword ids | Comma-separated TMDB company and keyword ids to complete (for example 41077 for A24). |
| Availability | Turns "Where to watch" on or off (the per-item lookups and the background pass). |
| Webhook URL | Optional; posted to (Discord-compatible) when a scan or the "where to watch" pass finishes. |
| Trakt client id | Enables the opt-in Trakt filmography cross-check. |
| TheTVDB API key | Your own v4 key; enables the TheTVDB series-content cross-check. |
| TMDB API key | Optional; falls back to the built-in public key. |

## Virtual placeholders (opt-in)

Off by default, the plugin can mint pathless "virtual" placeholder items so a gap renders greyed-out in
place. It is a stand-in for proper server support: the server does not reconcile or
garbage-collect these, so the plugin does it itself, and everything minted is tagged and fully reversible.

Minting is driven from the report, one gap at a time: each movie row has a **Mint** button, and you can
checkbox several rows and **Mint selected**. Both run in the background with progress. A collection gap
mints into its BoxSet; anything else mints into a catch-all "Mind the Gaps (minted)" collection, and a
filmography gap also attaches the person so it shows on that person's page. Every minted item queues a
metadata refresh, and at the end of every scan a reconcile pass drops any minted movie the library now
owns for real. The settings page keeps only **Remove minted movies** (with a dry-run preview) to undo
everything at once. Missing episodes are not minted here: the server already synthesizes those.

## Works alongside your other plugins

Mind the Gaps is self-contained, so it does not depend on any other plugin and will not clash with them.
Its links are still extensible without any setup: it folds in whatever your server's own link providers
emit, so TMDB and IMDb links come from core, and a **JustWatch** link lights up automatically if the
separate [Jellyfin.Plugin.JustWatch](https://github.com/IDisposable/jellyfin-plugin-justwatch) is
installed. For the architecture behind this, see [CONTRIBUTING](CONTRIBUTING.md).

## Advanced CSS customization

Every outbound link in the report (the per-row provider links, the links on a creator or set group
header, and the ids in the Diagnose popup) is tagged so a stylesheet can target a specific service, for
example to inject a service icon. Each link carries:

- a per-provider class, `cgProvider-<service>`, with the service name lowercased and stripped to letters
  and digits: `cgProvider-tmdb`, `cgProvider-imdb`, `cgProvider-thetvdb`, `cgProvider-tvmaze`,
  `cgProvider-trakt`, `cgProvider-musicbrainz`, `cgProvider-discogs`, `cgProvider-openlibrary`,
  `cgProvider-justwatch`;
- a `data-provider` attribute with the original service name (`data-provider="TheTVDB"`), for attribute
  selectors.

Drop CSS into your server via the community
[Custom CSS](https://github.com/sealednut/jellyfin-custom-css) or
[File Transformation](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation) plugins (this
plugin ships no CSS itself). For example, to put an icon before each Discogs and MusicBrainz link:

```css
.cgProvider-discogs::before,
.cgProvider-musicbrainz::before {
  content: "";
  display: inline-block;
  width: 1em;
  height: 1em;
  margin-right: .3em;
  vertical-align: text-bottom;
  background: center / contain no-repeat;
}
.cgProvider-discogs::before     { background-image: url("/path/to/discogs.svg"); }
.cgProvider-musicbrainz::before { background-image: url("/path/to/musicbrainz.svg"); }
```

These class and attribute names are a stable contract; the link text and layout around them are not.

## Documentation

- **[Configuration reference](docs/configuration.md)** - every setting, what it does, and what changes
  when you set or clear it.
- **[Report guide](docs/report-guide.md)** - the three tabs, the filters and saved views, and the per-row
  actions.
- **[Roadmap and status](docs/roadmap.md)** - what is built, what is planned, and what is deliberately not.

## Contributing

Bug reports, ideas, and pull requests are welcome. See **[CONTRIBUTING](CONTRIBUTING.md)** for how to
build, test, release, and find your way around the code.

## License

MIT. See [LICENSE](LICENSE).
