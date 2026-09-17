// Row popovers (Watch/Info/Actions) render as plain in-flow content, not position:fixed, precisely
// because jellyfin-web's page wrapper sets CSS containment that breaks fixed positioning (see
// CLAUDE.md's "position: fixed is not safe" note and build-harness.js). These specs run the real
// dashboard JS/CSS inside that exact wrapper, so a regression back to floating positioning (or a
// broken click/accordion wire-up) fails here instead of shipping silently, the way it did before.
//
// The icons behave identically in compact and non-compact (spacious) view as long as the row is
// collapsed: click-to-open, one at a time. Only expanding the title's detail in non-compact view
// hides them (see #cgReportPanel:not(.cgCompactMode) .cgRow:has(.cgTitleDetail.cgPinned)
// .cgIcons), since their content folds into that detail once it is open (report-expanded-detail.spec.js
// covers the detail itself); compact view never folds that content in, so its icons stay regardless.
const { test, expect } = require('@playwright/test');
const { openReport } = require('./support/report-page');
const { baseSummary, movieItem } = require('./support/fixtures');

async function setup(page) {
    const summary = baseSummary({ DomainPatternCounts: { Movies: { Recommendation: 1 }, Shows: {}, Music: {}, Books: {} }, TotalGaps: 1 });
    await openReport(page, summary, { Movies: [movieItem()] });
}

async function toCompact(page) {
    await page.locator('#cgCompact').evaluate((el) => { el.checked = true; el.dispatchEvent(new Event('change', { bubbles: true })); });
}

for (const mode of ['compact', 'non-compact (collapsed)']) {
    test.describe(mode, () => {
        test('each popover opens on click, with real visible content', async ({ page }) => {
            await setup(page);
            if (mode === 'compact') { await toCompact(page); }
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
            if (mode === 'compact') { await toCompact(page); }
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
            if (mode === 'compact') { await toCompact(page); }
            const row = page.locator('.cgRow').first();
            await row.locator('.cgPop[data-pop="actions"] > summary').evaluate((el) => el.click());
            await row.locator('.cgDiagnose').evaluate((el) => el.click());
            await expect(page.locator('#cgDiagModal')).toBeVisible();
        });
    });
}

test.describe('non-compact (spacious) view, expanded', () => {
    test('expanding the detail hides the icons; collapsing it again brings them back', async ({ page }) => {
        await setup(page);
        const row = page.locator('.cgRow').first();
        await expect(row.locator('.cgIcons')).toBeVisible();

        await row.locator('.cgTitle').evaluate((el) => el.click());
        await expect(row.locator('.cgIcons')).toBeHidden();

        await row.locator('.cgTitle').evaluate((el) => el.click());
        await expect(row.locator('.cgIcons')).toBeVisible();
    });
});

test('no JS errors across compact and non-compact interaction', async ({ page }) => {
    await setup(page);
    const row = page.locator('.cgRow').first();
    await row.locator('.cgTitle').evaluate((el) => el.click());
    await row.locator('.cgDiagnose').evaluate((el) => el.click());
    await page.locator('#cgDiagClose').evaluate((el) => el.click());
    await toCompact(page);
    await page.locator('.cgRow').first().locator('.cgPop[data-pop="watch"] > summary').evaluate((el) => el.click());
    const errors = await page.evaluate(() => window.__uiTestErrors);
    expect(errors).toEqual([]);
});
