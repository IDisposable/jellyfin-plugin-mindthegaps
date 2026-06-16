# 5. Name "Mind the Gaps"; Shows vs Live TV terminology

Status: Accepted.

## Context

The original name "CollectionGaps" collided with Jellyfin's existing Collection (BoxSet) concept,
which is ironically one of the plugin's own targets, and it undersold the scope (filmographies,
series, recommendations). Separately, "TV" is ambiguous in Jellyfin: the "Shows" library is episodic
content, while "Live TV" is the tuner/EPG feature, which this plugin has nothing to do with.

## Decision

Rename the plugin to "Mind the Gaps" (`Jellyfin.Plugin.MindTheGaps`), keeping the same plugin GUID so
identity is preserved. Use "Shows / Series / Seasons / Episodes" for episodic content and reserve
"TV / Live TV" for the tuner feature. `CollectionGapSource` and `CollectionGapMapper` keep their
names on purpose: they genuinely handle TMDB collections.

## Consequences

- A clearer, broader brand that matches the already-generic internal model (`IGapSource`, `GapItem`,
  `GapEngine`, `GapPattern`).
- The internal `Gaps/Sources/Series/` namespace and `SeriesContentGapSource` reflect the corrected
  terminology; no identifier uses "TV" to mean episodic content.
