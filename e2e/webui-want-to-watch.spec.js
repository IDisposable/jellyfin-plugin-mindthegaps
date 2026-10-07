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

test('taking a title off the wanted row removes its card; the row stays for its title search', async ({ page }) => {
    await openHomePage(page, buildWebUiHomeHarness(null, null, 1, undefined, undefined, wantedRow()));
    await expect(page.locator('#mtgHomeWanted .mtgCard')).toHaveCount(2);

    await bookmark(page, 'filmography:movie:7').click();

    await expect(page.locator('#mtgHomeWanted .mtgCard')).toHaveCount(1);
    expect(await todoUrls(page)).toContain('Home/Wanted/Remove');
    expect(await todoUrls(page)).toContain('gapId=filmography%3Amovie%3A7');

    await bookmark(page, 'recommendation:movie:8').click();
    await expect(page.locator('#mtgHomeWanted .mtgCard')).toHaveCount(0);
    await expect(page.locator('#mtgHomeWanted')).toBeVisible();
    await expect(page.locator('#mtgHomeWanted .mtgSearchInput')).toBeVisible();
});

// An owned title (from the user's want-to-watch playlist) carries its library item: the card shows the
// library's own poster, opens the item's page rather than the TMDB dialog, and its bookmark takes it off the
// playlist by item id.
const ownedRow = () => ({
    Titles: [
        movie(9, { GapId: 'owned:item9', Title: 'Owned Movie', OnList: true, ItemId: 'item9' }),
        movie(7, { GapId: 'filmography:movie:7', OnList: true })
    ]
});

test('an owned title on the wanted row shows the library poster and opens its library page', async ({ page }) => {
    await openHomePage(page, buildWebUiHomeHarness(null, null, 1, undefined, undefined, ownedRow()));
    await expect(page.locator('#mtgHomeWanted .mtgCard')).toHaveCount(2);

    const image = card(page, 'owned:item9').locator('.cardImageContainer');
    await expect(image).toHaveCSS('background-image', /Items\/item9\/Images\/Primary/);

    await card(page, 'owned:item9').click();
    await expect(page.locator('.mtgDialog')).toHaveCount(0);
    expect(await page.evaluate(() => window.__shownItems)).toEqual(['item9@test-server']);
});

test('an owned title opens its library page from the keyboard too', async ({ page }) => {
    await openHomePage(page, buildWebUiHomeHarness(null, null, 1, undefined, undefined, ownedRow()));

    await card(page, 'owned:item9').focus();
    await page.keyboard.press('Enter');
    await expect(page.locator('.mtgDialog')).toHaveCount(0);
    expect(await page.evaluate(() => window.__shownItems)).toEqual(['item9@test-server']);
});

test('taking an owned title off the wanted row removes it by item id', async ({ page }) => {
    await openHomePage(page, buildWebUiHomeHarness(null, null, 1, undefined, undefined, ownedRow()));

    await bookmark(page, 'owned:item9').click();

    await expect(page.locator('#mtgHomeWanted .mtgCard')).toHaveCount(1);
    expect(await todoUrls(page)).toContain('Home/Wanted/Remove?itemId=item9');
    await expect(card(page, 'filmography:movie:7')).toHaveCount(1);
});

// jellyfin-web only resumes its cached home view on a return, so the plugin asks for the row again itself.
const returnHome = (page) => page.evaluate(() => {
    document.querySelector('.page').dispatchEvent(new Event('viewshow', { bubbles: true }));
});

test('returning to the cached home shows a title added since, keeping focus on the same card', async ({ page }) => {
    await openHomePage(page, buildWebUiHomeHarness(null, null, 1, undefined, undefined, wantedRow()));
    await expect(page.locator('#mtgHomeWanted .mtgCard')).toHaveCount(2);
    await card(page, 'recommendation:movie:8').focus();

    await page.evaluate(() => {
        __WANTED_RESULT__ = { Titles: [{ GapId: 'owned:item9', Title: 'Bookmarked Since', Kind: 'Movie', TmdbId: 9, OnList: true, ItemId: 'item9' }].concat(__WANTED_RESULT__.Titles) };
    });
    await returnHome(page);

    await expect(page.locator('#mtgHomeWanted .mtgCard')).toHaveCount(3);
    await expect(page.locator('#mtgHomeWanted .mtgCard').first()).toHaveAttribute('data-gapid', 'owned:item9');
    expect(await page.evaluate(() => document.activeElement.getAttribute('data-gapid'))).toBe('recommendation:movie:8');
});

test('returning to the cached home drops a title removed since, moving focus to the first card', async ({ page }) => {
    await openHomePage(page, buildWebUiHomeHarness(null, null, 1, undefined, undefined, wantedRow()));
    await card(page, 'filmography:movie:7').focus();

    await page.evaluate(() => { __WANTED_RESULT__ = { Titles: [__WANTED_RESULT__.Titles[1]] }; });
    await returnHome(page);

    await expect(page.locator('#mtgHomeWanted .mtgCard')).toHaveCount(1);
    await expect(card(page, 'filmography:movie:7')).toHaveCount(0);
    expect(await page.evaluate(() => document.activeElement.getAttribute('data-gapid'))).toBe('recommendation:movie:8');
});

test('returning to the cached home leaves an unchanged row alone', async ({ page }) => {
    await openHomePage(page, buildWebUiHomeHarness(null, null, 1, undefined, undefined, wantedRow()));
    await expect(page.locator('#mtgHomeWanted .mtgCard')).toHaveCount(2);
    await page.evaluate(() => { document.getElementById('mtgHomeWanted').__marker = true; });

    await returnHome(page);
    await page.waitForTimeout(300);

    expect(await page.evaluate(() => document.getElementById('mtgHomeWanted').__marker === true)).toBe(true);
});

test('returning to the cached home keeps focus and the caret in the search box when the row changes', async ({ page }) => {
    await openHomePage(page, buildWebUiHomeHarness(null, null, 1, undefined, undefined, wantedRow()));
    const input = page.locator('#mtgHomeWanted .mtgSearchInput');
    await input.fill('matrix');
    await input.evaluate((el) => { el.setSelectionRange(2, 4); });

    await page.evaluate(() => { __WANTED_RESULT__ = { Titles: [__WANTED_RESULT__.Titles[1]] }; });
    await returnHome(page);

    await expect(page.locator('#mtgHomeWanted .mtgCard')).toHaveCount(1);
    expect(await page.evaluate(() => {
        var el = document.activeElement;
        return [el.className, el.value, el.selectionStart, el.selectionEnd];
    })).toEqual(['mtgSearchInput', 'matrix', 2, 4]);
});

// jellyfin-web's focusManager (12.1, main bundle) moves a remote's D-pad focus only between elements matching
// this selector. A card that does not match is unreachable: the D-pad lands on its bookmark button instead.
const JELLYFIN_FOCUSABLE = ['INPUT', 'TEXTAREA', 'SELECT', 'BUTTON', 'A']
    .map((t) => (t === 'INPUT' ? t + ':not([type="range"]):not([type="file"])' : t) + ':not([tabindex="-1"]):not(:disabled)')
    .join(',') + ',.focusable';

test('every card on the wanted and Discover rows is reachable by jellyfin-web remote navigation', async ({ page }) => {
    const discover = { CanTodo: true, Titles: [movie(1, { GapId: 'recommendation:movie:1' })] };
    await openHomePage(page, buildWebUiHomeHarness(discover, null, 1, undefined, undefined, ownedRow()));
    await expect(page.locator('#mtgHomeWanted .mtgCard')).toHaveCount(2);
    await expect(page.locator('#mtgHomeDiscover .mtgCard')).toHaveCount(1);

    const unreachable = await page.locator('.mtgCard').evaluateAll((cards, selector) => cards.filter((c) => !c.matches(selector)).map((c) => c.getAttribute('data-gapid')), JELLYFIN_FOCUSABLE);
    expect(unreachable).toEqual([]);
});

// On a TV, jellyfin-web's own cards grow when focused (.card.show-animation:focus > .cardBox, scale 1.07) rather
// than drawing an outline; the plugin's cards take the same classes, copied from a stock card on the page.
// jellyfin-web 12.1's own rules for a card's focus (main bundle CSS), which the harness otherwise lacks.
const JELLYFIN_CARD_CSS = '.card{outline:none!important}.card.show-animation:focus>.cardBox{transform:scale(1.07)}';
const addJellyfinCardCss = (page) => page.evaluate((css) => {
    const style = document.createElement('style');
    style.textContent = css;
    document.head.appendChild(style);
}, JELLYFIN_CARD_CSS);

const tvHome = async (page, stockClasses) => {
    await page.goto('file://' + buildWebUiHomeHarness(null, null, 1, undefined, undefined, wantedRow()));
    await page.evaluate((cls) => {
        document.documentElement.classList.add('layout-tv');
        const stock = document.createElement('div');
        stock.className = 'verticalSection';
        stock.innerHTML = '<button class="card portraitCard ' + cls + '"><div class="cardBox"></div></button>';
        document.querySelector('#homeTab .sections').appendChild(stock);
        document.querySelector('.page').dispatchEvent(new Event('viewshow', { bubbles: true }));
    }, stockClasses);
    await expect(page.locator('#mtgHomeWanted .mtgCard')).toHaveCount(2);
};

test('on a TV a focused card grows like a stock card, with no outline', async ({ page }) => {
    await tvHome(page, 'show-focus show-animation');
    await addJellyfinCardCss(page);

    const mtg = card(page, 'filmography:movie:7');
    await expect(mtg).toHaveClass(/\bshow-focus\b/);
    await expect(mtg).toHaveClass(/\bshow-animation\b/);
    await mtg.focus();
    await expect(mtg.locator('> .cardBox')).toHaveCSS('transform', 'matrix(1.07, 0, 0, 1.07, 0, 0)');
    await expect(mtg).toHaveCSS('outline-style', 'none');
});

test('on a TV whose stock cards do not grow, the plugin cards do not either', async ({ page }) => {
    await tvHome(page, 'show-focus');

    await expect(card(page, 'filmography:movie:7')).toHaveClass(/\bshow-focus\b/);
    await expect(card(page, 'filmography:movie:7')).not.toHaveClass(/\bshow-animation\b/);
});

test('off a TV a focused card keeps its outline and takes no TV focus classes', async ({ page }) => {
    await openHomePage(page, buildWebUiHomeHarness(null, null, 1, undefined, undefined, wantedRow()));
    await addJellyfinCardCss(page);
    const mtg = card(page, 'filmography:movie:7');
    await expect(mtg).not.toHaveClass(/\bshow-focus\b/);
    await mtg.focus();
    await expect(mtg).toHaveCSS('outline-style', 'solid');
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

test('an empty list still draws the row, for its title search', async ({ page }) => {
    await openHomePage(page, buildWebUiHomeHarness(null, null, 1, undefined, undefined, { Titles: [] }));
    await page.waitForTimeout(400);

    await expect(page.locator('#mtgHomeWanted')).toBeVisible();
    await expect(page.locator('#mtgHomeWanted .mtgCard')).toHaveCount(0);
    await expect(page.locator('#mtgHomeWanted .mtgSearchInput')).toBeVisible();
});

test('typing in the wanted row search finds a title and adds it to the list', async ({ page }) => {
    const result = [Object.assign(movie(50), { GapId: 'watchlistsearch:movie:50', Title: 'Wanted Movie', OnList: false })];
    await openHomePage(page, buildWebUiHomeHarness(null, null, 1, undefined, undefined, { Titles: [] }, result));

    await page.locator('#mtgHomeWanted .mtgSearchInput').fill('wanted');
    await expect(page.locator('#mtgSearchResults .mtgCard[data-gapid="watchlistsearch:movie:50"]')).toBeVisible({ timeout: 2000 });
    expect(await page.evaluate(() => window.__lastSearchUrl)).toContain('kind=Movie');
    expect(await page.evaluate(() => window.__lastSearchUrl)).toContain('q=wanted');

    await bookmark(page, 'watchlistsearch:movie:50').click();

    // The add posts kind/tmdbId, not a gapId: a search result has no persisted gap of its own to
    // rehydrate by id (wantParams' Search branch). The mock is stateless, so the row's own reload after
    // the add re-fetches the same canned (unowned) result rather than a genuinely updated one; the
    // request shape is the contract this pins, not the mock's replayed response.
    await expect.poll(() => todoUrls(page)).toContain('Home/Search/Todo');
    expect(await todoUrls(page)).toContain('kind=Movie');
    expect(await todoUrls(page)).toContain('tmdbId=50');
});

test('an empty search box shows no results box, and clearing the query hides it again', async ({ page }) => {
    const result = [Object.assign(movie(51), { GapId: 'watchlistsearch:movie:51', Title: 'Something' })];
    await openHomePage(page, buildWebUiHomeHarness(null, null, 1, undefined, undefined, { Titles: [] }, result));

    await expect(page.locator('#mtgSearchResults')).toHaveCount(0);

    const input = page.locator('#mtgHomeWanted .mtgSearchInput');
    await input.fill('something');
    await expect(page.locator('#mtgSearchResults')).toBeVisible({ timeout: 2000 });

    await input.fill('');
    await expect(page.locator('#mtgSearchResults')).toHaveCount(0);
});

test('no matches renders the typed query as plain text, never as markup', async ({ page }) => {
    await openHomePage(page, buildWebUiHomeHarness(null, null, 1, undefined, undefined, { Titles: [] }, []));

    const hostile = '<img src=x onerror=alert(1)>';
    await page.locator('#mtgHomeWanted .mtgSearchInput').fill(hostile);
    await expect(page.locator('#mtgSearchResults .mtgSearchNote')).toBeVisible({ timeout: 2000 });

    expect(await page.locator('#mtgSearchResults .mtgSearchNote').textContent()).toContain(hostile);
    await expect(page.locator('#mtgSearchResults img')).toHaveCount(0);
});

// An old TV browser has no Element.closest; the script adds its own, which the wanted row's refresh relies on to
// tell its own cards from the search results and to find the card that had focus.
test('without a native Element.closest, the script supplies one and the wanted row still refreshes', async ({ page }) => {
    await page.addInitScript(() => { delete Element.prototype.closest; });
    await openHomePage(page, buildWebUiHomeHarness(null, null, 1, undefined, undefined, wantedRow()));
    expect(await page.evaluate(() => String(Element.prototype.closest).indexOf('[native code]') === -1)).toBe(true);
    await card(page, 'filmography:movie:7').focus();

    await page.evaluate(() => { __WANTED_RESULT__ = { Titles: [__WANTED_RESULT__.Titles[1]] }; });
    await returnHome(page);

    await expect(page.locator('#mtgHomeWanted .mtgCard')).toHaveCount(1);
    expect(await page.evaluate(() => document.activeElement.getAttribute('data-gapid'))).toBe('recommendation:movie:8');
    expect(await page.evaluate(() => window.__uiTestErrors)).toEqual([]);
});

test('a browser with a native Element.closest keeps its own', async ({ page }) => {
    await openHomePage(page, buildWebUiHomeHarness(null, null, 1, undefined, undefined, wantedRow()));
    expect(await page.evaluate(() => String(Element.prototype.closest).indexOf('[native code]') !== -1)).toBe(true);
});
