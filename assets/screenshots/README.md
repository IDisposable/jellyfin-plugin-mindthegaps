# Screenshot capture list

Capture targets for the README and the docs. Each is referenced from a doc as
`assets/screenshots/<name>.png` (from the README) or `../assets/screenshots/<name>.png` (from a file in
`docs/`), with an HTML comment next to it repeating the capture note. Until a file exists the reference
renders as a broken-image placeholder; drop the named PNG in this folder and it lights up with no
markdown edits.

Guidance for all shots:

- Use a populated library so groups, badges, and provider chips are non-trivial.
- A wide browser window so the report toolbar sits on one row; trim to the relevant region.
- The light or dark Jellyfin theme is fine, but keep it consistent across the set.
- Avoid showing real API keys: the Data sources section shot should have the key fields blank or
  obviously fake.
- PNG, roughly 1400px wide for full-page shots; tighter crops for the close-ups.

| File | Used in | Capture |
|---|---|---|
| `report-overview.png` | README, report-guide | The report open with a healthy number of gaps, a pattern tab active and the toolbar visible. The hero shot. |
| `report-set-completion.png` | report-guide | Set completion tab: a collection or series group with a few missing entries and the "N of M owned" coverage badge. |
| `report-creator-works.png` | report-guide | Creator works tab: the A-Z letter grouping with a creator expanded to show a few missing films. |
| `report-recommendations.png` | report-guide | Recommendations tab: results grouped by title with the owning seed title(s) shown on a row. |
| `report-toolbar.png` | report-guide | Close crop of the toolbar: type/sort filters, the checkboxes, search, saved-views row, and (on a Creator/Recommendations tab) the "Muted creators/sources" picker. |
| `report-row-actions.png` | report-guide | One gap row close up, showing its external links, the open-in-Jellyfin jump, a Mint button (virtual items enabled), and the Resolve / Not interested / Snooze actions. |
| `report-where-to-watch.png` | report-guide | A row (or rows) with streaming-provider chips populated, and the per-provider availability filter visible. |
| `config-what-to-scan.png` | configuration | The settings page "What to scan" section with the source toggles and the curated-set inputs. |
| `config-data-sources.png` | configuration | The "Data sources" section (TMDB key, webhook, Trakt/TVmaze/TheTVDB cross-checks). Keys blank or fake. |
| `config-limits.png` | configuration | The "Limits" section including the Max-per-run fields and the "Reset scan rotation" button. |
| `config-virtual-items.png` | configuration | The "Experimental: virtual items" section with the Remove buttons. |
