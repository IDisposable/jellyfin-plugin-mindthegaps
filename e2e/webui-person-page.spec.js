// Drives the real mindthegaps.webui.js against a fake jellyfin-web Person page, the way
// report-*.spec.js drive the real report.js against a fake copy of this plugin's own dashboard. This
// is the surface CLAUDE.md's "position: fixed is not safe" note is about: the script renders inside
// jellyfin-web's own page, which sets the same CSS containment, so a regression here would be
// invisible without actually rendering it inside that containing block. The cards themselves carry no
// actions: clicking one opens a detail dialog appended to document.body (escaping that same CSS
// containment on purpose, see webui-dialog.spec.js), and Add-to-TODO lives inside it.
const { test, expect } = require('@playwright/test');
const { buildWebUiHarness } = require('./support/webui-harness');

const PERSON_ITEM = { Id: 'person-1', Name: 'Some Actor', Type: 'Person' };

async function openPersonPage(page, harnessPath) {
    await page.goto('file://' + harnessPath);
    await page.evaluate(() => { window.location.hash = '#/details?id=person-1'; });
    await page.evaluate(() => {
        document.querySelector('.page').dispatchEvent(new Event('viewshow', { bubbles: true }));
    });
}

async function openCardDialog(page, gapId) {
    await page.locator('[data-gapid="' + gapId + '"]').click();
    await expect(page.locator('.mtgDialogBackdrop')).toHaveClass(/mtgDialogOpen/);
}

test('renders the person page section with movies and series, in normal document flow', async ({ page }) => {
    const missing = {
        PersonId: 'person-1',
        PersonName: 'Some Actor',
        Reason: null,
        Movies: [{ GapId: 'filmography:movie:1', Title: 'A Missing Movie', Year: 2001, Role: 'as Lead', TmdbId: 1, ImageUrl: 'https://example.com/poster.jpg', Upcoming: false }],
        Series: [{ GapId: 'filmography:series:2', Title: 'A Missing Show', Year: 2010, Role: 'as Regular', TmdbId: 2, ImageUrl: 'https://example.com/poster2.jpg', Upcoming: false }]
    };
    const harnessPath = buildWebUiHarness(PERSON_ITEM, missing);
    await openPersonPage(page, harnessPath);

    const section = page.locator('#mtgPersonMissing');
    await expect(section).toBeVisible();
    await expect(section.getByText('Missing from your library')).toBeVisible();
    await expect(section.getByText('A Missing Movie')).toBeVisible();
    await expect(section.getByText('A Missing Show')).toBeVisible();

    // Plain in-flow content: no position:fixed/absolute anywhere in the inserted section, so it cannot
    // land somewhere else under jellyfin-web's own CSS containment.
    const positions = await section.evaluate((el) => {
        const all = [el, ...el.querySelectorAll('*')];
        return all.map((e) => getComputedStyle(e).position);
    });
    expect(positions).not.toContain('fixed');
    expect(positions).not.toContain('absolute');

    // Inserted right before #similarCollapsible, as a sibling within the normal document flow.
    const order = await page.evaluate(() => {
        const parent = document.querySelector('.detailPageContent');
        return Array.prototype.indexOf.call(parent.children, document.getElementById('mtgPersonMissing'))
            < Array.prototype.indexOf.call(parent.children, document.getElementById('similarCollapsible'));
    });
    expect(order).toBe(true);
});

test('no TODO button for a signed-out/rating-limited viewer, but the TMDB link still works', async ({ page }) => {
    const missing = {
        CanTodo: false,
        Reason: null,
        Movies: [{ GapId: 'filmography:movie:1', Title: 'A Missing Movie', Year: 2001, Role: null, Kind: 'Movie', TmdbId: 603, ImageUrl: null, Upcoming: false }],
        Series: []
    };
    const harnessPath = buildWebUiHarness(PERSON_ITEM, missing);
    await openPersonPage(page, harnessPath);
    await openCardDialog(page, 'filmography:movie:1');

    await expect(page.locator('.mtgDialog .mtgWantButton')).toHaveCount(0);

    const tmdbLink = page.locator('.mtgDialog .mtgDialogLinks a').first();
    await expect(tmdbLink).toHaveText('View on TMDB');
    await expect(tmdbLink).toHaveAttribute('href', 'https://www.themoviedb.org/movie/603');
    await expect(tmdbLink).toHaveAttribute('target', '_blank');
    await expect(tmdbLink).toHaveAttribute('rel', /noopener/);
});

test('the TMDB link links to the right kind of page for a series credit', async ({ page }) => {
    const missing = {
        CanTodo: false,
        Reason: null,
        Movies: [],
        Series: [{ GapId: 'filmography:series:2', Title: 'A Missing Show', Year: 2010, Role: null, Kind: 'Series', TmdbId: 1396, ImageUrl: null, Upcoming: false }]
    };
    const harnessPath = buildWebUiHarness(PERSON_ITEM, missing);
    await openPersonPage(page, harnessPath);
    await openCardDialog(page, 'filmography:series:2');

    const tmdbLink = page.locator('.mtgDialog .mtgDialogLinks a').first();
    await expect(tmdbLink).toHaveAttribute('href', 'https://www.themoviedb.org/tv/1396');
});

test('a signed-in user gets the want-to-watch button', async ({ page }) => {
    const missing = {
        CanTodo: true,
        Reason: null,
        Movies: [{ GapId: 'filmography:movie:1', Title: 'A Missing Movie', Year: 2001, Role: null, Kind: 'Movie', TmdbId: 603, ImageUrl: null, Upcoming: false }],
        Series: []
    };
    const harnessPath = buildWebUiHarness(PERSON_ITEM, missing, null, 1);
    await openPersonPage(page, harnessPath);
    await openCardDialog(page, 'filmography:movie:1');

    const wantBtn = page.locator('.mtgDialog .mtgWantButton');
    await expect(wantBtn).toHaveText('Want to watch');
    await wantBtn.click();
    await expect(wantBtn).toHaveText('On your list');
    await expect(wantBtn).toBeEnabled();

    const todoUrl = await page.evaluate(() => window.__lastTodoUrl);
    expect(todoUrl).toContain('Person/person-1/Todo');
    expect(todoUrl).toContain('gapId=filmography%3Amovie%3A1');
});

test('a failed update leaves the button as it was and enabled', async ({ page }) => {
    const missing = {
        CanTodo: true,
        Reason: null,
        Movies: [{ GapId: 'filmography:movie:1', Title: 'A Missing Movie', Year: 2001, Role: null, Kind: 'Movie', TmdbId: 603, ImageUrl: null, Upcoming: false }],
        Series: []
    };
    const harnessPath = buildWebUiHarness(PERSON_ITEM, missing, null, 'fail');
    await openPersonPage(page, harnessPath);
    await openCardDialog(page, 'filmography:movie:1');

    const wantBtn = page.locator('.mtgDialog .mtgWantButton');
    await wantBtn.click();
    await expect(wantBtn).toHaveText('Want to watch');
    await expect(wantBtn).toBeEnabled();
});

test('shows the reason instead of a list when the person cannot be looked up', async ({ page }) => {
    const missing = { Reason: 'TMDB has no record for this person right now.', Movies: [], Series: [] };
    const harnessPath = buildWebUiHarness(PERSON_ITEM, missing);
    await openPersonPage(page, harnessPath);

    const section = page.locator('#mtgPersonMissing');
    await expect(section).toBeVisible();
    await expect(section.getByText('TMDB has no record for this person right now.')).toBeVisible();
});

test('renders nothing when the surface is off (the endpoint 404s)', async ({ page }) => {
    const harnessPath = buildWebUiHarness(PERSON_ITEM, null);
    await openPersonPage(page, harnessPath);
    await page.waitForTimeout(200);

    await expect(page.locator('#mtgPersonMissing')).toHaveCount(0);
});

test('renders nothing on a non-Person page, and no JS errors happen along the way', async ({ page }) => {
    const movieItem = { Id: 'movie-1', Name: 'A Movie', Type: 'Movie' };
    const harnessPath = buildWebUiHarness(movieItem, { Reason: null, Movies: [], Series: [] });
    await openPersonPage(page, harnessPath);
    await page.evaluate(() => { window.location.hash = '#/details?id=movie-1'; });
    await page.evaluate(() => document.querySelector('.page').dispatchEvent(new Event('viewshow', { bubbles: true })));
    await page.waitForTimeout(200);

    await expect(page.locator('#mtgPersonMissing')).toHaveCount(0);
    const errors = await page.evaluate(() => window.__uiTestErrors);
    expect(errors).toEqual([]);
});
