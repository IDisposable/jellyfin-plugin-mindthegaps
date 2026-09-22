// A card's image and the dialog's poster and backdrop are loaded through the server's image cache, and fall back
// to the provider's own address when the cache cannot serve them.
const { test, expect } = require('@playwright/test');
const { buildWebUiHarness } = require('./support/webui-harness');
const { pointImagesAtTheServer, serveImages } = require('./support/image-cache');

const PERSON_ITEM = { Id: 'person-1', Name: 'Some Actor', Type: 'Person' };
const POSTER = 'https://image.tmdb.org/t/p/w500/poster.jpg';

const missing = () => ({
    CanTodo: false,
    Reason: null,
    Movies: [{ GapId: 'filmography:movie:1', Title: 'A Movie', Year: 2001, Role: null, Kind: 'Movie', TmdbId: 1, ImageUrl: POSTER, Upcoming: false }],
    Series: []
});

async function openPersonPage(page) {
    await page.goto('file://' + buildWebUiHarness(PERSON_ITEM, missing()));
    await pointImagesAtTheServer(page);
    await page.evaluate(() => { window.location.hash = '#/details?id=person-1'; });
    await page.evaluate(() => {
        document.querySelector('.page').dispatchEvent(new Event('viewshow', { bubbles: true }));
    });
}

const cardImage = (page) => page.locator('.mtgCard .cardImageContainer');
const backgroundOf = (locator) => locator.evaluate((el) => el.style.backgroundImage);
const viaServer = (url) => 'url("https://server.test/MindTheGaps/Image?u=' + encodeURIComponent(url) + '")';

test('a card image is loaded from the server, not from the provider', async ({ page }) => {
    const seen = await serveImages(page, 200, 200);
    await openPersonPage(page);

    await expect.poll(() => backgroundOf(cardImage(page))).toBe(viaServer(POSTER));
    await expect.poll(() => seen.server.length).toBeGreaterThan(0);
    expect(seen.provider).toHaveLength(0);
});

test('a card image the server cannot serve is loaded from the provider instead', async ({ page }) => {
    const seen = await serveImages(page, 404, 200);
    await openPersonPage(page);

    await expect.poll(() => backgroundOf(cardImage(page))).toBe('url("' + POSTER + '")');
    await expect.poll(() => seen.provider).toContain(POSTER);
});

test('the dialog shows the poster it starts with, then the detail\'s poster and backdrop, through the server', async ({ page }) => {
    await serveImages(page, 200, 200);
    await openPersonPage(page);
    await page.locator('.mtgCard').click();

    // The detail lookup's poster and backdrop replace the card's own once TMDB answers.
    await expect.poll(() => backgroundOf(page.locator('.mtgDialogPoster'))).toBe(viaServer('https://example.com/poster.jpg'));
    await expect.poll(() => backgroundOf(page.locator('.mtgDialogBackdropImage'))).toBe(viaServer('https://example.com/backdrop.jpg'));
});

test('a card with no image address shows the plain placeholder and asks for nothing', async ({ page }) => {
    const seen = await serveImages(page, 200, 200);
    const noImage = missing();
    noImage.Movies[0].ImageUrl = null;
    await page.goto('file://' + buildWebUiHarness(PERSON_ITEM, noImage));
    await pointImagesAtTheServer(page);
    await page.evaluate(() => { window.location.hash = '#/details?id=person-1'; });
    await page.evaluate(() => {
        document.querySelector('.page').dispatchEvent(new Event('viewshow', { bubbles: true }));
    });

    await expect(cardImage(page)).toHaveClass(/defaultCardBackground/);
    expect(seen.server).toHaveLength(0);
});
