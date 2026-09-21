// Drives the real mindthegaps.webui.js against a fake jellyfin-web Music Artist and Book detail page,
// the artist/book sibling of webui-item-page.spec.js. A work (an album or a book) has no TMDB id, so its
// dialog is built from the card's own data and links and makes no detail lookup; the only action is the
// administrator's Add to TODO.
const { test, expect } = require('@playwright/test');
const { buildWebUiHarness } = require('./support/webui-harness');

const ARTIST_ITEM = { Id: 'artist-1', Name: 'A Band', Type: 'MusicArtist' };
const BOOK_ITEM = { Id: 'book-1', Name: 'A Novel', Type: 'Book' };

const ALBUM = {
    GapId: 'discography:mbid:rg-1',
    Title: 'A Missing Album',
    Year: 1994,
    Kind: 'MusicAlbum',
    Creator: 'A Band',
    ImageUrl: 'https://example.com/cover.jpg',
    Upcoming: false,
    Links: [{ Name: 'MusicBrainz', Url: 'https://musicbrainz.org/release-group/rg-1' }]
};

const BOOK = {
    GapId: 'bibliography:OL1A:OL2W',
    Title: 'Another Novel',
    Year: 1987,
    Kind: 'Book',
    Creator: 'An Author',
    ImageUrl: null,
    Upcoming: false,
    Links: [{ Name: 'OpenLibrary', Url: 'https://openlibrary.org/works/OL2W' }]
};

async function openItemPage(page, harnessPath, itemId) {
    await page.goto('file://' + harnessPath);
    await page.evaluate((id) => { window.location.hash = '#/details?id=' + id; }, itemId);
    await page.evaluate(() => {
        document.querySelector('.page').dispatchEvent(new Event('viewshow', { bubbles: true }));
    });
}

async function openCardDialog(page, gapId) {
    await page.locator('[data-gapid="' + gapId + '"]').click();
    await expect(page.locator('.mtgDialogBackdrop')).toHaveClass(/mtgDialogOpen/);
}

test('an artist page lists the missing albums after similarCollapsible, in normal flow', async ({ page }) => {
    const works = { ItemId: 'artist-1', ItemName: 'A Band', Kind: 'MusicAlbum', CanTodo: false, Reason: null, Works: [ALBUM] };
    await openItemPage(page, buildWebUiHarness(ARTIST_ITEM, works), 'artist-1');

    const section = page.locator('#mtgWorksMissing');
    await expect(section).toBeVisible();
    await expect(section.getByText("Albums you don't have")).toBeVisible();
    await expect(section.getByText('A Missing Album')).toBeVisible();

    const positions = await section.evaluate((el) => [el, ...el.querySelectorAll('*')].map((e) => getComputedStyle(e).position));
    expect(positions).not.toContain('fixed');
    expect(positions).not.toContain('absolute');

    const order = await page.evaluate(() => {
        const parent = document.querySelector('.detailPageContent');
        return Array.prototype.indexOf.call(parent.children, document.getElementById('similarCollapsible'))
            < Array.prototype.indexOf.call(parent.children, document.getElementById('mtgWorksMissing'));
    });
    expect(order).toBe(true);
});

test('an album card is square and a book card is portrait', async ({ page }) => {
    const albums = { Kind: 'MusicAlbum', CanTodo: false, Reason: null, Works: [ALBUM] };
    await openItemPage(page, buildWebUiHarness(ARTIST_ITEM, albums), 'artist-1');
    await expect(page.locator('[data-gapid="' + ALBUM.GapId + '"]')).toHaveClass(/squareCard/);
    await expect(page.locator('[data-gapid="' + ALBUM.GapId + '"] .cardPadder-square')).toHaveCount(1);

    const books = { Kind: 'Book', CanTodo: false, Reason: null, Works: [BOOK] };
    await openItemPage(page, buildWebUiHarness(BOOK_ITEM, books), 'book-1');
    await expect(page.locator('[data-gapid="' + BOOK.GapId + '"]')).toHaveClass(/portraitCard/);
});

test('a book page says it is more by the author', async ({ page }) => {
    const works = { ItemId: 'book-1', ItemName: 'A Novel', Kind: 'Book', CanTodo: false, Reason: null, Works: [BOOK] };
    await openItemPage(page, buildWebUiHarness(BOOK_ITEM, works), 'book-1');

    await expect(page.locator('#mtgWorksMissing').getByText("More by this author you don't have")).toBeVisible();
    await expect(page.locator('[data-gapid="' + BOOK.GapId + '"]')).toBeVisible();
});

test('the dialog shows who the work is by and its own links, and makes no detail lookup', async ({ page }) => {
    const works = { Kind: 'MusicAlbum', CanTodo: false, Reason: null, Works: [ALBUM] };
    await openItemPage(page, buildWebUiHarness(ARTIST_ITEM, works), 'artist-1');
    await openCardDialog(page, ALBUM.GapId);

    const dialog = page.locator('.mtgDialog');
    await expect(dialog.getByText('A Missing Album (1994)')).toBeVisible();
    await expect(dialog.getByText('Album by A Band')).toBeVisible();
    const link = dialog.locator('.mtgDialogLinks a');
    await expect(link).toHaveCount(1);
    await expect(link).toHaveText('View on MusicBrainz');
    await expect(link).toHaveAttribute('href', 'https://musicbrainz.org/release-group/rg-1');
    await expect(link).toHaveAttribute('target', '_blank');
    await expect(dialog.getByText('Loading details')).toHaveCount(0);
    await expect(dialog.locator('.mtgSendButton')).toHaveCount(0);

    expect(await page.evaluate(() => window.__lastDetailUrl)).toBeNull();
    expect(await page.evaluate(() => window.__lastProfilesUrl)).toBeNull();
});

test('a link that is not https is not offered', async ({ page }) => {
    const album = Object.assign({}, ALBUM, { Links: [{ Name: 'Bad', Url: 'javascript:alert(1)' }, { Name: 'Good', Url: 'https://example.com/x' }] });
    const works = { Kind: 'MusicAlbum', CanTodo: false, Reason: null, Works: [album] };
    await openItemPage(page, buildWebUiHarness(ARTIST_ITEM, works), 'artist-1');
    await openCardDialog(page, ALBUM.GapId);

    const links = page.locator('.mtgDialog .mtgDialogLinks a');
    await expect(links).toHaveCount(1);
    await expect(links).toHaveText('View on Good');
});

test('an administrator can add a work to the TODO list from the dialog', async ({ page }) => {
    const works = { Kind: 'Book', CanTodo: true, Reason: null, Works: [BOOK] };
    await openItemPage(page, buildWebUiHarness(BOOK_ITEM, works, null, 1), 'book-1');
    await openCardDialog(page, BOOK.GapId);

    const button = page.locator('.mtgDialog .mtgTodoButton');
    await button.click();
    await expect(button).toHaveText('Added to TODO');

    const todoUrl = await page.evaluate(() => window.__lastTodoUrl);
    expect(todoUrl).toContain('Item/book-1/Works/Todo');
    expect(todoUrl).toContain('gapId=' + encodeURIComponent(BOOK.GapId));
});

test('no Add to TODO for a caller who is not an administrator', async ({ page }) => {
    const works = { Kind: 'Book', CanTodo: false, Reason: null, Works: [BOOK] };
    await openItemPage(page, buildWebUiHarness(BOOK_ITEM, works), 'book-1');
    await openCardDialog(page, BOOK.GapId);

    await expect(page.locator('.mtgDialog .mtgTodoButton')).toHaveCount(0);
});

test('a reason is shown when the works could not be looked up', async ({ page }) => {
    const works = { Kind: 'MusicAlbum', CanTodo: false, Reason: 'This artist has no MusicBrainz id.', Works: [] };
    await openItemPage(page, buildWebUiHarness(ARTIST_ITEM, works), 'artist-1');

    await expect(page.locator('#mtgWorksMissing')).toContainText('This artist has no MusicBrainz id.');
});

test('nothing is rendered when nothing is missing, or the surface is off', async ({ page }) => {
    const none = { Kind: 'MusicAlbum', CanTodo: false, Reason: null, Works: [] };
    await openItemPage(page, buildWebUiHarness(ARTIST_ITEM, none), 'artist-1');
    await expect(page.locator('#mtgWorksMissing')).toHaveCount(0);

    await openItemPage(page, buildWebUiHarness(ARTIST_ITEM, null), 'artist-1');
    await expect(page.locator('#mtgWorksMissing')).toHaveCount(0);
});
