# 8. Gap ids are stable, derived from durable keys, and a persistence contract

Status: Accepted.

## Context

Every `GapItem` carries an `Id` that the engine de-dups on, so the same missing episode found by the
library reader and the TVmaze and TheTVDB cross-checks collapses to one entry (that is what
`SeriesGapKey` exists for). Two features also persist by that id and so depend on it being stable across
rescans and plugin upgrades:

- Gap resolutions (a note marking a gap "not really missing") are stored in `resolutions.json` keyed by
  gap id.
- The scan carries availability and resolved external ids forward across rescans by gap id, so it does
  not re-look-up or wipe what a previous pass found.

Both only work if the same logical gap gets the same id on every scan.

## Decision

Gap ids are derived solely from durable identifiers, never from volatile things (array position, scan
order, timestamps), and each source's id format is treated as a stable, versioned contract:

- Missing episode: `seriescontent:{ownedSeriesGuid:N}:s{NN}e{NN}` (`SeriesGapKey.Episode`), shared by
  all three series sources so they agree on one key.
- Collection part: `collection:{tmdbCollectionId}:{tmdbMovieId}`.
- Filmography (TMDB and Trakt): `filmography:movie:{tmdbId}` (or `filmography:movie:imdb:{imdbId}` when
  only IMDb is known), keyed on the work, not the person, so two creators surfacing the same film
  collapse to one gap.
- Recommendation: `recommendation:{movie|series}:{tmdbId}`.

Changing one of these formats is a data migration, not a refactor.

## Consequences

- The same gap gets the same id across rescans, so resolutions and the availability/external-id
  carry-forward match the right gaps with no extra bookkeeping.
- The formats are now a persistence contract: changing one silently orphans existing resolutions and
  drops the carry-forward for that source. If a format must change, migrate the keys.
- Episode ids depend on the owned series' Jellyfin guid, so deleting and re-adding a series orphans its
  episode resolutions. That is acceptable: it is a different library item.
- Orphaned resolutions (a gap resolved, then acquired so it is no longer missing, or whose id changed)
  are left in place rather than pruned on scan. A source failing transiently can make a gap momentarily
  absent, and pruning would wrongly drop a valid resolution. They are tiny, and only ever resurface
  (and become clearable) when the matching gap reappears under "Show resolved".
