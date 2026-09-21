// The detail dialog shared by all three web UI surfaces (person, item, home): opened by clicking a card,
// shows TMDB's own synopsis alongside the Send/Add-to-TODO action and, when sendable, a quality-profile
// picker. Driven here through the person-page harness (any surface wires to the same dialog code), since
// the dialog itself does not care which surface's card opened it. Appended straight to document.body,
// which is exactly what lets it use position:fixed safely despite jellyfin-web's page wrapper setting CSS
// containment (see CLAUDE.md's "position: fixed is not safe" note): a fixed element inside that
// containment would compute against the wrong box, but this dialog is not inside it.
const { test, expect } = require('@playwright/test');
const { buildWebUiHarness } = require('./support/webui-harness');

const PERSON_ITEM = { Id: 'person-1', Name: 'Some Actor', Type: 'Person' };

function missingWith(overrides) {
    return {
        CanSendMovies: true,
        CanSendSeries: true,
        CanTodo: true,
        Reason: null,
        Movies: [{ GapId: 'filmography:movie:1', Title: 'A Missing Movie', Year: 1999, Role: 'as Lead', Kind: 'Movie', TmdbId: 603, ImageUrl: null, Upcoming: false }],
        Series: [],
        ...overrides
    };
}

async function openPersonPage(page, harnessPath) {
    await page.goto('file://' + harnessPath);
    await page.evaluate(() => { window.location.hash = '#/details?id=person-1'; });
    await page.evaluate(() => {
        document.querySelector('.page').dispatchEvent(new Event('viewshow', { bubbles: true }));
    });
}

test('clicking a card opens the dialog with the title and year immediately, appended to document.body', async ({ page }) => {
    const harnessPath = buildWebUiHarness(PERSON_ITEM, missingWith({}));
    await openPersonPage(page, harnessPath);

    await page.locator('[data-gapid="filmography:movie:1"]').click();

    const backdrop = page.locator('.mtgDialogBackdrop');
    await expect(backdrop).toHaveClass(/mtgDialogOpen/);
    await expect(page.locator('.mtgDialogTitle')).toHaveText('A Missing Movie (1999)');

    // A direct child of body, not of the person section: this is what keeps position:fixed correct.
    const parentIsBody = await backdrop.evaluate((el) => el.parentElement === document.body);
    expect(parentIsBody).toBe(true);
    const position = await backdrop.evaluate((el) => getComputedStyle(el).position);
    expect(position).toBe('fixed');
});

test('the dialog fills in TMDB detail once the lookup resolves', async ({ page }) => {
    const harnessPath = buildWebUiHarness(PERSON_ITEM, missingWith({}));
    await openPersonPage(page, harnessPath);
    await page.locator('[data-gapid="filmography:movie:1"]').click();

    await expect(page.locator('.mtgDialogTagline')).toHaveText('Welcome to the Real World.');
    await expect(page.locator('.mtgDialogOverview')).toHaveText('A test overview.');
    await expect(page.locator('.mtgDialogGenres')).toHaveText('Action, Sci-Fi');
    await expect(page.locator('.mtgDialogMeta')).toContainText('136 min');
    await expect(page.locator('.mtgDialogMeta')).toContainText('Released');
    await expect(page.locator('.mtgDialogMeta')).toContainText('TMDB 8.2/10');

    const links = page.locator('.mtgDialogLinks a');
    await expect(links).toHaveCount(4);
    await expect(links.nth(0)).toHaveText('View on TMDB');
    await expect(links.nth(1)).toHaveText('View on IMDb');
    await expect(links.nth(1)).toHaveAttribute('href', 'https://www.imdb.com/title/tt0133093/');
    await expect(links.nth(2)).toHaveText('Search JustWatch');
    await expect(links.nth(2)).toHaveAttribute('href', 'https://www.justwatch.com/us/search?q=A%20Missing%20Movie');
    await expect(links.nth(2)).toHaveAttribute('target', '_blank');
    await expect(links.nth(3)).toHaveText('Watch trailer');
    await expect(links.nth(3)).toHaveAttribute('href', 'https://www.youtube.com/watch?v=vKQi3bBA1y8');

    const detailUrl = await page.evaluate(() => window.__lastDetailUrl);
    expect(detailUrl).toContain('WebUi/Detail');
    expect(detailUrl).toContain('tmdbId=603');
    expect(detailUrl).toContain('kind=Movie');
});

test('a series card shows season count and network in the meta line', async ({ page }) => {
    const missing = missingWith({
        Movies: [],
        Series: [{ GapId: 'filmography:series:9', Title: 'A Missing Show', Year: 2010, Role: null, Kind: 'Series', TmdbId: 1399, ImageUrl: null, Upcoming: false }]
    });
    const detail = {
        Title: 'A Missing Show', Kind: 'Series', TmdbId: 1399, Year: 2010,
        Tagline: null, Overview: 'A show overview.', Genres: [], RuntimeMinutes: 60, VoteAverage: null,
        Status: 'Ended', NumberOfSeasons: 8, Networks: ['HBO'],
        PosterUrl: null, BackdropUrl: null, TmdbUrl: 'https://www.themoviedb.org/tv/1399', ImdbUrl: null, YoutubeTrailerKey: null
    };
    const harnessPath = buildWebUiHarness(PERSON_ITEM, missing, null, undefined, detail);
    await openPersonPage(page, harnessPath);
    await page.locator('[data-gapid="filmography:series:9"]').click();

    await expect(page.locator('.mtgDialogMeta')).toContainText('8 seasons');
    await expect(page.locator('.mtgDialogMeta')).toContainText('HBO');
    // No rating, no tagline, no trailer/IMDb link for this fixture.
    await expect(page.locator('.mtgDialogTagline')).toHaveCount(0);
    await expect(page.locator('.mtgDialogLinks a')).toHaveCount(1);
});

test('shows a message instead when TMDB has nothing for the id', async ({ page }) => {
    const harnessPath = buildWebUiHarness(PERSON_ITEM, missingWith({}), null, undefined, null);
    await openPersonPage(page, harnessPath);
    await page.locator('[data-gapid="filmography:movie:1"]').click();

    await expect(page.locator('.mtgDialog .mtgNote')).toHaveText('No further details available.');
});

test('shows a message instead when the TMDB lookup fails, without breaking Send', async ({ page }) => {
    const harnessPath = buildWebUiHarness(PERSON_ITEM, missingWith({}), { Success: true, Message: 'Sent 1 item(s).' }, undefined, { reject: true });
    await openPersonPage(page, harnessPath);
    await page.locator('[data-gapid="filmography:movie:1"]').click();

    await expect(page.locator('.mtgDialog .mtgNote')).toHaveText('Could not load details from TMDB.');

    // Send does not depend on the TMDB lookup succeeding.
    const button = page.locator('.mtgDialog .mtgSendButton');
    await expect(button).toBeVisible();
    await button.click();
    await expect(button).toHaveText('Sent');
});

test('the quality profile picker is populated and preselects the configured default', async ({ page }) => {
    const harnessPath = buildWebUiHarness(PERSON_ITEM, missingWith({}));
    await openPersonPage(page, harnessPath);
    await page.locator('[data-gapid="filmography:movie:1"]').click();

    const select = page.locator('.mtgDialog .mtgProfileSelect');
    await expect(select).toBeVisible();
    const options = await select.locator('option').allTextContents();
    expect(options).toEqual(['HD-1080p', 'Ultra-HD']);
    await expect(select).toHaveValue('1');

    const profilesUrl = await page.evaluate(() => window.__lastProfilesUrl);
    expect(profilesUrl).toContain('WebUi/Profiles');
    expect(profilesUrl).toContain('kind=Movie');
});

test('sending with a chosen profile threads qualityProfileId through the Send call', async ({ page }) => {
    const harnessPath = buildWebUiHarness(PERSON_ITEM, missingWith({}), { Success: true, Message: 'Sent 1 item(s).' });
    await openPersonPage(page, harnessPath);
    await page.locator('[data-gapid="filmography:movie:1"]').click();

    const select = page.locator('.mtgDialog .mtgProfileSelect');
    await expect(select).toBeVisible();
    await select.selectOption('2');
    await page.locator('.mtgDialog .mtgSendButton').click();

    const sendUrl = await page.evaluate(() => window.__lastSendUrl);
    expect(sendUrl).toContain('qualityProfileId=2');
});

test('the picker stays hidden and Send still works when the profiles lookup fails', async ({ page }) => {
    const harnessPath = buildWebUiHarness(PERSON_ITEM, missingWith({}), { Success: true, Message: 'Sent 1 item(s).' }, undefined, undefined, { reject: true });
    await openPersonPage(page, harnessPath);
    await page.locator('[data-gapid="filmography:movie:1"]').click();

    await expect(page.locator('.mtgDialog .mtgProfileSelect')).toBeHidden();
    const button = page.locator('.mtgDialog .mtgSendButton');
    await button.click();
    await expect(button).toHaveText('Sent');

    const sendUrl = await page.evaluate(() => window.__lastSendUrl);
    expect(sendUrl).not.toContain('qualityProfileId');
});

test('closes via the close button, the backdrop, and Escape', async ({ page }) => {
    const harnessPath = buildWebUiHarness(PERSON_ITEM, missingWith({}));
    await openPersonPage(page, harnessPath);
    const backdrop = page.locator('.mtgDialogBackdrop');

    await page.locator('[data-gapid="filmography:movie:1"]').click();
    await expect(backdrop).toHaveClass(/mtgDialogOpen/);
    await page.locator('.mtgDialogClose').click();
    await expect(backdrop).not.toHaveClass(/mtgDialogOpen/);

    await page.locator('[data-gapid="filmography:movie:1"]').click();
    await expect(backdrop).toHaveClass(/mtgDialogOpen/);
    await backdrop.click({ position: { x: 5, y: 5 } });
    await expect(backdrop).not.toHaveClass(/mtgDialogOpen/);

    await page.locator('[data-gapid="filmography:movie:1"]').click();
    await expect(backdrop).toHaveClass(/mtgDialogOpen/);
    await page.keyboard.press('Escape');
    await expect(backdrop).not.toHaveClass(/mtgDialogOpen/);
});

test('clicking inside the dialog does not close it', async ({ page }) => {
    const harnessPath = buildWebUiHarness(PERSON_ITEM, missingWith({}));
    await openPersonPage(page, harnessPath);
    await page.locator('[data-gapid="filmography:movie:1"]').click();

    await page.locator('.mtgDialog').click();
    await expect(page.locator('.mtgDialogBackdrop')).toHaveClass(/mtgDialogOpen/);
});

test('opening a second card replaces the dialog contents rather than stacking them', async ({ page }) => {
    const missing = missingWith({
        Movies: [
            { GapId: 'filmography:movie:1', Title: 'A Missing Movie', Year: 1999, Role: null, Kind: 'Movie', TmdbId: 603, ImageUrl: null, Upcoming: false },
            { GapId: 'filmography:movie:2', Title: 'Another Movie', Year: 2003, Role: null, Kind: 'Movie', TmdbId: 604, ImageUrl: null, Upcoming: false }
        ]
    });
    const harnessPath = buildWebUiHarness(PERSON_ITEM, missing);
    await openPersonPage(page, harnessPath);

    await page.locator('[data-gapid="filmography:movie:1"]').click();
    await expect(page.locator('.mtgDialogTitle')).toHaveText('A Missing Movie (1999)');

    await page.locator('.mtgDialogClose').click();
    await page.locator('[data-gapid="filmography:movie:2"]').click();
    await expect(page.locator('.mtgDialogTitle')).toHaveText('Another Movie (2003)');
    await expect(page.locator('.mtgDialogBody')).toHaveCount(1);
});

// ---- Remote/keyboard reachability ----
// A card is a plain div: without tabindex/keydown handling it would be invisible to Tab order and to a
// TV remote's Select button, unlike a real jellyfin-web card. These pin that it behaves like one anyway.

test('a card is reachable by Tab and opens the dialog with Enter', async ({ page }) => {
    const harnessPath = buildWebUiHarness(PERSON_ITEM, missingWith({}));
    await openPersonPage(page, harnessPath);

    await page.evaluate(() => document.body.focus());
    await page.keyboard.press('Tab');
    const focusedGapId = await page.evaluate(() => document.activeElement.getAttribute('data-gapid'));
    expect(focusedGapId).toBe('filmography:movie:1');

    await page.keyboard.press('Enter');
    await expect(page.locator('.mtgDialogBackdrop')).toHaveClass(/mtgDialogOpen/);
});

test('Space also activates a focused card', async ({ page }) => {
    const harnessPath = buildWebUiHarness(PERSON_ITEM, missingWith({}));
    await openPersonPage(page, harnessPath);

    await page.locator('[data-gapid="filmography:movie:1"]').focus();
    await page.keyboard.press(' ');
    await expect(page.locator('.mtgDialogBackdrop')).toHaveClass(/mtgDialogOpen/);
});

// ---- Remote/keyboard behavior inside the open dialog ----

test('opening the dialog moves focus to the close button', async ({ page }) => {
    const harnessPath = buildWebUiHarness(PERSON_ITEM, missingWith({}));
    await openPersonPage(page, harnessPath);
    await page.locator('[data-gapid="filmography:movie:1"]').click();

    const activeIsClose = await page.evaluate(() => document.activeElement.classList.contains('mtgDialogClose'));
    expect(activeIsClose).toBe(true);
});

test('closing the dialog restores focus to the card that opened it', async ({ page }) => {
    const harnessPath = buildWebUiHarness(PERSON_ITEM, missingWith({}));
    await openPersonPage(page, harnessPath);

    await page.locator('[data-gapid="filmography:movie:1"]').focus();
    await page.keyboard.press('Enter');
    await expect(page.locator('.mtgDialogBackdrop')).toHaveClass(/mtgDialogOpen/);
    await page.locator('.mtgDialogClose').click();

    const active = await page.evaluate(() => document.activeElement.getAttribute('data-gapid'));
    expect(active).toBe('filmography:movie:1');
});

test('Tab cycles forward through the dialog controls and wraps around; Shift+Tab reverses', async ({ page }) => {
    const harnessPath = buildWebUiHarness(PERSON_ITEM, missingWith({}), { Success: true, Message: 'Sent 1 item(s).' });
    await openPersonPage(page, harnessPath);
    await page.locator('[data-gapid="filmography:movie:1"]').click();

    // Wait for the profile select and the extra links (IMDb, JustWatch, trailer) to settle in, so the order
    // below (close, TMDB, IMDb, JustWatch, trailer, select, Send) is the full, final set.
    await expect(page.locator('.mtgDialog .mtgProfileSelect')).toBeVisible();
    await expect(page.locator('.mtgDialogLinks a')).toHaveCount(4);

    const activeClasses = () => page.evaluate(() => document.activeElement.className);

    expect(await activeClasses()).toContain('mtgDialogClose');
    for (let i = 0; i < 6; i++) { await page.keyboard.press('Tab'); }
    expect(await activeClasses()).toContain('mtgSendButton');

    // Tab from the last control wraps back to the first.
    await page.keyboard.press('Tab');
    expect(await activeClasses()).toContain('mtgDialogClose');

    // Shift+Tab from the first control wraps back to the last.
    await page.keyboard.press('Shift+Tab');
    expect(await activeClasses()).toContain('mtgSendButton');
});

test('ArrowRight/ArrowLeft move focus the same way Tab does', async ({ page }) => {
    const harnessPath = buildWebUiHarness(PERSON_ITEM, missingWith({}));
    await openPersonPage(page, harnessPath);
    await page.locator('[data-gapid="filmography:movie:1"]').click();
    await expect(page.locator('.mtgDialogLinks a')).toHaveCount(4);

    await page.keyboard.press('ArrowRight');
    let active = await page.evaluate(() => document.activeElement.textContent.trim());
    expect(active).toBe('View on TMDB');

    await page.keyboard.press('ArrowLeft');
    const activeIsClose = await page.evaluate(() => document.activeElement.classList.contains('mtgDialogClose'));
    expect(activeIsClose).toBe(true);
});

test('arrow keys on a focused profile select change its value instead of moving focus out', async ({ page }) => {
    const harnessPath = buildWebUiHarness(PERSON_ITEM, missingWith({}));
    await openPersonPage(page, harnessPath);
    await page.locator('[data-gapid="filmography:movie:1"]').click();

    const select = page.locator('.mtgDialog .mtgProfileSelect');
    await expect(select).toBeVisible();
    await select.focus();
    await page.keyboard.press('ArrowDown');

    await expect(select).toHaveValue('2');
    const stillOnSelect = await page.evaluate(() => document.activeElement.classList.contains('mtgProfileSelect'));
    expect(stillOnSelect).toBe(true);
});

test('the browser Back button closes the dialog', async ({ page }) => {
    const harnessPath = buildWebUiHarness(PERSON_ITEM, missingWith({}));
    await openPersonPage(page, harnessPath);
    await page.locator('[data-gapid="filmography:movie:1"]').click();
    await expect(page.locator('.mtgDialogBackdrop')).toHaveClass(/mtgDialogOpen/);

    await page.goBack();
    await expect(page.locator('.mtgDialogBackdrop')).not.toHaveClass(/mtgDialogOpen/);
});

test('closing without Back (the close button) pops the pushed history entry rather than leaving it behind', async ({ page }) => {
    // history.length never shrinks (browsers only truncate forward entries on a new navigation), so the
    // way to tell the dialog's pushState was actually undone is that the current entry's state is back to
    // what it was before opening, not still carrying the dialog marker a later real Back would land on.
    const harnessPath = buildWebUiHarness(PERSON_ITEM, missingWith({}));
    await openPersonPage(page, harnessPath);
    const before = await page.evaluate(() => history.state);

    await page.locator('[data-gapid="filmography:movie:1"]').click();
    const whileOpen = await page.evaluate(() => history.state);
    expect(whileOpen).toEqual({ mtgDialog: true });

    await page.locator('.mtgDialogClose').click();
    const after = await page.evaluate(() => history.state);
    expect(after).toEqual(before);
});

test('the close button is a round icon button with its cross centered by geometry', async ({ page }) => {
    await openPersonPage(page, buildWebUiHarness(PERSON_ITEM, missingWith({})));
    await page.locator('[data-gapid="filmography:movie:1"]').click();

    const close = page.locator('.mtgDialogClose');
    await expect(close).toHaveClass(/paper-icon-button-light/);
    await expect(close).toHaveText('');

    const boxes = await close.evaluate((el) => {
        const b = el.getBoundingClientRect();
        const s = el.querySelector('svg').getBoundingClientRect();
        return { bx: b.x + b.width / 2, by: b.y + b.height / 2, sx: s.x + s.width / 2, sy: s.y + s.height / 2, w: b.width, h: b.height };
    });
    expect(Math.abs(boxes.bx - boxes.sx)).toBeLessThan(1);
    expect(Math.abs(boxes.by - boxes.sy)).toBeLessThan(1);
    expect(Math.abs(boxes.w - boxes.h)).toBeLessThan(1);
});

// A link and a button in the dialog share one class list, so they take the same box model. The elements are
// created with createElement and never upgraded to emby-button, so the class is the only thing that gives
// an anchor the padding and weight a button gets.
test('every dialog button and link carries the same button classes', async ({ page }) => {
    await openPersonPage(page, buildWebUiHarness(PERSON_ITEM, missingWith({})));
    await page.locator('[data-gapid="filmography:movie:1"]').click();

    const classes = await page.locator('.mtgDialogLinks a, .mtgDialogActions button').evaluateAll(
        (els) => els.map((e) => e.className.split(/\s+/).filter((c) => /^(emby-button|raised|raised-mini|mtgActionButton)$/.test(c)).sort().join(' '))
    );
    expect(classes.length).toBeGreaterThanOrEqual(2);
    expect(new Set(classes).size).toBe(1);
    expect(classes[0]).toBe('emby-button mtgActionButton raised raised-mini');
});

test('no JustWatch link when the detail carries none', async ({ page }) => {
    const detail = {
        Title: 'A Missing Show', Kind: 'Series', TmdbId: 1399, Year: 2010,
        Tagline: null, Overview: 'A show overview.', Genres: [], RuntimeMinutes: 60, VoteAverage: null,
        Status: 'Ended', NumberOfSeasons: 8, Networks: [],
        PosterUrl: null, BackdropUrl: null, TmdbUrl: 'https://www.themoviedb.org/tv/1399', ImdbUrl: null, JustWatchUrl: null, YoutubeTrailerKey: null
    };
    await openPersonPage(page, buildWebUiHarness(PERSON_ITEM, missingWith({}), null, undefined, detail));
    await page.locator('[data-gapid="filmography:movie:1"]').click();

    await expect(page.locator('.mtgDialogOverview')).toHaveText('A show overview.');
    await expect(page.locator('.mtgDialogLinks a', { hasText: 'Search JustWatch' })).toHaveCount(0);
});
