// Drives the real mindthegaps.webui.js against a fake jellyfin-web Movie/Series detail page, the
// item-page sibling of webui-person-page.spec.js. See that file's header for why this harness (not the
// dashboard one) exists and what it is checking for.
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

test('renders the related row after similarCollapsible, in normal document flow', async ({ page }) => {
    const related = {
        ItemId: 'movie-1',
        ItemName: 'A Movie',
        CanSend: true,
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

test('a related title can be sent; the button reflects the outcome', async ({ page }) => {
    const related = {
        CanSend: true,
        Reason: null,
        Titles: [{ GapId: 'recommendation:movie:2', Title: 'A Similar Movie', Year: 2005, TmdbId: 2, ImageUrl: 'https://example.com/poster.jpg', Upcoming: false }]
    };
    const harnessPath = buildWebUiHarness(MOVIE_ITEM, related, { Success: true, Message: 'Sent 1 item(s).' });
    await openItemPage(page, harnessPath);

    const button = page.locator('#mtgRelatedMissing .mtgSendButton');
    await button.click();
    await expect(button).toHaveText('Sent');

    const sendUrl = await page.evaluate(() => window.__lastSendUrl);
    expect(sendUrl).toContain('Item/movie-1/Send');
    expect(sendUrl).toContain('gapId=recommendation%3Amovie%3A2');
});

test('no Send button when the caller cannot send, but the TMDB link still works', async ({ page }) => {
    const related = { CanSend: false, CanTodo: false, Reason: null, Titles: [{ GapId: 'recommendation:movie:2', Title: 'A Similar Movie', Year: 2005, Kind: 'Movie', TmdbId: 603, ImageUrl: null, Upcoming: false }] };
    const harnessPath = buildWebUiHarness(MOVIE_ITEM, related);
    await openItemPage(page, harnessPath);

    await expect(page.locator('#mtgRelatedMissing')).toBeVisible();
    await expect(page.locator('#mtgRelatedMissing .mtgSendButton')).toHaveCount(0);
    await expect(page.locator('#mtgRelatedMissing .mtgTodoButton')).toHaveCount(0);

    const tmdbLink = page.locator('#mtgRelatedMissing .mtgTmdbLink');
    await expect(tmdbLink).toHaveAttribute('href', 'https://www.themoviedb.org/movie/603');
    await expect(tmdbLink).toHaveAttribute('target', '_blank');
});

test('an administrator with no arr configured gets an Add to TODO fallback', async ({ page }) => {
    const related = { CanSend: false, CanTodo: true, Reason: null, Titles: [{ GapId: 'recommendation:movie:2', Title: 'A Similar Movie', Year: 2005, Kind: 'Movie', TmdbId: 603, ImageUrl: null, Upcoming: false }] };
    const harnessPath = buildWebUiHarness(MOVIE_ITEM, related, null, 1);
    await openItemPage(page, harnessPath);

    const todoBtn = page.locator('#mtgRelatedMissing .mtgTodoButton');
    await expect(todoBtn).toBeVisible();
    await todoBtn.click();
    await expect(todoBtn).toHaveText('Added to TODO');

    const todoUrl = await page.evaluate(() => window.__lastTodoUrl);
    expect(todoUrl).toContain('Item/movie-1/Todo');
    expect(todoUrl).toContain('gapId=recommendation%3Amovie%3A2');
});

test('shows the reason instead of a row when the title has no TMDB id', async ({ page }) => {
    const related = { CanSend: false, Reason: 'This title has no TMDB id in the library, so similar titles cannot be looked up.', Titles: [] };
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
    const related = { CanSend: false, Reason: null, Titles: [{ GapId: 'recommendation:movie:2', Title: 'A Similar Movie', Year: 2005, TmdbId: 2, ImageUrl: null, Upcoming: false }] };
    const harnessPath = buildWebUiHarness(MOVIE_ITEM, related);
    await openItemPage(page, harnessPath);
    await expect(page.locator('#mtgRelatedMissing')).toBeVisible();

    await page.evaluate(() => {
        document.querySelector('.page').dispatchEvent(new Event('viewshow', { bubbles: true }));
    });
    await expect(page.locator('#mtgRelatedMissing')).toBeVisible();
    await expect(page.locator('#mtgPersonMissing')).toHaveCount(0);
});
