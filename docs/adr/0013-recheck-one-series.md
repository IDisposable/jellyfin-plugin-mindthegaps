# 13. Re-check one series in place, without a full rescan

Status: Accepted.

## Context

While fixing a library's metadata (renumbering an episode, correcting a series' provider id) you want to
see whether the fix cleared a false gap, but a full scan is too slow and too broad to run for one show:
the cross-checks rotate stalest-first, so a full scan might not even revisit the series you just touched.

## Decision

The three series-content sources (the library reader and the TVmaze and TheTVDB cross-checks) share an
`ISeriesContentSource.CheckSeriesAsync` seam: the per-series step the full scan already loops over, exposed
to run on its own. `GapEngine.RecheckSeriesAsync` runs the enabled ones for a single series, de-dupes, and
carries the prior enrichment forward; `GapStore.ReplaceSeriesGaps` swaps just that series' content gaps in
the report (dropping any a fix resolved) and leaves every other gap untouched, preserving the report's scan
time and version. A `RecheckSeries` endpoint drives it synchronously, behind a refresh icon on each series
and season header; that stays quick because the library read is local and the cross-check lookups are
served from the read-through cache.

## Consequences

- A metadata fix can be verified for one show in a click, with no full rescan and no waiting for the
  rotation to come back around.
- A re-check is a partial update: it does not bump the report's scan time or version, so it never triggers
  the post-upgrade rescan nudge, and the swap is scoped by the series' source id and a "seriescontent:" id
  so it cannot disturb another gap.
- It runs in the request rather than the background (unlike a full scan or an explore), which is fine for
  one series but would not be for the whole library; a wholly cold cross-check cache makes the first
  re-check of a show a few live calls, then cached.
