# Splitting the work for upstream

The gap-finding plugin can ship on its own, but a few capabilities are cleaner (or only possible) in
the server and web client. This folder holds the upstream asks so each can be merged on its own
schedule. The plugin does not depend on any of them; they are progressive enhancements.

## The pieces

| # | Where | Needs discussion? | What |
|---|---|---|---|
| A | jellyfin-web | No | Relax the "Missing" indicator so virtual items render in more contexts than just episodes. Filed as [jellyfin-web #8049](https://github.com/jellyfin/jellyfin-web/pull/8049). |
| B | jellyfin (server) | Yes | Mint virtual items for any type, not just show seasons/episodes, with reconciliation. See [discussion-mint-virtual-items.md](discussion-mint-virtual-items.md). |
| C | jellyfin (server) | Yes | Expose the TMDB client and key from the published NuGet so plugins reuse the shared cache and key. See [discussion-tmdb-nuget-surface.md](discussion-tmdb-nuget-surface.md). |
| Plugin | new repo | No | The gap-finder plugin itself, standalone. Build/release like jellyfin-plugin-justwatch. Path to standalone: [../standard-plugin.md](../standard-plugin.md). |

## Dependency order

- The plugin works today with none of A/B/C: it presents the gaps as a dashboard todo list.
- A is independently useful (it improves how any existing virtual item displays) but only shows
  missing movies once B exists to create them.
- B is the substantive feature: native greyed-out missing movies inside collections. A is its web
  half; the two ship together for the movie case but are separate PRs.
- C is pure plumbing cleanup: it lets the standalone plugin drop its own TMDB client and reuse the
  host's. The plugin ships fine without it (it carries its own client), so C is optional.

Background analysis for B (and the SPI note) lives in [../virtual-movies-analysis.md](../virtual-movies-analysis.md).
