// Drives the real mindthegaps.webui.js against a fake jellyfin-web home screen. This is the surface
// with the least straightforward wiring: the home view is cached by jellyfin-web and its own sections
// render asynchronously after viewshow fires, so the script waits for a MutationObserver signal rather
// than inserting immediately, and must not fire again on its own row coming and going. Send/Add-to-TODO
// live inside the detail dialog (see webui-dialog.spec.js), not on the card itself.
const { test, expect } = require('@playwright/test');
const { buildWebUiHomeHarness } = require('./support/webui-harness');

async function openHomePage(page, harnessPath) {
    await page.goto('file://' + harnessPath);
    await page.evaluate(() => {
        document.querySelector('.page').dispatchEvent(new Event('viewshow', { bubbles: true }));
    });
}

// Simulates jellyfin-web laying out its own home sections after the view has shown.
async function simulateJellyfinsOwnSections(page) {
    await page.evaluate(() => {
        var sections = document.querySelector('#homeTab .sections');
        var section = document.createElement('div');
        section.className = 'verticalSection';
        section.textContent = 'Continue Watching';
        sections.appendChild(section);
    });
}

async function openCardDialog(page, gapId) {
    await page.locator('[data-gapid="' + gapId + '"]').click();
    await expect(page.locator('.mtgDialogBackdrop')).toHaveClass(/mtgDialogOpen/);
}

test('renders the discover row once jellyfin has laid out its own sections, in normal document flow', async ({ page }) => {
    const discover = {
        CanSendMovies: true,
        CanSendSeries: false,
        Titles: [{ GapId: 'recommendation:movie:1', Title: 'A Recommended Movie', Kind: 'Movie', Year: 2001, Because: 'Because you have Fargo', TmdbId: 1, ImageUrl: 'https://example.com/p.jpg', Upcoming: false }]
    };
    const harnessPath = buildWebUiHomeHarness(discover);
    await openHomePage(page, harnessPath);

    // Before jellyfin's own sections exist, nothing is inserted yet (an early insertion would be wiped
    // by jellyfin's own innerHTML assignment when it finally lays its sections out).
    await page.waitForTimeout(300);
    await expect(page.locator('#mtgHomeDiscover')).toHaveCount(0);

    await simulateJellyfinsOwnSections(page);

    const row = page.locator('#mtgHomeDiscover');
    await expect(row).toBeVisible({ timeout: 2000 });
    await expect(row.getByText('Discover: not in your library')).toBeVisible();
    await expect(row.getByText('A Recommended Movie')).toBeVisible();

    const positions = await row.evaluate((el) => {
        const all = [el, ...el.querySelectorAll('*')];
        return all.map((e) => getComputedStyle(e).position);
    });
    expect(positions).not.toContain('fixed');
    expect(positions).not.toContain('absolute');
});

test('a movie can be sent from the dialog; a series on the same row respects its own flag', async ({ page }) => {
    const discover = {
        CanSendMovies: true,
        CanSendSeries: false,
        Titles: [
            { GapId: 'recommendation:movie:1', Title: 'A Recommended Movie', Kind: 'Movie', Year: 2001, TmdbId: 1, ImageUrl: null, Upcoming: false },
            { GapId: 'recommendation:series:2', Title: 'A Recommended Show', Kind: 'Series', Year: 2010, TmdbId: 2, ImageUrl: null, Upcoming: false }
        ]
    };
    const harnessPath = buildWebUiHomeHarness(discover, { Success: true, Message: 'Sent 1 item(s).' });
    await openHomePage(page, harnessPath);
    await simulateJellyfinsOwnSections(page);
    await expect(page.locator('#mtgHomeDiscover')).toBeVisible();

    // The movie (CanSendMovies true) gets a Send button in its dialog; the series (CanSendSeries false)
    // gets none.
    await openCardDialog(page, 'recommendation:series:2');
    await expect(page.locator('.mtgDialog .mtgSendButton')).toHaveCount(0);
    await page.locator('.mtgDialogClose').click();
    await expect(page.locator('.mtgDialogBackdrop')).not.toHaveClass(/mtgDialogOpen/);

    await openCardDialog(page, 'recommendation:movie:1');
    const button = page.locator('.mtgDialog .mtgSendButton');
    await expect(button).toHaveCount(1);
    await button.click();
    await expect(button).toHaveText('Sent');
    const sendUrl = await page.evaluate(() => window.__lastSendUrl);
    expect(sendUrl).toContain('Home/Send');
    expect(sendUrl).toContain('gapId=recommendation%3Amovie%3A1');
});

test('a card with no send flags gets a TMDB link and, for an administrator, an Add to TODO fallback', async ({ page }) => {
    const discover = {
        CanSendMovies: false,
        CanSendSeries: false,
        CanTodo: true,
        Titles: [{ GapId: 'recommendation:movie:1', Title: 'A Recommended Movie', Kind: 'Movie', Year: 2001, TmdbId: 603, ImageUrl: null, Upcoming: false }]
    };
    const harnessPath = buildWebUiHomeHarness(discover, null, 1);
    await openHomePage(page, harnessPath);
    await simulateJellyfinsOwnSections(page);
    await openCardDialog(page, 'recommendation:movie:1');

    await expect(page.locator('.mtgDialog .mtgSendButton')).toHaveCount(0);
    const tmdbLink = page.locator('.mtgDialog .mtgDialogLinks a').first();
    await expect(tmdbLink).toHaveAttribute('href', 'https://www.themoviedb.org/movie/603');

    const todoBtn = page.locator('.mtgDialog .mtgTodoButton');
    await expect(todoBtn).toBeVisible();
    await todoBtn.click();
    await expect(todoBtn).toHaveText('Added to TODO');
    const todoUrl = await page.evaluate(() => window.__lastTodoUrl);
    expect(todoUrl).toContain('Home/Todo');
});

test('renders nothing when the surface is off (the endpoint 404s), with no JS errors', async ({ page }) => {
    const harnessPath = buildWebUiHomeHarness(null);
    await openHomePage(page, harnessPath);
    await simulateJellyfinsOwnSections(page);
    await page.waitForTimeout(400);

    await expect(page.locator('#mtgHomeDiscover')).toHaveCount(0);
    const errors = await page.evaluate(() => window.__uiTestErrors);
    expect(errors).toEqual([]);
});

test('the row surviving its own re-insertion does not retrigger a reload loop', async ({ page }) => {
    const discover = { CanSendMovies: false, CanSendSeries: false, Titles: [{ GapId: 'recommendation:movie:1', Title: 'A Recommended Movie', Kind: 'Movie', Year: 2001, TmdbId: 1, ImageUrl: null, Upcoming: false }] };
    const harnessPath = buildWebUiHomeHarness(discover);
    await openHomePage(page, harnessPath);
    await simulateJellyfinsOwnSections(page);
    await expect(page.locator('#mtgHomeDiscover')).toBeVisible();

    // A second viewshow (returning to the cached home) must not duplicate the row.
    await page.evaluate(() => {
        document.querySelector('.page').dispatchEvent(new Event('viewshow', { bubbles: true }));
    });
    await page.waitForTimeout(400);
    await expect(page.locator('#mtgHomeDiscover')).toHaveCount(1);
});

// A home section is not padded by its parent: jellyfin-web wraps its title in a padded-left container, and
// the scroller supplies the cards' own offset. A bare title sits at the page edge, left of every other
// section's.
test('the discover row uses the home markup: title in a padded-left container, scroller without no-padding', async ({ page }) => {
    const discover = {
        CanSendMovies: true, CanSendSeries: false,
        Titles: [{ GapId: 'recommendation:movie:1', Title: 'A Recommended Movie', Kind: 'Movie', Year: 2001, Because: 'Because you have Fargo', TmdbId: 1, ImageUrl: null, Upcoming: false }]
    };
    await openHomePage(page, buildWebUiHomeHarness(discover));
    await simulateJellyfinsOwnSections(page);

    const row = page.locator('#mtgHomeDiscover');
    await expect(row).toBeVisible({ timeout: 2000 });
    await expect(row.locator('.sectionTitleContainer.sectionTitleContainer-cards.padded-left h2.sectionTitle')).toHaveText('Discover: not in your library');
    await expect(row.locator('[is="emby-scroller"]')).not.toHaveClass(/no-padding/);
});
