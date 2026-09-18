// A row's thumbnail can be a guessed URL (MusicBrainz's cover art is a fixed formula keyed by MBID,
// with no check that art actually exists there), so a failed image load is expected, not a bug: it
// should fall back to the plain "no image" placeholder instead of the browser's broken-image icon.
const { test, expect } = require('@playwright/test');
const { openReport } = require('./support/report-page');
const { baseSummary, movieItem } = require('./support/fixtures');

test('a thumbnail that fails to load falls back to the empty placeholder', async ({ page }) => {
    const summary = baseSummary({ DomainPatternCounts: { Movies: { Recommendation: 1 }, Shows: {}, Music: {}, Books: {} }, TotalGaps: 1 });
    // A local file:// path that cannot exist: fails to load with no network dependency, same as a
    // real 404 from a remote host.
    const items = [movieItem({ ImageUrl: 'file:///no-such-cover-image.jpg' })];
    await openReport(page, summary, { Movies: items });

    const row = page.locator('.cgRow').first();
    await expect(row.locator('span.cgThumb.cgThumbEmpty')).toBeVisible();
    await expect(row.locator('img.cgThumb')).toHaveCount(0);
});
