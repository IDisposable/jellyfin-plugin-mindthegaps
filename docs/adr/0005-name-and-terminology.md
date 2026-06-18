# 5. Name "Mind the Gaps"; Shows vs Live TV terminology

Status: Accepted.

## Context

"Collection" is loaded in Jellyfin: it is the BoxSet concept, which is one of this plugin's own targets,
so naming the plugin after it would collide and undersell the scope (filmographies, series,
recommendations). Separately, "TV" is ambiguous: the "Shows" library is episodic content, while "Live TV"
is the tuner/EPG feature, which this plugin has nothing to do with.

## Decision

Name the plugin "Mind the Gaps" (`Jellyfin.Plugin.MindTheGaps`); the GUID is fixed so identity is stable.
Use "Shows / Series / Seasons / Episodes" for episodic content and reserve "TV / Live TV" for the tuner
feature. `CollectionGapSource` and `CollectionGapMapper` keep their names on purpose: they genuinely
handle TMDB collections.

## Consequences

- A clearer, broader brand that matches the already-generic internal model (`IGapSource`, `GapItem`,
  `GapEngine`, `GapPattern`).
- The internal `Gaps/Sources/Series/` namespace and `SeriesContentGapSource` reflect the corrected
  terminology; no identifier uses "TV" to mean episodic content.
