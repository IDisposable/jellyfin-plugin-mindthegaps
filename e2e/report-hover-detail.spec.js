// The title's hover-revealed detail has two independent ways to be visible: a transient hover/focus
// preview (cgHoverShown, ends when the pointer leaves both the title and the detail itself) and a
// persistent click-pin (cgHoverPinned, ends only when that title or another row's is clicked again).
// Getting the hover boundary wrong looks fine on a quick glance (hovering the title works) and only
// breaks when the user tries to interact with the popup itself, which is what these specs pin down.
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

test('hovering the title shows the detail, and moving into the detail keeps it shown', async ({ page }) => {
    await setup(page);
    const row = page.locator('.cgRow').first();
    const title = row.locator('.cgTitle');
    const detail = row.locator('.cgHoverDetail');

    await title.dispatchEvent('mouseover', { bubbles: true });
    await expect(detail).toHaveClass(/cgHoverShown/);

    // The pointer moving from the title down into the now-visible detail: a mouseout on the title
    // whose relatedTarget is inside the detail must not count as "left".
    await page.evaluate(() => {
        const t = document.querySelector('.cgRow .cgTitle');
        const d = document.querySelector('.cgRow .cgHoverDetail');
        t.dispatchEvent(new MouseEvent('mouseout', { bubbles: true, relatedTarget: d }));
        d.dispatchEvent(new MouseEvent('mouseover', { bubbles: true, relatedTarget: t }));
    });
    await expect(detail).toHaveClass(/cgHoverShown/);

    // Leaving the detail for somewhere outside both title and detail hides it.
    await page.evaluate(() => {
        const d = document.querySelector('.cgRow .cgHoverDetail');
        d.dispatchEvent(new MouseEvent('mouseout', { bubbles: true, relatedTarget: document.body }));
    });
    await expect(detail).not.toHaveClass(/cgHoverShown/);
});

test('clicking a title pins the detail; only another click on it or another title dismisses it', async ({ page }) => {
    await setup(page);
    const rows = page.locator('.cgRow');
    const title0 = rows.nth(0).locator('.cgTitle');
    const detail0 = rows.nth(0).locator('.cgHoverDetail');
    const title1 = rows.nth(1).locator('.cgTitle');
    const detail1 = rows.nth(1).locator('.cgHoverDetail');

    await title0.evaluate((el) => el.click());
    await expect(detail0).toHaveClass(/cgHoverPinned/);

    // Clicking elsewhere on the page must not unpin it.
    await page.click('#cgSummary');
    await expect(detail0).toHaveClass(/cgHoverPinned/);

    // Clicking a different row's title moves the pin rather than adding a second one.
    await title1.evaluate((el) => el.click());
    await expect(detail0).not.toHaveClass(/cgHoverPinned/);
    await expect(detail1).toHaveClass(/cgHoverPinned/);

    // Clicking that same title again unpins it.
    await title1.evaluate((el) => el.click());
    await expect(detail1).not.toHaveClass(/cgHoverPinned/);
});

test('the detail body has no repeated title and includes where-to-watch content', async ({ page }) => {
    await setup(page);
    const row = page.locator('.cgRow').first();
    await row.locator('.cgTitle').evaluate((el) => el.click());
    const html = await row.locator('.cgHoverDetail').innerHTML();
    expect(html).not.toContain("Breakfast at Tiffany's");
    expect(html).toMatch(/Search JustWatch|Look up where to watch/);
});
