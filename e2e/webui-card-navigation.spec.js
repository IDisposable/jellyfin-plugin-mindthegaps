// Arrow-key navigation between cards, the hand-rolled substitute for jellyfin-web's own
// focusManager.moveUp/Down/Left/Right (not reachable from an externally injected script; see the detail
// dialog's own header comment in mindthegaps.webui.js for why not). Left/Right is plain DOM-sibling order,
// true regardless of layout; Up/Down needs real on-screen geometry, so these tests force a deterministic
// two-column layout via injected CSS (the harness carries none of jellyfin-web's real card CSS) rather
// than relying on the default viewport width to happen to wrap the same way.
const { test, expect } = require('@playwright/test');
const { buildWebUiHarness } = require('./support/webui-harness');

const PERSON_ITEM = { Id: 'person-1', Name: 'Some Actor', Type: 'Person' };
const MOVIE_ITEM = { Id: 'movie-1', Name: 'A Movie', Type: 'Movie' };

function movie(n) {
    return { GapId: 'filmography:movie:' + n, Title: 'Movie ' + n, Year: 2000 + n, Role: null, Kind: 'Movie', TmdbId: n, ImageUrl: null, Upcoming: false };
}

async function openPersonPage(page, harnessPath) {
    await page.goto('file://' + harnessPath);
    await page.evaluate(() => { window.location.hash = '#/details?id=person-1'; });
    await page.evaluate(() => {
        document.querySelector('.page').dispatchEvent(new Event('viewshow', { bubbles: true }));
    });
}

async function openItemPage(page, harnessPath) {
    await page.goto('file://' + harnessPath);
    await page.evaluate(() => { window.location.hash = '#/details?id=movie-1'; });
    await page.evaluate(() => {
        document.querySelector('.page').dispatchEvent(new Event('viewshow', { bubbles: true }));
    });
}

// Forces two cards per row: the harness carries none of jellyfin-web's real card CSS, so without this a
// plain <div> lays out full-width, one per row, and Up/Down would have nothing meaningful to navigate.
async function forceTwoColumnGrid(page) {
    await page.addStyleTag({ content: '#mtgPersonMissing .itemsContainer{width:260px} .mtgCard{display:inline-block;width:120px;vertical-align:top}' });
}

function gapIdOfActive(page) {
    return page.evaluate(() => document.activeElement.getAttribute('data-gapid'));
}

test('ArrowRight/ArrowLeft move between cards in a grid, and stop at the ends', async ({ page }) => {
    const missing = {
        CanSendMovies: false, CanSendSeries: false, Reason: null,
        Movies: [movie(1), movie(2), movie(3)], Series: []
    };
    const harnessPath = buildWebUiHarness(PERSON_ITEM, missing);
    await openPersonPage(page, harnessPath);

    await page.locator('[data-gapid="filmography:movie:1"]').focus();
    await page.keyboard.press('ArrowRight');
    expect(await gapIdOfActive(page)).toBe('filmography:movie:2');
    await page.keyboard.press('ArrowRight');
    expect(await gapIdOfActive(page)).toBe('filmography:movie:3');

    // No fourth card: ArrowRight at the last one is a no-op, not an error, and focus stays put.
    await page.keyboard.press('ArrowRight');
    expect(await gapIdOfActive(page)).toBe('filmography:movie:3');

    await page.keyboard.press('ArrowLeft');
    expect(await gapIdOfActive(page)).toBe('filmography:movie:2');
    await page.keyboard.press('ArrowLeft');
    expect(await gapIdOfActive(page)).toBe('filmography:movie:1');
    await page.keyboard.press('ArrowLeft');
    expect(await gapIdOfActive(page)).toBe('filmography:movie:1');
});

test('ArrowDown/ArrowUp move to the nearest card in the row below/above a wrapping grid', async ({ page }) => {
    const missing = {
        CanSendMovies: false, CanSendSeries: false, Reason: null,
        Movies: [movie(1), movie(2), movie(3), movie(4)], Series: []
    };
    const harnessPath = buildWebUiHarness(PERSON_ITEM, missing);
    await openPersonPage(page, harnessPath);
    await forceTwoColumnGrid(page);

    // Row 1: movie 1, movie 2. Row 2: movie 3, movie 4.
    await page.locator('[data-gapid="filmography:movie:1"]').focus();
    await page.keyboard.press('ArrowDown');
    expect(await gapIdOfActive(page)).toBe('filmography:movie:3');

    await page.locator('[data-gapid="filmography:movie:2"]').focus();
    await page.keyboard.press('ArrowDown');
    expect(await gapIdOfActive(page)).toBe('filmography:movie:4');

    await page.keyboard.press('ArrowUp');
    expect(await gapIdOfActive(page)).toBe('filmography:movie:2');
});

test('ArrowDown at the last row and ArrowUp at the first row are no-ops', async ({ page }) => {
    const missing = {
        CanSendMovies: false, CanSendSeries: false, Reason: null,
        Movies: [movie(1), movie(2), movie(3)], Series: []
    };
    const harnessPath = buildWebUiHarness(PERSON_ITEM, missing);
    await openPersonPage(page, harnessPath);
    await forceTwoColumnGrid(page);

    await page.locator('[data-gapid="filmography:movie:1"]').focus();
    await page.keyboard.press('ArrowUp');
    expect(await gapIdOfActive(page)).toBe('filmography:movie:1');

    await page.locator('[data-gapid="filmography:movie:3"]').focus();
    await page.keyboard.press('ArrowDown');
    expect(await gapIdOfActive(page)).toBe('filmography:movie:3');
});

test('the two grids (Movies and Shows) navigate independently, since they are separate containers', async ({ page }) => {
    const missing = {
        CanSendMovies: false, CanSendSeries: false, Reason: null,
        Movies: [movie(1)],
        Series: [{ GapId: 'filmography:series:1', Title: 'A Show', Year: 2010, Role: null, Kind: 'Series', TmdbId: 9, ImageUrl: null, Upcoming: false }]
    };
    const harnessPath = buildWebUiHarness(PERSON_ITEM, missing);
    await openPersonPage(page, harnessPath);

    // A single card in each of two separate grids: ArrowRight has no sibling to move to in either one.
    await page.locator('[data-gapid="filmography:movie:1"]').focus();
    await page.keyboard.press('ArrowRight');
    expect(await gapIdOfActive(page)).toBe('filmography:movie:1');

    await page.locator('[data-gapid="filmography:series:1"]').focus();
    await page.keyboard.press('ArrowLeft');
    expect(await gapIdOfActive(page)).toBe('filmography:series:1');
});

test('ArrowRight/ArrowLeft move between cards in a horizontal scroller (the item/home rows)', async ({ page }) => {
    const related = {
        CanSend: false, Reason: null,
        Titles: [
            { GapId: 'recommendation:movie:1', Title: 'A', Year: 2001, Kind: 'Movie', TmdbId: 1, ImageUrl: null, Upcoming: false },
            { GapId: 'recommendation:movie:2', Title: 'B', Year: 2002, Kind: 'Movie', TmdbId: 2, ImageUrl: null, Upcoming: false }
        ]
    };
    const harnessPath = buildWebUiHarness(MOVIE_ITEM, related);
    await openItemPage(page, harnessPath);

    await page.locator('[data-gapid="recommendation:movie:1"]').focus();
    await page.keyboard.press('ArrowRight');
    expect(await gapIdOfActive(page)).toBe('recommendation:movie:2');
    await page.keyboard.press('ArrowLeft');
    expect(await gapIdOfActive(page)).toBe('recommendation:movie:1');
});

test('ArrowUp/ArrowDown are a no-op in a single-row scroller', async ({ page }) => {
    const related = {
        CanSend: false, Reason: null,
        Titles: [
            { GapId: 'recommendation:movie:1', Title: 'A', Year: 2001, Kind: 'Movie', TmdbId: 1, ImageUrl: null, Upcoming: false },
            { GapId: 'recommendation:movie:2', Title: 'B', Year: 2002, Kind: 'Movie', TmdbId: 2, ImageUrl: null, Upcoming: false }
        ]
    };
    const harnessPath = buildWebUiHarness(MOVIE_ITEM, related);
    await openItemPage(page, harnessPath);
    // The harness carries none of jellyfin-web's real scroller CSS (flex, side by side); without it a
    // plain <div> stacks full-width, one per row, which would make ArrowDown find a real "row below" and
    // defeat the point of this test.
    await page.addStyleTag({ content: '.scrollSlider{display:flex}' });

    await page.locator('[data-gapid="recommendation:movie:1"]').focus();
    await page.keyboard.press('ArrowDown');
    expect(await gapIdOfActive(page)).toBe('recommendation:movie:1');
    await page.keyboard.press('ArrowUp');
    expect(await gapIdOfActive(page)).toBe('recommendation:movie:1');
});
