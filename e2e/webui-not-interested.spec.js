// "Not interested" is a signed-in user's own way to stop seeing a title on the injected pages. It is offered
// only when the server says the caller may keep the list (CanHide), from the title's dialog;
// the card then stays where it was, greyed, with an Undo, until the page is next laid out. Each row header
// shows the size of the user's list and opens it, where a title can be shown again, and closing that list
// lays the page out again so a title shown again comes back.
const { test, expect } = require('@playwright/test');
const { buildWebUiHarness, buildWebUiHomeHarness } = require('./support/webui-harness');

const PERSON_ITEM = { Id: 'person-1', Name: 'Some Actor', Type: 'Person' };

function personMissing(canHide, notInterestedCount) {
    return {
        PersonId: 'person-1',
        PersonName: 'Some Actor',
        Reason: null,
        CanTodo: true,
        CanHide: canHide,
        NotInterestedCount: notInterestedCount,
        Movies: [
            { GapId: 'filmography:movie:1', Title: 'A Missing Movie', Year: 2001, Kind: 'Movie', TmdbId: 1, ImageUrl: 'https://example.com/1.jpg' },
            { GapId: 'filmography:movie:2', Title: 'Another Movie', Year: 2003, Kind: 'Movie', TmdbId: 2, ImageUrl: 'https://example.com/2.jpg' }
        ],
        Series: []
    };
}

async function openPersonPage(page, missing) {
    await page.goto('file://' + buildWebUiHarness(PERSON_ITEM, missing));
    await page.evaluate(() => { window.location.hash = '#/details?id=person-1'; });
    await page.evaluate(() => { document.querySelector('.page').dispatchEvent(new Event('viewshow', { bubbles: true })); });
    await expect(page.locator('#mtgPersonMissing')).toBeVisible();
}

const card = (page, gapId) => page.locator('#mtgPersonMissing .mtgCard[data-gapid="' + gapId + '"]');

async function openCardDialog(page, gapId) {
    await card(page, gapId).click();
    await expect(page.locator('.mtgDialogBackdrop')).toHaveClass(/mtgDialogOpen/);
}

test('nothing is offered when the caller may not keep the list', async ({ page }) => {
    await openPersonPage(page, personMissing(false, 0));

    await expect(page.locator('.mtgNotInterestedLink')).toHaveCount(0);
    await openCardDialog(page, 'filmography:movie:1');
    await expect(page.locator('.mtgDialog .mtgNotInterestedButton')).toHaveCount(0);
});

test('saying not interested greys the card in place, and Undo puts it back', async ({ page }) => {
    await openPersonPage(page, personMissing(true, 0));
    const link = page.locator('#mtgPersonMissing .mtgNotInterestedLink');
    await expect(link).toBeHidden();

    await openCardDialog(page, 'filmography:movie:1');
    await page.locator('.mtgDialog .mtgNotInterestedButton').click();

    await expect(page.locator('.mtgDialogBackdrop')).not.toHaveClass(/mtgDialogOpen/);
    expect(await page.evaluate(() => window.__notInterestedCalls)).toEqual(['POST MindTheGaps/Person/person-1/NotInterested?gapId=filmography%3Amovie%3A1']);
    await expect(card(page, 'filmography:movie:1')).toHaveClass(/mtgHidden/);
    await expect(card(page, 'filmography:movie:2')).not.toHaveClass(/mtgHidden/);
    await expect(link).toHaveText('Not interested (1)');

    // Focus lands on the Undo, which names the title; a click on the greyed card opens nothing.
    const undo = card(page, 'filmography:movie:1').locator('.mtgHiddenUndo');
    await expect(undo).toBeFocused();
    await expect(undo).toHaveAccessibleName('Undo not interested in A Missing Movie');
    // Dispatched on the card itself: with no jellyfin-web stylesheet the image box has no height, so the
    // overlay's Undo spills over the title and a pointer click there would land on it.
    await card(page, 'filmography:movie:1').evaluate((el) => el.click());
    await expect(page.locator('.mtgDialogBackdrop')).not.toHaveClass(/mtgDialogOpen/);

    await undo.click();
    expect((await page.evaluate(() => window.__notInterestedCalls))[1]).toBe('POST MindTheGaps/Person/person-1/NotInterested/Remove?gapId=filmography%3Amovie%3A1');
    await expect(card(page, 'filmography:movie:1')).not.toHaveClass(/mtgHidden/);
    await expect(card(page, 'filmography:movie:1').locator('.mtgHiddenOverlay')).toHaveCount(0);
    await expect(card(page, 'filmography:movie:1')).toBeFocused();
    await expect(link).toBeHidden();
    expect(await page.evaluate(() => window.__uiTestErrors)).toEqual([]);
});

test('a failed request changes nothing and says so', async ({ page }) => {
    await openPersonPage(page, personMissing(true, 0));
    await page.evaluate(() => { window.__notInterestedFails = true; });
    const alerts = [];
    page.on('console', (m) => { if (m.text().startsWith('Dashboard.alert')) { alerts.push(m.text()); } });

    await openCardDialog(page, 'filmography:movie:1');
    const button = page.locator('.mtgDialog .mtgNotInterestedButton');
    await button.click();

    await expect.poll(() => alerts).toEqual(['Dashboard.alert: Could not update your list.']);
    await expect(button).toBeEnabled();
    await expect(card(page, 'filmography:movie:1')).not.toHaveClass(/mtgHidden/);
});

test('the header opens the list, a title can be shown again, and closing it lays the page out again', async ({ page }) => {
    await openPersonPage(page, personMissing(true, 2));
    await page.evaluate(() => {
        window.__notInterestedList = [
            { Id: 'person:9:10', Name: 'Hidden Film', Year: 1990, TargetKindName: 'Movie', ImageUrl: 'https://example.com/10.jpg' },
            { Id: 'home:11', Name: 'Hidden Show', Year: null, TargetKindName: 'Series', ImageUrl: null }
        ];
    });
    const link = page.locator('#mtgPersonMissing .mtgNotInterestedLink');
    await expect(link).toHaveText('Not interested (2)');

    await link.click();
    const dialog = page.locator('.mtgDialog .mtgNiBody');
    await expect(dialog.getByRole('heading', { name: 'Not interested' })).toBeVisible();
    await expect(dialog.getByRole('listitem')).toHaveCount(2);
    await expect(dialog.getByRole('listitem').nth(0)).toContainText('Hidden Film (1990)');
    await expect(dialog.getByRole('listitem').nth(1)).toContainText('Hidden Show');

    await dialog.getByRole('button', { name: 'Show Hidden Film (1990) again' }).click();
    expect(await page.evaluate(() => window.__notInterestedCalls)).toContain('POST MindTheGaps/WebUi/NotInterested/Restore?id=person%3A9%3A10');
    await expect(dialog.getByRole('listitem').nth(0)).toContainText('Will show again');
    await expect(dialog.getByRole('status')).toHaveText('Hidden Film (1990) will show again.');
    await expect(link).toHaveText('Not interested (1)');

    // The page asks for its titles again once the list closes, so the one shown again comes back.
    await page.evaluate(() => {
        window.__pageLoads = 0;
        var getItem = ApiClient.getItem;
        ApiClient.getItem = function () { window.__pageLoads++; return getItem.apply(this, arguments); };
    });
    await page.keyboard.press('Escape');
    await expect.poll(() => page.evaluate(() => window.__pageLoads)).toBe(1);
    await expect(page.locator('#mtgPersonMissing')).toBeVisible();
    expect(await page.evaluate(() => window.__uiTestErrors)).toEqual([]);
});

test('closing the list without showing anything again leaves the page as it is', async ({ page }) => {
    await openPersonPage(page, personMissing(true, 1));
    await page.evaluate(() => {
        window.__notInterestedList = [{ Id: 'person:9:10', Name: 'Hidden Film', Year: 1990, TargetKindName: 'Movie' }];
        window.__pageLoads = 0;
        var getItem = ApiClient.getItem;
        ApiClient.getItem = function () { window.__pageLoads++; return getItem.apply(this, arguments); };
    });

    await page.locator('#mtgPersonMissing .mtgNotInterestedLink').click();
    await expect(page.locator('.mtgNiRow')).toHaveCount(1);
    await page.keyboard.press('Escape');

    await expect(page.locator('.mtgDialogBackdrop')).not.toHaveClass(/mtgDialogOpen/);
    expect(await page.evaluate(() => window.__pageLoads)).toBe(0);
});

test('show all again takes a second click, empties the list, and lays the page out again on close', async ({ page }) => {
    await openPersonPage(page, personMissing(true, 2));
    await page.evaluate(() => {
        window.__notInterestedList = [
            { Id: 'person:9:10', Name: 'Hidden Film', Year: 1990, TargetKindName: 'Movie' },
            { Id: 'home:11', Name: 'Hidden Show', Year: null, TargetKindName: 'Series' }
        ];
    });
    await page.locator('#mtgPersonMissing .mtgNotInterestedLink').click();
    const dialog = page.locator('.mtgDialog .mtgNiBody');
    await expect(dialog.getByRole('listitem')).toHaveCount(2);

    // The first click only asks; leaving the button takes the question back.
    const clearAll = dialog.locator('.mtgNiClear');
    await expect(clearAll).toHaveText('Show all again');
    await clearAll.click();
    await expect(clearAll).toHaveText('Show all 2 again?');
    await expect(clearAll).toHaveAccessibleName('Confirm: show all 2 titles again');
    expect(await page.evaluate(() => window.__notInterestedCalls.filter((c) => c.indexOf('/Clear') !== -1))).toEqual([]);
    await dialog.getByRole('button', { name: 'Show Hidden Show again' }).focus();
    await expect(clearAll).toHaveText('Show all again');

    await clearAll.click();
    await clearAll.click();
    expect(await page.evaluate(() => window.__notInterestedCalls)).toContain('POST MindTheGaps/WebUi/NotInterested/Clear?');
    await expect(dialog.getByRole('status')).toHaveText('Every title will show again.');
    await expect(dialog.getByText('Will show again', { exact: true })).toHaveCount(2);
    await expect(clearAll).toBeHidden();
    await expect(page.locator('#mtgPersonMissing .mtgNotInterestedLink')).toBeHidden();

    await page.evaluate(() => {
        window.__pageLoads = 0;
        var getItem = ApiClient.getItem;
        ApiClient.getItem = function () { window.__pageLoads++; return getItem.apply(this, arguments); };
    });
    await page.keyboard.press('Escape');
    await expect.poll(() => page.evaluate(() => window.__pageLoads)).toBe(1);
    expect(await page.evaluate(() => window.__uiTestErrors)).toEqual([]);
});

test('show all again is not offered for an empty list', async ({ page }) => {
    await openPersonPage(page, personMissing(true, 1));
    await page.locator('#mtgPersonMissing .mtgNotInterestedLink').click();

    await expect(page.locator('.mtgDialog .mtgNiBody').getByRole('status')).toHaveText('Nothing is hidden.');
    await expect(page.locator('.mtgDialog .mtgNiClear')).toBeHidden();
});

test('the home row says not interested through its own route', async ({ page }) => {
    const discover = {
        CanTodo: true,
        CanHide: true,
        NotInterestedCount: 0,
        Titles: [{ GapId: 'rec:movie:5', Title: 'A Suggestion', Year: 2020, Kind: 'Movie', TmdbId: 5, ImageUrl: 'https://example.com/5.jpg' }]
    };
    await page.goto('file://' + buildWebUiHomeHarness(discover));
    await page.evaluate(() => { document.querySelector('.page').dispatchEvent(new Event('viewshow', { bubbles: true })); });
    await page.evaluate(() => {
        var s = document.createElement('div');
        s.className = 'verticalSection section0';
        document.querySelector('#homeTab .sections').appendChild(s);
    });
    const row = page.locator('#mtgHomeDiscover');
    await expect(row).toBeVisible();

    await row.locator('.mtgCard[data-gapid="rec:movie:5"]').click();
    await page.locator('.mtgDialog .mtgNotInterestedButton').click();

    expect(await page.evaluate(() => window.__notInterestedCalls)).toEqual(['POST MindTheGaps/Home/NotInterested?gapId=rec%3Amovie%3A5']);
    await expect(row.locator('.mtgCard[data-gapid="rec:movie:5"]')).toHaveClass(/mtgHidden/);
    await expect(row.locator('.mtgNotInterestedLink')).toHaveText('Not interested (1)');
});
