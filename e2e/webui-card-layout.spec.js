// How a card lays out its text, and what an author's page shows. The harness carries none of jellyfin-web's
// own stylesheet, so a card is given a width the way the real grid would give it one.
const { test, expect } = require('@playwright/test');
const { buildWebUiHarness } = require('./support/webui-harness');

const PERSON_ITEM = { Id: 'person-1', Name: 'Some Actor', Type: 'Person' };

const LONG_TITLE = 'Doctor Who: 60th Anniversary Specials and a Great Deal More Besides';
const LONG_ROLE = 'as The Doctor (archive footage) and the Fourteenth Doctor';

const movie = (extra) => Object.assign({ GapId: 'filmography:movie:1', Title: 'A Movie', Year: 2023, Role: null, Kind: 'Movie', TmdbId: 1, ImageUrl: null, Upcoming: false, OnList: false }, extra);

const missing = (movies, extra) => Object.assign({ CanSendMovies: false, CanSendSeries: false, CanTodo: true, Reason: null, Movies: movies, Series: [] }, extra);

async function openPersonPage(page, harnessPath) {
    await page.goto('file://' + harnessPath);
    await page.evaluate(() => { window.location.hash = '#/details?id=person-1'; });
    await page.evaluate(() => {
        document.querySelector('.page').dispatchEvent(new Event('viewshow', { bubbles: true }));
    });
}

// A card with no artwork also prints its title on the image, so the title line is the one to look for.
const titleIn = (page, section, text) => page.locator(section + ' .cardText-first', { hasText: text });

async function narrowCards(page) {
    await page.locator('.mtgCard').evaluateAll((cards) => cards.forEach((c) => { c.style.width = '11em'; }));
}

test('a long role is cut with an ellipsis while the year stays whole', async ({ page }) => {
    await openPersonPage(page, buildWebUiHarness(PERSON_ITEM, missing([movie({ Title: LONG_TITLE, Role: LONG_ROLE })])));
    await narrowCards(page);

    const year = page.locator('.mtgCard .mtgCardYear');
    const role = page.locator('.mtgCard .mtgCardRole');
    await expect(year).toHaveText('2023 ·');
    expect(await year.evaluate((el) => el.scrollWidth <= el.clientWidth)).toBe(true);
    expect(await role.evaluate((el) => el.scrollWidth > el.clientWidth)).toBe(true);
    expect(await role.evaluate((el) => getComputedStyle(el).textOverflow)).toBe('ellipsis');

    const card = await page.locator('.mtgCard').boundingBox();
    const yearBox = await year.boundingBox();
    expect(yearBox.x).toBeGreaterThanOrEqual(card.x);
    expect(yearBox.x + yearBox.width).toBeLessThanOrEqual(card.x + card.width);
});

test('the bookmark stays in view whatever the role or title says', async ({ page }) => {
    await openPersonPage(page, buildWebUiHarness(PERSON_ITEM, missing([movie({ Title: LONG_TITLE, Role: LONG_ROLE })])));
    await narrowCards(page);

    const card = await page.locator('.mtgCard').boundingBox();
    const mark = await page.locator('.mtgCard .mtgWant').boundingBox();
    expect(mark.x).toBeGreaterThanOrEqual(card.x);
    expect(mark.x + mark.width).toBeLessThanOrEqual(card.x + card.width);
});

test('a card with no role shows just the year, and one with neither shows an empty line', async ({ page }) => {
    await openPersonPage(page, buildWebUiHarness(PERSON_ITEM, missing([
        movie({ GapId: 'filmography:movie:1' }),
        movie({ GapId: 'filmography:movie:2', Year: null })
    ])));

    await expect(page.locator('[data-gapid="filmography:movie:1"] .mtgCardMeta')).toHaveText('2023');
    await expect(page.locator('[data-gapid="filmography:movie:2"] .mtgCardMeta')).toHaveText('');
});

test('the whole title and the whole role are on hover', async ({ page }) => {
    await openPersonPage(page, buildWebUiHarness(PERSON_ITEM, missing([movie({ Title: LONG_TITLE, Role: LONG_ROLE })])));

    await expect(page.locator('.mtgCard .cardText-first')).toHaveAttribute('title', LONG_TITLE);
    await expect(page.locator('.mtgCard .mtgCardMeta')).toHaveAttribute('title', '2023 · ' + LONG_ROLE);
});

// ---- An author's page ----

const BOOK = {
    GapId: 'bibliography:OL1A:OL2W',
    Title: 'Another Novel',
    Year: 1987,
    Kind: 'Book',
    Creator: 'An Author',
    ImageUrl: null,
    Upcoming: false,
    OnList: false,
    Links: [{ Name: 'OpenLibrary', Url: 'https://openlibrary.org/works/OL2W' }]
};

const AUTHOR_NOTE = 'This person has no TMDB id in the library, so their filmography cannot be looked up.';

test('an author page lists the books they have not got, and drops the filmography note', async ({ page }) => {
    const works = { ItemId: 'person-1', ItemName: 'An Author', Kind: 'Book', CanTodo: true, Reason: null, Works: [BOOK] };
    const harness = buildWebUiHarness(PERSON_ITEM, missing([], { Reason: AUTHOR_NOTE }), null, 1, undefined, undefined, works);
    await openPersonPage(page, harness);

    const section = page.locator('#mtgWorksMissing');
    await expect(section.getByText("More by this author you don't have")).toBeVisible();
    await expect(titleIn(page, '#mtgWorksMissing', 'Another Novel')).toBeVisible();
    await expect(page.locator('#mtgPersonMissing')).toHaveCount(0);
});

test('an actor who is also an author gets both sections', async ({ page }) => {
    const works = { ItemId: 'person-1', ItemName: 'Both', Kind: 'Book', CanTodo: true, Reason: null, Works: [BOOK] };
    const harness = buildWebUiHarness(PERSON_ITEM, missing([movie()]), null, 1, undefined, undefined, works);
    await openPersonPage(page, harness);

    await expect(titleIn(page, '#mtgPersonMissing', 'A Movie')).toBeVisible();
    await expect(titleIn(page, '#mtgWorksMissing', 'Another Novel')).toBeVisible();
});

test('on an author page the books row takes the filmography section\'s markup, right after it', async ({ page }) => {
    const works = { ItemId: 'person-1', ItemName: 'Both', Kind: 'Book', CanTodo: true, Reason: null, Works: [BOOK] };
    await openPersonPage(page, buildWebUiHarness(PERSON_ITEM, missing([movie()]), null, 1, undefined, undefined, works));

    const wrap = page.locator('#mtgWorksMissing');
    await expect(wrap).toHaveClass(/detailPageSecondaryContainer/);
    await expect(wrap).toHaveClass(/padded-left/);
    await expect(wrap).not.toHaveClass(/detailVerticalSection/);

    const order = await page.evaluate(() => {
        const kids = Array.prototype.slice.call(document.querySelector('.detailPageContent').children).map((e) => e.id);
        return kids;
    });
    expect(order).toEqual(['mtgPersonMissing', 'mtgWorksMissing', 'similarCollapsible']);
});

test('an actor with no books gets only the filmography', async ({ page }) => {
    await openPersonPage(page, buildWebUiHarness(PERSON_ITEM, missing([movie()])));

    await expect(titleIn(page, '#mtgPersonMissing', 'A Movie')).toBeVisible();
    await expect(page.locator('#mtgWorksMissing')).toHaveCount(0);
});

test('a filmography note still shows on a person who has no books', async ({ page }) => {
    await openPersonPage(page, buildWebUiHarness(PERSON_ITEM, missing([], { Reason: AUTHOR_NOTE })));

    await expect(page.locator('#mtgPersonMissing').getByText(AUTHOR_NOTE)).toBeVisible();
    await expect(page.locator('#mtgWorksMissing')).toHaveCount(0);
});

test('adding a book from an author page goes to the works route of the person', async ({ page }) => {
    const works = { ItemId: 'person-1', ItemName: 'An Author', Kind: 'Book', CanTodo: true, Reason: null, Works: [BOOK] };
    await openPersonPage(page, buildWebUiHarness(PERSON_ITEM, missing([], { Reason: AUTHOR_NOTE }), null, 1, undefined, undefined, works));

    await page.locator('#mtgWorksMissing .mtgWant').click();

    await expect(page.locator('#mtgWorksMissing .mtgWant')).toHaveAttribute('aria-pressed', 'true');
    expect(await page.evaluate(() => window.__lastTodoUrl)).toContain('Item/person-1/Works/Todo');
});
