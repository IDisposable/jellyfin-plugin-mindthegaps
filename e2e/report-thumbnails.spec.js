// A row's thumbnail can be a guessed URL (MusicBrainz's cover art is a fixed formula keyed by MBID,
// with no check that art actually exists there), so a failed image load is expected, not a bug: it
// should fall back to the plain "no image" placeholder instead of the browser's broken-image icon.
const { test, expect } = require('@playwright/test');
const { openReport } = require('./support/report-page');
const { baseSummary, movieItem } = require('./support/fixtures');

test('a thumbnail that fails to load falls back to the empty placeholder', async ({ page }) => {
    const summary = baseSummary({ DomainPatternCounts: { Movies: { Recommendation: 1 }, Shows: {}, Music: {}, Books: {} }, TotalGaps: 1 });
    // Bytes that are not an image: fails to load with no network dependency, the same way a real 404 from a
    // remote host does. A data: URL is never deferred by loading="lazy", so the failure does not wait on
    // when the browser decides the image is near the viewport.
    const items = [movieItem({ ImageUrl: 'data:image/png;base64,bm90IGFuIGltYWdl' })];
    await openReport(page, summary, { Movies: items });

    const row = page.locator('.cgRow').first();
    await expect(row.locator('span.cgThumb.cgThumbEmpty')).toBeVisible();
    await expect(row.locator('img.cgThumb')).toHaveCount(0);
});
