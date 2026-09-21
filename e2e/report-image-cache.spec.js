// A row's thumbnail and service logos load through the server's image cache, and fall back to the provider's own
// address when the cache cannot serve them, so the cache can never make an image go missing.
const { test, expect } = require('@playwright/test');
const { openReport } = require('./support/report-page');
const { baseSummary, movieItem } = require('./support/fixtures');
const { pointImagesAtTheServer, serveImages } = require('./support/image-cache');

const POSTER = 'https://image.tmdb.org/t/p/w500/poster.jpg';
const LOGO = 'https://image.tmdb.org/t/p/w45/logo.jpg';

const summary = () => baseSummary({ DomainPatternCounts: { Movies: { Recommendation: 1 }, Shows: {}, Music: {}, Books: {} }, TotalGaps: 1 });

const item = () => movieItem({
    ImageUrl: POSTER,
    Availability: [{ Provider: 'Netflix', MonetizationType: 'flatrate', LogoUrl: LOGO, Url: 'https://example.test/watch' }],
    AvailabilityChecked: true
});

const loaded = (locator) => locator.evaluate((img) => img.complete && img.naturalWidth > 0);

test('a thumbnail and a service logo are loaded from the server, not from the provider', async ({ page }) => {
    const seen = await serveImages(page, 200, 200);
    await openReport(page, summary(), { Movies: [item()] }, undefined, pointImagesAtTheServer);

    const thumb = page.locator('.cgRow img.cgThumb');
    const logo = page.locator('.cgRow img.cgSvc');
    await expect.poll(() => loaded(thumb)).toBe(true);
    await expect.poll(() => loaded(logo)).toBe(true);

    expect(await thumb.getAttribute('src')).toBe('https://server.test/MindTheGaps/Image?u=' + encodeURIComponent(POSTER));
    expect(await logo.getAttribute('src')).toBe('https://server.test/MindTheGaps/Image?u=' + encodeURIComponent(LOGO));
    expect(seen.server).toHaveLength(2);
    expect(seen.provider).toHaveLength(0);
});

test('an image the server cannot serve is loaded from the provider instead', async ({ page }) => {
    const seen = await serveImages(page, 404, 200);
    await openReport(page, summary(), { Movies: [item()] }, undefined, pointImagesAtTheServer);

    const thumb = page.locator('.cgRow img.cgThumb');
    await expect.poll(() => loaded(thumb)).toBe(true);
    expect(await thumb.getAttribute('src')).toBe(POSTER);
    await expect(page.locator('.cgRow img.cgSvc')).toHaveAttribute('src', LOGO);
    expect(seen.provider).toContain(POSTER);
});

test('an image neither the server nor the provider has falls back to the placeholders', async ({ page }) => {
    await serveImages(page, 404, 404);
    await openReport(page, summary(), { Movies: [item()] }, undefined, pointImagesAtTheServer);

    const row = page.locator('.cgRow').first();
    await expect(row.locator('span.cgThumb.cgThumbEmpty')).toBeVisible();
    await expect(row.locator('img.cgThumb')).toHaveCount(0);
    await expect(row.locator('.cgSvc.cgSvcText')).toHaveText('N');
});
