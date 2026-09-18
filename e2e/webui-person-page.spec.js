// Drives the real mindthegaps.webui.js against a fake jellyfin-web Person page, the way
// report-*.spec.js drive the real report.js against a fake copy of this plugin's own dashboard. This
// is the surface CLAUDE.md's "position: fixed is not safe" note is about: the script renders inside
// jellyfin-web's own page, which sets the same CSS containment, so a regression here would be
// invisible without actually rendering it inside that containing block.
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

test('renders the person page section with movies and series, in normal document flow', async ({ page }) => {
    const missing = {
        PersonId: 'person-1',
        PersonName: 'Some Actor',
        CanSendMovies: true,
        CanSendSeries: false,
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

test('a movie can be sent to Radarr; the button reflects the outcome', async ({ page }) => {
    const missing = {
        CanSendMovies: true,
        CanSendSeries: false,
        Reason: null,
        Movies: [{ GapId: 'filmography:movie:1', Title: 'A Missing Movie', Year: 2001, Role: 'as Lead', TmdbId: 1, ImageUrl: null, Upcoming: false }],
        Series: []
    };
    const harnessPath = buildWebUiHarness(PERSON_ITEM, missing, { Success: true, Message: 'Sent 1 item(s).' });
    await openPersonPage(page, harnessPath);

    const button = page.locator('#mtgPersonMissing .mtgSendButton');
    await expect(button).toBeVisible();
    await button.click();
    await expect(button).toHaveText('Sent');
    await expect(button).toBeDisabled();

    const sendUrl = await page.evaluate(() => window.__lastSendUrl);
    expect(sendUrl).toContain('Person/person-1/Send');
    expect(sendUrl).toContain('gapId=filmography%3Amovie%3A1');
});

test('a failed send re-enables the button and shows the message', async ({ page }) => {
    const missing = {
        CanSendMovies: true,
        CanSendSeries: false,
        Reason: null,
        Movies: [{ GapId: 'filmography:movie:1', Title: 'A Missing Movie', Year: 2001, Role: null, TmdbId: 1, ImageUrl: null, Upcoming: false }],
        Series: []
    };
    const harnessPath = buildWebUiHarness(PERSON_ITEM, missing, { Success: false, Message: 'Radarr rejected it.' });
    await openPersonPage(page, harnessPath);

    const button = page.locator('#mtgPersonMissing .mtgSendButton');
    await button.click();
    await expect(button).toHaveText('Download Now');
    await expect(button).toBeEnabled();
});

test('no Send or TODO button for a non-administrator viewer, but the TMDB link still works', async ({ page }) => {
    const missing = {
        CanSendMovies: false,
        CanSendSeries: false,
        CanTodo: false,
        Reason: null,
        Movies: [{ GapId: 'filmography:movie:1', Title: 'A Missing Movie', Year: 2001, Role: null, Kind: 'Movie', TmdbId: 603, ImageUrl: null, Upcoming: false }],
        Series: []
    };
    const harnessPath = buildWebUiHarness(PERSON_ITEM, missing);
    await openPersonPage(page, harnessPath);

    await expect(page.locator('#mtgPersonMissing')).toBeVisible();
    await expect(page.locator('#mtgPersonMissing .mtgSendButton')).toHaveCount(0);
    await expect(page.locator('#mtgPersonMissing .mtgTodoButton')).toHaveCount(0);

    const tmdbLink = page.locator('#mtgPersonMissing .mtgTmdbLink');
    await expect(tmdbLink).toHaveCount(1);
    await expect(tmdbLink).toHaveAttribute('href', 'https://www.themoviedb.org/movie/603');
    await expect(tmdbLink).toHaveAttribute('target', '_blank');
    await expect(tmdbLink).toHaveAttribute('rel', /noopener/);
});

test('the TMDB link is present even when Send is available, and links to the right kind of page', async ({ page }) => {
    const missing = {
        CanSendMovies: false,
        CanSendSeries: true,
        CanTodo: false,
        Reason: null,
        Movies: [],
        Series: [{ GapId: 'filmography:series:2', Title: 'A Missing Show', Year: 2010, Role: null, Kind: 'Series', TmdbId: 1396, ImageUrl: null, Upcoming: false }]
    };
    const harnessPath = buildWebUiHarness(PERSON_ITEM, missing);
    await openPersonPage(page, harnessPath);

    const tmdbLink = page.locator('#mtgPersonMissing .mtgTmdbLink');
    await expect(tmdbLink).toHaveCount(1);
    await expect(tmdbLink).toHaveAttribute('href', 'https://www.themoviedb.org/tv/1396');
});

test('an administrator with no arr configured gets an Add to TODO fallback instead of Send', async ({ page }) => {
    const missing = {
        CanSendMovies: false,
        CanSendSeries: false,
        CanTodo: true,
        Reason: null,
        Movies: [{ GapId: 'filmography:movie:1', Title: 'A Missing Movie', Year: 2001, Role: null, Kind: 'Movie', TmdbId: 603, ImageUrl: null, Upcoming: false }],
        Series: []
    };
    const harnessPath = buildWebUiHarness(PERSON_ITEM, missing, null, 1);
    await openPersonPage(page, harnessPath);

    await expect(page.locator('#mtgPersonMissing .mtgSendButton')).toHaveCount(0);
    const todoBtn = page.locator('#mtgPersonMissing .mtgTodoButton');
    await expect(todoBtn).toBeVisible();
    await todoBtn.click();
    await expect(todoBtn).toHaveText('Added to TODO');
    await expect(todoBtn).toBeDisabled();

    const todoUrl = await page.evaluate(() => window.__lastTodoUrl);
    expect(todoUrl).toContain('Person/person-1/Todo');
    expect(todoUrl).toContain('gapId=filmography%3Amovie%3A1');
});

test('a failed Add to TODO re-enables the button', async ({ page }) => {
    const missing = {
        CanSendMovies: false,
        CanSendSeries: false,
        CanTodo: true,
        Reason: null,
        Movies: [{ GapId: 'filmography:movie:1', Title: 'A Missing Movie', Year: 2001, Role: null, Kind: 'Movie', TmdbId: 603, ImageUrl: null, Upcoming: false }],
        Series: []
    };
    const harnessPath = buildWebUiHarness(PERSON_ITEM, missing, null, 0);
    await openPersonPage(page, harnessPath);

    const todoBtn = page.locator('#mtgPersonMissing .mtgTodoButton');
    await todoBtn.click();
    await expect(todoBtn).toHaveText('Add to TODO');
    await expect(todoBtn).toBeEnabled();
});

test('shows the reason instead of a list when the person cannot be looked up', async ({ page }) => {
    const missing = { CanSendMovies: false, CanSendSeries: false, Reason: 'TMDB has no record for this person right now.', Movies: [], Series: [] };
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
    const harnessPath = buildWebUiHarness(movieItem, { CanSendMovies: true, CanSendSeries: false, Reason: null, Movies: [], Series: [] });
    await openPersonPage(page, harnessPath);
    await page.evaluate(() => { window.location.hash = '#/details?id=movie-1'; });
    await page.evaluate(() => document.querySelector('.page').dispatchEvent(new Event('viewshow', { bubbles: true })));
    await page.waitForTimeout(200);

    await expect(page.locator('#mtgPersonMissing')).toHaveCount(0);
    const errors = await page.evaluate(() => window.__uiTestErrors);
    expect(errors).toEqual([]);
});
