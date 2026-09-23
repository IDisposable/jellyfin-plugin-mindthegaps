// Drives the real mindthegaps.webui.js against a fake jellyfin-web studio list page (jellyfin-web's own
// generic list page, `#/list?studioId=…`, reached by clicking a studio credit on a movie's own page - not
// a page this plugin injects a section into). Confirmed live against a real server: the page carries no
// id of its own (unlike the item detail page's `itemDetailPage`), no `#similarCollapsible`-style anchor,
// and viewshow fires fresh on every navigation here, including between two different studios, so the
// "Missing from <studio>" row is simply appended after the studio's own movie grid with no
// MutationObserver needed.
const { test, expect } = require('@playwright/test');
const { buildWebUiStudioHarness } = require('./support/webui-harness');

const STUDIO_RESULT = {
    StudioId: 'studio-1',
    StudioName: 'A24',
    CanTodo: false,
    Reason: null,
    Titles: [{ GapId: 'curated:company:41077:1', Title: 'A Missing A24 Film', Year: 2021, Kind: 'Movie', TmdbId: 1, ImageUrl: 'https://example.com/poster.jpg', Upcoming: false }]
};

async function openStudioPage(page, harnessPath, studioId) {
    await page.goto('file://' + harnessPath);
    await visitStudio(page, studioId);
}

// Changes the hash and re-fires viewshow without reloading the document, the same way navigating from one
// studio's list page to another does live (both confirmed to fire a fresh viewshow against a real server).
async function visitStudio(page, studioId) {
    await page.evaluate((id) => { window.location.hash = '#/list?studioId=' + id + '&serverId=test-server'; }, studioId);
    await page.evaluate(() => {
        document.querySelector('.page').dispatchEvent(new Event('viewshow', { bubbles: true }));
    });
}

test('renders the missing-movies row after the studio\'s own grid, in normal document flow', async ({ page }) => {
    const harnessPath = buildWebUiStudioHarness(STUDIO_RESULT);
    await openStudioPage(page, harnessPath, 'studio-1');

    const section = page.locator('#mtgStudioMissing');
    await expect(section).toBeVisible();
    await expect(section.getByText('Missing from A24')).toBeVisible();
    await expect(section.getByText('A Missing A24 Film')).toBeVisible();

    const positions = await section.evaluate((el) => [el, ...el.querySelectorAll('*')].map((e) => getComputedStyle(e).position));
    expect(positions).not.toContain('fixed');
    expect(positions).not.toContain('absolute');

    const order = await page.evaluate(() => {
        const pageEl = document.querySelector('.page');
        const grid = pageEl.querySelector('.itemsViewSettingsContainer').parentElement;
        return Array.prototype.indexOf.call(pageEl.children, grid) < Array.prototype.indexOf.call(pageEl.children, document.getElementById('mtgStudioMissing'));
    });
    expect(order).toBe(true);
});

test('shows the reason instead of a row when the studio has no TMDB match', async ({ page }) => {
    const result = {
        StudioId: 'studio-1',
        StudioName: 'A Local Studio',
        CanTodo: false,
        Reason: 'This studio could not be matched to a TMDB company, so its movies cannot be looked up.',
        Titles: []
    };
    await openStudioPage(page, buildWebUiStudioHarness(result), 'studio-1');

    await expect(page.locator('#mtgStudioMissing')).toContainText('This studio could not be matched to a TMDB company');
});

test('nothing is rendered when nothing is missing, or the surface is off', async ({ page }) => {
    const none = { StudioId: 'studio-1', StudioName: 'A24', CanTodo: false, Reason: null, Titles: [] };
    await openStudioPage(page, buildWebUiStudioHarness(none), 'studio-1');
    await expect(page.locator('#mtgStudioMissing')).toHaveCount(0);

    await openStudioPage(page, buildWebUiStudioHarness(null), 'studio-1');
    await expect(page.locator('#mtgStudioMissing')).toHaveCount(0);
    const errors = await page.evaluate(() => window.__uiTestErrors);
    expect(errors).toEqual([]);
});

test('a signed-in user can add a missing movie to their list from the row', async ({ page }) => {
    const result = Object.assign({}, STUDIO_RESULT, { CanTodo: true });
    await openStudioPage(page, buildWebUiStudioHarness(result, null, 1), 'studio-1');

    await page.locator('[data-gapid="' + STUDIO_RESULT.Titles[0].GapId + '"] .mtgWant').click();

    const todoUrl = await page.evaluate(() => window.__lastTodoUrl);
    expect(todoUrl).toContain('Studio/studio-1/Todo');
    expect(todoUrl).toContain('gapId=' + encodeURIComponent(STUDIO_RESULT.Titles[0].GapId));
});

test('landing on a different studio replaces the row rather than stacking it', async ({ page }) => {
    const harnessPath = buildWebUiStudioHarness(STUDIO_RESULT);
    await openStudioPage(page, harnessPath, 'studio-1');
    await expect(page.locator('#mtgStudioMissing')).toHaveCount(1);

    // Same fake page instance, just a fresh viewshow for a different studio - the harness always answers
    // with the same fixed result, so this proves the row is replaced in place, not duplicated.
    await visitStudio(page, 'studio-2');
    await expect(page.locator('#mtgStudioMissing')).toHaveCount(1);
});
