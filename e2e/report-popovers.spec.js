// Row popovers (Watch/Info/Actions) render as plain in-flow content, not position:fixed, precisely
// because jellyfin-web's page wrapper sets CSS containment that breaks fixed positioning (see
// CLAUDE.md's "position: fixed is not safe" note and build-harness.js). These specs run the real
// dashboard JS/CSS inside that exact wrapper, so a regression back to floating positioning (or a
// broken click/accordion wire-up) fails here instead of shipping silently, the way it did before.
const { test, expect } = require('@playwright/test');
const { openReport } = require('./support/report-page');
const { baseSummary, movieItem } = require('./support/fixtures');

async function setup(page) {
    const summary = baseSummary({ DomainPatternCounts: { Movies: { Recommendation: 1 }, Shows: {}, Music: {}, Books: {} }, TotalGaps: 1 });
    await openReport(page, summary, { Movies: [movieItem()] });
}

test('each popover opens with real, visible content', async ({ page }) => {
    await setup(page);
    for (const kind of ['watch', 'info', 'actions']) {
        const summary = page.locator('.cgRow').first().locator(`.cgPop[data-pop="${kind}"] > summary`);
        await summary.evaluate((el) => el.click());
        const pop = page.locator('.cgRow').first().locator(`.cgPop[data-pop="${kind}"]`);
        await expect(pop).toHaveAttribute('open', '');
        const body = pop.locator('> .cgPopBody');
        await expect(body).toBeVisible();
        const box = await body.boundingBox();
        expect(box.width).toBeGreaterThan(50);
        expect(box.height).toBeGreaterThan(10);
    }
});

test('only one popover is open at a time (accordion)', async ({ page }) => {
    await setup(page);
    const row = page.locator('.cgRow').first();
    await row.locator('.cgPop[data-pop="watch"] > summary').evaluate((el) => el.click());
    await row.locator('.cgPop[data-pop="info"] > summary').evaluate((el) => el.click());
    await row.locator('.cgPop[data-pop="actions"] > summary').evaluate((el) => el.click());

    await expect(row.locator('.cgPop[data-pop="watch"]')).not.toHaveAttribute('open', '');
    await expect(row.locator('.cgPop[data-pop="info"]')).not.toHaveAttribute('open', '');
    await expect(row.locator('.cgPop[data-pop="actions"]')).toHaveAttribute('open', '');
});

test('an action button inside an open popover still fires through delegation', async ({ page }) => {
    await setup(page);
    const row = page.locator('.cgRow').first();
    await row.locator('.cgPop[data-pop="actions"] > summary').evaluate((el) => el.click());
    await row.locator('.cgDiagnose').evaluate((el) => el.click());
    await expect(page.locator('#cgDiagModal')).toBeVisible();
});

test('no JS errors during any of this', async ({ page }) => {
    await setup(page);
    const row = page.locator('.cgRow').first();
    await row.locator('.cgPop[data-pop="watch"] > summary').evaluate((el) => el.click());
    await row.locator('.cgPop[data-pop="actions"] > summary').evaluate((el) => el.click());
    const errors = await page.evaluate(() => window.__uiTestErrors);
    expect(errors).toEqual([]);
});
