# 15. Merge every provider's episodes per season; the library drives which run

Status: Accepted. Supersedes the provider-priority deferral of [ADR-0014](0014-tmdb-episode-source-and-provider-priority.md).

## Context

ADR-0014 honored the library's fetcher order by letting a lower-ranked cross-check defer a whole series to a
higher-ranked provider. That was too coarse in both directions: it discarded seasons a lower provider lists
that the authority never did (a later season TheMovieDb has not added yet), and it still let the library's
own virtual (missing) episodes contradict the authority, because the library reader ran as its own
independent source rather than under the priority. Enablement was also configured in two places: the
library's metadata fetchers, and a separate per-provider toggle in the plugin.

## Decision

One orchestrator (`SeriesContentGapSource`) owns series completeness. For each series it asks every reachable
provider (`ISeriesEpisodeProvider`: TheMovieDb, TheTVDB, TVmaze) for its canonical episode list, orders them
by the library's metadata fetcher order, and merges by season (`SeriesContentMerge`): the highest-ranked
provider that lists a season owns it, a lower provider may add a season none above it has, but may not
contradict a higher provider within a season it covers. The library's own virtual episodes are appended as
the lowest-ranked, last-chance list, so a fresher provider's opinion always wins; a reported episode the
server still tracks as a virtual item is linked to it.

Enablement is the library's, not the plugin's. A provider runs for a series when the library lists it as a
metadata fetcher, it has its credentials (TheTVDB's key; TheMovieDb and TVmaze are keyless), and the series
carries an id it resolves by. The per-provider toggles are gone. A library that configures no fetcher order
falls back to every credentialed provider, so it is still cross-checked.

## Consequences

- The merge is per season, not per series: a stale virtual season the authority does not list is suppressed,
  while a real later season only a secondary provider knows is still offered.
- Every reachable provider is fetched and merged each pass, more calls per series than the old deferral,
  bounded by the same stalest-first rotation, read-through cache, and per-service circuit.
- Series no provider can resolve are still surfaced from their virtual episodes alone, in one bulk pass, so a
  large library's missing episodes appear every run regardless of the provider batch cap.
- One place configures providers: the library. There is no plugin toggle to fall out of step with it.
