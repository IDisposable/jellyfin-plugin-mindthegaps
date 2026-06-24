# 14. A TheMovieDb episode source, and honor the library's provider priority

Status: Accepted; the provider-priority deferral is superseded by [ADR-0015](0015-merge-episodes-per-season-library-driven.md), which merges providers per season instead of deferring a whole series. The TheMovieDb episode source itself stands (now `TmdbEpisodeProvider`).

## Context

The series-content cross-checks (TVmaze, TheTVDB) diff their own episode numbering against the library, but
a library numbers its episodes by whatever metadata provider it prioritises (TheMovieDb for many). A
provider that numbers a season differently then disagrees with the library on every renumbered or merged
episode. Air-date and title reconciliation (ADR-0011) recovers most of those, but two gaps remained: there
was no cross-check against the user's own authoritative provider when that provider is TheMovieDb, and a
non-authoritative provider could still list episodes the authority does not consider real.

## Decision

Two parts. Add `TmdbContentGapSource`, an opt-in series-content cross-check that diffs a series against
TheMovieDb's own season and episode list (`TmdbClient.GetSeriesEpisodesAsync`, cached), so a
TheMovieDb-led library is checked against its own numbering. And honor the library's metadata fetcher
order: `SeriesContentPriority` lets a lower-ranked cross-check defer a series to a higher-ranked provider
when that provider's cross-check is enabled and the series carries its id, so the user's chosen authority
owns the episode list. TVmaze, not a Jellyfin metadata fetcher, ranks below the others.

## Consequences

- A TheMovieDb-prioritised library can be cross-checked against TheMovieDb directly, which diffs cleanly
  against its own numbering instead of fighting a provider that numbers differently.
- Deferral only steps a source aside in favor of one the user ranked higher and enabled, so it never hides
  a gap the authority would also report; it trades a lower provider's extra (often spurious) episodes for
  the authority's view, which is the point.
- It costs a per-series read of the library's fetcher order, and TheMovieDb's list is one call for the show
  plus one per season, bounded by the same stalest-first rotation and read-through cache as the other
  cross-checks.
