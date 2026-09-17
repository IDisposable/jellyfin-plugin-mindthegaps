// The title's detail is click-only, not hover: it renders directly below the title (see renderRow),
// so a hover/mouseleave boundary can never distinguish "moving toward the detail" from "leaving
// entirely" (the earlier hover-based version dismissed itself the moment the pointer crossed back
// out over the title on its way down into the popup). cgPinned is the sole visibility switch,
// set by clicking the title, cleared only by clicking that title again or another row's.
const { test, expect } = require('@playwright/test');
const { openReport } = require('./support/report-page');
const { baseSummary, movieItem } = require('./support/fixtures');

async function setup(page) {
    const summary = baseSummary({ DomainPatternCounts: { Movies: { Recommendation: 2 }, Shows: {}, Music: {}, Books: {} }, TotalGaps: 2 });
    const items = [
        movieItem(),
        movieItem({ Id: 'justwatch:watchlist:200', Name: 'A Second Test Movie', Year: 1999, ProviderIds: { Tmdb: '200' }, Links: [], Overview: 'A second overview.' })
    ];
    await openReport(page, summary, { Movies: items });
}

test('clicking a title pins the detail; only another click on it or another title dismisses it', async ({ page }) => {
    await setup(page);
    const rows = page.locator('.cgRow');
    const title0 = rows.nth(0).locator('.cgTitle');
    const detail0 = rows.nth(0).locator('.cgTitleDetail');
    const title1 = rows.nth(1).locator('.cgTitle');
    const detail1 = rows.nth(1).locator('.cgTitleDetail');

    await title0.evaluate((el) => el.click());
    await expect(detail0).toHaveClass(/cgPinned/);

    // Clicking elsewhere on the page must not unpin it.
    await page.click('#cgSummary');
    await expect(detail0).toHaveClass(/cgPinned/);

    // Clicking a different row's title moves the pin rather than adding a second one.
    await title1.evaluate((el) => el.click());
    await expect(detail0).not.toHaveClass(/cgPinned/);
    await expect(detail1).toHaveClass(/cgPinned/);

    // Clicking that same title again unpins it.
    await title1.evaluate((el) => el.click());
    await expect(detail1).not.toHaveClass(/cgPinned/);
});

test('hovering the title does not reveal the detail', async ({ page }) => {
    await setup(page);
    const row = page.locator('.cgRow').first();
    await row.locator('.cgTitle').dispatchEvent('mouseover', { bubbles: true });
    await expect(row.locator('.cgTitleDetail')).not.toHaveClass(/cgPinned/);
    await expect(row.locator('.cgTitleDetail')).toBeHidden();
});

test('the title carries a native title= tooltip with the overview, for a zero-click hint', async ({ page }) => {
    await setup(page);
    const row = page.locator('.cgRow').filter({ hasText: "Breakfast at Tiffany's" });
    const title = await row.locator('.cgTitle').getAttribute('title');
    expect(title).toBe('A young New York socialite falls for her new neighbor.');
});

test.describe('non-compact (spacious) view', () => {
    test('the detail has no repeated title heading, and folds in where-to-watch, information, and actions', async ({ page }) => {
        await setup(page);
        const row = page.locator('.cgRow').filter({ hasText: "Breakfast at Tiffany's" });
        await row.locator('.cgTitle').evaluate((el) => el.click());
        const html = await row.locator('.cgTitleDetail').innerHTML();
        // The regression this guards against: a bolded paragraph repeating the row's own title,
        // which used to lead the detail body before the title moved above it in the row.
        expect(html).not.toMatch(/font-weight:\s*600[^>]*>Breakfast at Tiffany's/);
        expect(html).toMatch(/Search JustWatch|Look up where to watch/);
        expect(html).toContain('IMDb');
        expect(html).toContain('Diagnose');
    });

    test('the detail buttons tile inline instead of one per line', async ({ page }) => {
        await setup(page);
        const row = page.locator('.cgRow').filter({ hasText: "Breakfast at Tiffany's" });
        await row.locator('.cgTitle').evaluate((el) => el.click());
        const link = row.locator('.cgTitleDetail .cgLink').first();
        await expect(link).toHaveCSS('display', 'inline-block');
    });
});

test.describe('compact view', () => {
    test('the detail stays overview-and-watch only; information and actions stay behind their icons', async ({ page }) => {
        await setup(page);
        await page.locator('#cgCompact').evaluate((el) => { el.checked = true; el.dispatchEvent(new Event('change', { bubbles: true })); });
        const row = page.locator('.cgRow').filter({ hasText: "Breakfast at Tiffany's" });
        await row.locator('.cgTitle').evaluate((el) => el.click());
        const html = await row.locator('.cgTitleDetail').innerHTML();
        expect(html).toMatch(/Search JustWatch|Look up where to watch/);
        expect(html).not.toContain('Diagnose');
    });
});
