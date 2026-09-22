// Want to watch: a bookmark on every card the surfaces render, which puts the title on the signed-in
// user's own list and takes it off again, the same control in the detail dialog, and a home row of what is
// still on the list. The cards say whether their title is on the list (OnList) and whether the caller may
// keep one (CanTodo); both come from the server.
const { test, expect } = require('@playwright/test');
const { buildWebUiHarness, buildWebUiHomeHarness } = require('./support/webui-harness');

const PERSON_ITEM = { Id: 'person-1', Name: 'Some Actor', Type: 'Person' };
const ARTIST_ITEM = { Id: 'artist-1', Name: 'A Band', Type: 'MusicArtist' };

const movie = (id, extra) => Object.assign({ GapId: 'filmography:movie:' + id, Title: 'Movie ' + id, Year: 1999, Role: null, Kind: 'Movie', TmdbId: id, ImageUrl: null, Upcoming: false, OnList: false }, extra);

const person = (extra) => Object.assign({
    CanTodo: true,
    Reason: null,
    Movies: [movie(1), movie(2, { OnList: true })],
    Series: []
}, extra);

async function openPersonPage(page, harnessPath) {
    await page.goto('file://' + harnessPath);
    await page.evaluate(() => { window.location.hash = '#/details?id=person-1'; });
    await page.evaluate(() => {
        document.querySelector('.page').dispatchEvent(new Event('viewshow', { bubbles: true }));
    });
}

async function openHomePage(page, harnessPath) {
    await page.goto('file://' + harnessPath);
    await page.evaluate(() => {
        document.querySelector('.page').dispatchEvent(new Event('viewshow', { bubbles: true }));
    });
    await page.evaluate(() => {
        const sections = document.querySelector('#homeTab .sections');
        const section = document.createElement('div');
        section.className = 'verticalSection';
        section.textContent = 'Continue Watching';
        sections.appendChild(section);
    });
}

const card = (page, gapId) => page.locator('.mtgCard[data-gapid="' + gapId + '"]');
const bookmark = (page, gapId) => card(page, gapId).locator('.mtgWant');
const todoUrls = (page) => page.evaluate(() => window.__lastTodoUrl);

test('every card carries a bookmark that says whether the title is on the list', async ({ page }) => {
    await openPersonPage(page, buildWebUiHarness(PERSON_ITEM, person()));

    await expect(bookmark(page, 'filmography:movie:1')).toHaveAttribute('aria-pressed', 'false');
    await expect(bookmark(page, 'filmography:movie:1')).toHaveAttribute('title', 'Want to watch');
    await expect(bookmark(page, 'filmography:movie:2')).toHaveAttribute('aria-pressed', 'true');
    await expect(bookmark(page, 'filmography:movie:2')).toHaveAttribute('title', 'Remove from your list');
});

test('there is no bookmark, and no dialog button, when the caller cannot keep a list', async ({ page }) => {
    await openPersonPage(page, buildWebUiHarness(PERSON_ITEM, person({ CanTodo: false })));

    await expect(page.locator('.mtgWant')).toHaveCount(0);
    await card(page, 'filmography:movie:1').click();
    await expect(page.locator('.mtgDialog .mtgWantButton')).toHaveCount(0);
});

test('the bookmark is in the upper right corner of the card image, and nothing is fixed', async ({ page }) => {
    await openPersonPage(page, buildWebUiHarness(PERSON_ITEM, person()));

    const positions = await page.locator('#mtgPersonMissing').evaluate((el) => [el, ...el.querySelectorAll('*')].map((e) => getComputedStyle(e).position));
    expect(positions).not.toContain('fixed');

    const image = await card(page, 'filmography:movie:1').locator('.cardScalable').boundingBox();
    const mark = await bookmark(page, 'filmography:movie:1').boundingBox();
    expect(mark.x + mark.width).toBeLessThanOrEqual(image.x + image.width);
    expect(mark.x + mark.width).toBeGreaterThan(image.x + image.width - mark.width);
    expect(mark.y).toBeGreaterThanOrEqual(image.y);
    expect(mark.y).toBeLessThan(image.y + mark.height);
    expect(mark.width).toBeGreaterThanOrEqual(32);
});

test('the dialog carries the same bookmark on its poster, and it is the same state', async ({ page }) => {
    await openPersonPage(page, buildWebUiHarness(PERSON_ITEM, person(), null, 1));

    await card(page, 'filmography:movie:1').click();
    const mark = page.locator('.mtgDialog .mtgDialogPoster .mtgWant');
    await expect(mark).toHaveAttribute('aria-pressed', 'false');
    await mark.click();

    await expect(mark).toHaveAttribute('aria-pressed', 'true');
    await expect(page.locator('.mtgDialog .mtgWantButton')).toHaveText('On your list');
    await expect(bookmark(page, 'filmography:movie:1')).toHaveAttribute('aria-pressed', 'true');
});

test('clicking the bookmark puts the title on the list and does not open the dialog', async ({ page }) => {
    await openPersonPage(page, buildWebUiHarness(PERSON_ITEM, person(), null, 1));

    await bookmark(page, 'filmography:movie:1').click();

    await expect(bookmark(page, 'filmography:movie:1')).toHaveAttribute('aria-pressed', 'true');
    await expect(page.locator('.mtgDialogBackdrop.mtgDialogOpen')).toHaveCount(0);
    const url = await todoUrls(page);
    expect(url).toContain('Person/person-1/Todo');
    expect(url).not.toContain('Todo/Remove');
    expect(url).toContain('gapId=filmography%3Amovie%3A1');
});

test('clicking it again takes the title off the list, by the same gap', async ({ page }) => {
    await openPersonPage(page, buildWebUiHarness(PERSON_ITEM, person(), null, 1));

    await bookmark(page, 'filmography:movie:2').click();

    await expect(bookmark(page, 'filmography:movie:2')).toHaveAttribute('aria-pressed', 'false');
    const url = await todoUrls(page);
    expect(url).toContain('Person/person-1/Todo/Remove');
    expect(url).toContain('gapId=filmography%3Amovie%3A2');
});

test('Enter and Space on the bookmark act on it, not on the card', async ({ page }) => {
    await openPersonPage(page, buildWebUiHarness(PERSON_ITEM, person(), null, 1));

    await bookmark(page, 'filmography:movie:1').focus();
    await page.keyboard.press('Enter');
    await expect(bookmark(page, 'filmography:movie:1')).toHaveAttribute('aria-pressed', 'true');
    await page.keyboard.press('Space');
    await expect(bookmark(page, 'filmography:movie:1')).toHaveAttribute('aria-pressed', 'false');

    await expect(page.locator('.mtgDialogBackdrop.mtgDialogOpen')).toHaveCount(0);
});

test('a failed update leaves the bookmark as it was and usable', async ({ page }) => {
    await openPersonPage(page, buildWebUiHarness(PERSON_ITEM, person(), null, 'fail'));

    await bookmark(page, 'filmography:movie:1').click();

    await expect(bookmark(page, 'filmography:movie:1')).toHaveAttribute('aria-pressed', 'false');
    await expect(bookmark(page, 'filmography:movie:1')).toBeEnabled();
});

test('the dialog button and the bookmark are one state', async ({ page }) => {
    await openPersonPage(page, buildWebUiHarness(PERSON_ITEM, person(), null, 1));

    await card(page, 'filmography:movie:1').click();
    const button = page.locator('.mtgDialog .mtgWantButton');
    await expect(button).toHaveText('Want to watch');
    await button.click();

    await expect(button).toHaveText('On your list');
    await expect(bookmark(page, 'filmography:movie:1')).toHaveAttribute('aria-pressed', 'true');

    await button.click();
    await expect(button).toHaveText('Want to watch');
    await expect(bookmark(page, 'filmography:movie:1')).toHaveAttribute('aria-pressed', 'false');
});

test('the dialog opens already showing a title that is on the list', async ({ page }) => {
    await openPersonPage(page, buildWebUiHarness(PERSON_ITEM, person()));

    await card(page, 'filmography:movie:2').click();

    await expect(page.locator('.mtgDialog .mtgWantButton')).toHaveText('On your list');
});

test('an album says want to listen', async ({ page }) => {
    const works = {
        Kind: 'MusicAlbum',
        CanTodo: true,
        Reason: null,
        Works: [{ GapId: 'discography:x:y', Title: 'An Album', Year: 1994, Kind: 'MusicAlbum', Creator: 'A Band', ImageUrl: null, Upcoming: false, OnList: false, Links: [] }]
    };
    await page.goto('file://' + buildWebUiHarness(ARTIST_ITEM, works, null, 1));
    await page.evaluate(() => { window.location.hash = '#/details?id=artist-1'; });
    await page.evaluate(() => { document.querySelector('.page').dispatchEvent(new Event('viewshow', { bubbles: true })); });

    await expect(bookmark(page, 'discography:x:y')).toHaveAttribute('title', 'Want to listen');
    await bookmark(page, 'discography:x:y').click();

    await expect(bookmark(page, 'discography:x:y')).toHaveAttribute('aria-pressed', 'true');
    expect(await todoUrls(page)).toContain('Item/artist-1/Works/Todo');
});

const wantedRow = (extra) => Object.assign({
    Titles: [
        movie(7, { GapId: 'filmography:movie:7', OnList: true }),
        movie(8, { GapId: 'recommendation:movie:8', OnList: true })
    ]
}, extra);

test('the home screen shows the list as a Want to watch row, ahead of Discover', async ({ page }) => {
    const discover = { CanTodo: true, Titles: [movie(1, { GapId: 'recommendation:movie:1', Because: 'Because you have Fargo' })] };
    await openHomePage(page, buildWebUiHomeHarness(discover, null, 1, undefined, undefined, wantedRow()));

    const row = page.locator('#mtgHomeWanted');
    await expect(row).toBeVisible({ timeout: 2000 });
    await expect(row.getByText('Want to watch')).toBeVisible();
    await expect(page.locator('#mtgHomeDiscover')).toBeVisible();
    const order = await page.evaluate(() => {
        const sections = document.querySelector('#homeTab .sections');
        return Array.prototype.indexOf.call(sections.children, document.getElementById('mtgHomeWanted'))
            < Array.prototype.indexOf.call(sections.children, document.getElementById('mtgHomeDiscover'));
    });
    expect(order).toBe(true);
    await expect(row.locator('.mtgCard')).toHaveCount(2);
});

test('the wanted row is drawn without the Discover row when only the list is on', async ({ page }) => {
    await openHomePage(page, buildWebUiHomeHarness(null, null, 1, undefined, undefined, wantedRow()));

    await expect(page.locator('#mtgHomeWanted')).toBeVisible({ timeout: 2000 });
    await expect(page.locator('#mtgHomeDiscover')).toHaveCount(0);
});

test('taking a title off the wanted row removes its card, and the row when it was the last', async ({ page }) => {
    await openHomePage(page, buildWebUiHomeHarness(null, null, 1, undefined, undefined, wantedRow()));
    await expect(page.locator('#mtgHomeWanted .mtgCard')).toHaveCount(2);

    await bookmark(page, 'filmography:movie:7').click();

    await expect(page.locator('#mtgHomeWanted .mtgCard')).toHaveCount(1);
    expect(await todoUrls(page)).toContain('Home/Wanted/Remove');
    expect(await todoUrls(page)).toContain('gapId=filmography%3Amovie%3A7');

    await bookmark(page, 'recommendation:movie:8').click();
    await expect(page.locator('#mtgHomeWanted')).toHaveCount(0);
});

test('a title removed from the wanted row while its dialog is open leaves the dialog saying so', async ({ page }) => {
    await openHomePage(page, buildWebUiHomeHarness(null, null, 1, undefined, undefined, wantedRow()));

    await card(page, 'filmography:movie:7').click();
    const button = page.locator('.mtgDialog .mtgWantButton');
    await expect(button).toHaveText('On your list');
    await button.click();

    await expect(button).toHaveText('Removed from your list');
    await expect(button).toBeDisabled();
    await expect(page.locator('#mtgHomeWanted .mtgCard[data-gapid="filmography:movie:7"]')).toHaveCount(0);
});

test('no wanted row when want to watch is off (the endpoint 404s), and no script errors', async ({ page }) => {
    await openHomePage(page, buildWebUiHomeHarness(null));
    await page.waitForTimeout(400);

    await expect(page.locator('#mtgHomeWanted')).toHaveCount(0);
    expect(await page.evaluate(() => window.__uiTestErrors)).toEqual([]);
});

test('an empty list draws no row', async ({ page }) => {
    await openHomePage(page, buildWebUiHomeHarness(null, null, 1, undefined, undefined, { Titles: [] }));
    await page.waitForTimeout(400);

    await expect(page.locator('#mtgHomeWanted')).toHaveCount(0);
});
