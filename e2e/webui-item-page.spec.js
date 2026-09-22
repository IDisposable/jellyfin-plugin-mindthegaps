// Drives the real mindthegaps.webui.js against a fake jellyfin-web Movie/Series detail page, the
// item-page sibling of webui-person-page.spec.js. See that file's header for why this harness (not the
// dashboard one) exists and what it is checking for, and for why Add-to-TODO lives inside the detail
// dialog rather than on the card itself.
const { test, expect } = require('@playwright/test');
const { buildWebUiHarness } = require('./support/webui-harness');

const MOVIE_ITEM = { Id: 'movie-1', Name: 'A Movie', Type: 'Movie' };

async function openItemPage(page, harnessPath) {
    await page.goto('file://' + harnessPath);
    await page.evaluate(() => { window.location.hash = '#/details?id=movie-1'; });
    await page.evaluate(() => {
        document.querySelector('.page').dispatchEvent(new Event('viewshow', { bubbles: true }));
    });
}

async function openCardDialog(page, gapId) {
    await page.locator('[data-gapid="' + gapId + '"]').click();
    await expect(page.locator('.mtgDialogBackdrop')).toHaveClass(/mtgDialogOpen/);
}

test('renders the related row after similarCollapsible, in normal document flow', async ({ page }) => {
    const related = {
        ItemId: 'movie-1',
        ItemName: 'A Movie',
        Reason: null,
        Titles: [{ GapId: 'recommendation:movie:2', Title: 'A Similar Movie', Year: 2005, TmdbId: 2, ImageUrl: 'https://example.com/poster.jpg', Upcoming: false }]
    };
    const harnessPath = buildWebUiHarness(MOVIE_ITEM, related);
    await openItemPage(page, harnessPath);

    const section = page.locator('#mtgRelatedMissing');
    await expect(section).toBeVisible();
    await expect(section.getByText("More like this you don't have")).toBeVisible();
    await expect(section.getByText('A Similar Movie')).toBeVisible();

    const positions = await section.evaluate((el) => {
        const all = [el, ...el.querySelectorAll('*')];
        return all.map((e) => getComputedStyle(e).position);
    });
    expect(positions).not.toContain('fixed');
    expect(positions).not.toContain('absolute');

    const order = await page.evaluate(() => {
        const parent = document.querySelector('.detailPageContent');
        return Array.prototype.indexOf.call(parent.children, document.getElementById('similarCollapsible'))
            < Array.prototype.indexOf.call(parent.children, document.getElementById('mtgRelatedMissing'));
    });
    expect(order).toBe(true);
});

test('no want-to-watch button when the caller cannot keep a list, but the TMDB link still works', async ({ page }) => {
    const related = { CanTodo: false, Reason: null, Titles: [{ GapId: 'recommendation:movie:2', Title: 'A Similar Movie', Year: 2005, Kind: 'Movie', TmdbId: 603, ImageUrl: null, Upcoming: false }] };
    const harnessPath = buildWebUiHarness(MOVIE_ITEM, related);
    await openItemPage(page, harnessPath);
    await openCardDialog(page, 'recommendation:movie:2');

    await expect(page.locator('.mtgDialog .mtgWantButton')).toHaveCount(0);

    const tmdbLink = page.locator('.mtgDialog .mtgDialogLinks a').first();
    await expect(tmdbLink).toHaveAttribute('href', 'https://www.themoviedb.org/movie/603');
    await expect(tmdbLink).toHaveAttribute('target', '_blank');
});

test('a signed-in user gets a want-to-watch button that puts the title on their list', async ({ page }) => {
    const related = { CanTodo: true, Reason: null, Titles: [{ GapId: 'recommendation:movie:2', Title: 'A Similar Movie', Year: 2005, Kind: 'Movie', TmdbId: 603, ImageUrl: null, Upcoming: false }] };
    const harnessPath = buildWebUiHarness(MOVIE_ITEM, related, null, 1);
    await openItemPage(page, harnessPath);
    await openCardDialog(page, 'recommendation:movie:2');

    const wantBtn = page.locator('.mtgDialog .mtgWantButton');
    await expect(wantBtn).toHaveText('Want to watch');
    await wantBtn.click();
    await expect(wantBtn).toHaveText('On your list');

    const todoUrl = await page.evaluate(() => window.__lastTodoUrl);
    expect(todoUrl).toContain('Item/movie-1/Todo');
    expect(todoUrl).toContain('gapId=recommendation%3Amovie%3A2');
});

test('shows the reason instead of a row when the title has no TMDB id', async ({ page }) => {
    const related = { Reason: 'This title has no TMDB id in the library, so similar titles cannot be looked up.', Titles: [] };
    const harnessPath = buildWebUiHarness(MOVIE_ITEM, related);
    await openItemPage(page, harnessPath);

    const section = page.locator('#mtgRelatedMissing');
    await expect(section).toBeVisible();
    await expect(section.getByText('This title has no TMDB id in the library, so similar titles cannot be looked up.')).toBeVisible();
});

test('renders nothing when the surface is off (the endpoint 404s)', async ({ page }) => {
    const harnessPath = buildWebUiHarness(MOVIE_ITEM, null);
    await openItemPage(page, harnessPath);
    await page.waitForTimeout(200);

    await expect(page.locator('#mtgRelatedMissing')).toHaveCount(0);
    const errors = await page.evaluate(() => window.__uiTestErrors);
    expect(errors).toEqual([]);
});

test('the person section and the related row do not collide on the same page instance', async ({ page }) => {
    // A single script instance serves every page; switching from a Person page to a Movie page must
    // clear the other surface's leftover section rather than stacking both.
    const related = { Reason: null, Titles: [{ GapId: 'recommendation:movie:2', Title: 'A Similar Movie', Year: 2005, TmdbId: 2, ImageUrl: null, Upcoming: false }] };
    const harnessPath = buildWebUiHarness(MOVIE_ITEM, related);
    await openItemPage(page, harnessPath);
    await expect(page.locator('#mtgRelatedMissing')).toBeVisible();

    await page.evaluate(() => {
        document.querySelector('.page').dispatchEvent(new Event('viewshow', { bubbles: true }));
    });
    await expect(page.locator('#mtgRelatedMissing')).toBeVisible();
    await expect(page.locator('#mtgPersonMissing')).toHaveCount(0);
});

// jellyfin-web's own rows on the item page sit inside .detailVerticalSection, which already pads the left
// edge, so its scrollers take no-padding. Without it the cards start a full padding further in than the
// row above them.
test('the related row uses the item page markup: no-padding on the scroller, title padded on the right', async ({ page }) => {
    const related = {
        ItemId: 'movie-1', ItemName: 'A Movie', Reason: null,
        Titles: [{ GapId: 'recommendation:movie:2', Title: 'A Similar Movie', Year: 2005, TmdbId: 2, ImageUrl: null, Upcoming: false }]
    };
    await openItemPage(page, buildWebUiHarness(MOVIE_ITEM, related));

    const section = page.locator('#mtgRelatedMissing');
    await expect(section.locator('[is="emby-scroller"]')).toHaveClass(/no-padding/);
    await expect(section.locator('h2.sectionTitle')).toHaveClass(/padded-right/);
});
