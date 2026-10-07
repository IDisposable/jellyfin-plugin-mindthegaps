// Selection is kept apart from the DOM: every row has a checkbox, every section and group header has a
// select-all over the rows under it (built or not), and the rollup line's selects everything shown. A header
// reads checked, clear, or indeterminate from the selection. Separately, the filter hint says how many rows
// the search and Hide filters are hiding, so a forgotten search does not read as an empty tab.
const { test, expect } = require('@playwright/test');
const { buildHarness } = require('./support/build-harness');
const { baseSummary, movieItem } = require('./support/fixtures');

function items() {
    const rec = (id, name, source, extra) => movieItem(Object.assign({
        Id: id, Name: name, SourceItemType: 'Movie', SourceItemId: 'owned-' + source, SourceItemName: source
    }, extra));
    return [
        rec('a1', 'Star Wars', 'Alien'),
        rec('a2', 'Starman', 'Alien'),
        rec('a3', 'Solaris', 'Alien', { ProviderIds: { Imdb: 'tt0069293' } }),
        rec('b1', 'Brazil', 'Blade Runner')
    ];
}

async function open(page) {
    const recs = items();
    const summary = baseSummary({
        DomainPatternCounts: { Movies: { Recommendation: recs.length }, Shows: {}, Music: {}, Books: {} },
        TotalGaps: recs.length, DiscoverKinds: ['Movie'], MintableKinds: { Movie: 'Tmdb' }
    });
    await page.goto('file://' + buildHarness(summary, { Movies: recs }));
    await page.evaluate(() => document.querySelector('#MindTheGapsPage').dispatchEvent(new Event('pageshow')));
    await page.waitForSelector('#cgList .cgGroup');
}

const group = (page, name) => page.locator(`#cgList .cgL2[data-cglabel="${name}"]`);

test('every row has a checkbox, and the bar appears only once something is selected', async ({ page }) => {
    await open(page);
    await expect(page.locator('#cgSelectBar')).toBeHidden();

    await group(page, 'Blade Runner').locator('> .cgHdr').click();
    await expect(group(page, 'Blade Runner').locator('.cgSel')).toHaveCount(1);
    await group(page, 'Blade Runner').locator('.cgSel').check();

    await expect(page.locator('#cgSelectBar')).toBeVisible();

    // Partly checked, the master selects everything; a second click clears it all.
    const master = page.locator('#cgRollup .cgGrpSel');
    await master.click();
    await expect(page.locator('#cgTodoSelCount')).toHaveText('4');
    await master.click();
    await expect(page.locator('#cgSelectBar')).toBeHidden();
    await expect(group(page, 'Blade Runner').locator('.cgSel')).not.toBeChecked();
});

test('a group checkbox selects its unbuilt rows without expanding it, and reads partial once one is cleared', async ({ page }) => {
    await open(page);
    const alien = group(page, 'Alien');
    await alien.locator('> .cgHdr .cgGrpSel').check();

    await expect(alien).toHaveClass(/cgCollapsed/);
    await expect(page.locator('#cgTodoSelCount')).toHaveText('3');
    expect(await page.locator('#cgRollup .cgGrpSel').evaluate((el) => el.indeterminate)).toBe(true);

    await alien.locator('> .cgHdr').click();
    await expect(alien.locator('.cgSel:checked')).toHaveCount(3);
    await alien.locator('.cgSel').first().uncheck();
    expect(await alien.locator('> .cgHdr .cgGrpSel').evaluate((el) => el.indeterminate)).toBe(true);
    await expect(page.locator('#cgTodoSelCount')).toHaveText('2');
});

test('Mint counts only the selected rows that can be minted', async ({ page }) => {
    await open(page);
    await page.locator('#cgRollup .cgGrpSel').check();

    await expect(page.locator('#cgTodoSelCount')).toHaveText('4');
    await expect(page.locator('#cgSelCount')).toHaveText('3');
});

test('the filter hint names what is hiding rows, and Show them brings them back', async ({ page }) => {
    await open(page);
    await expect(page.locator('#cgFilterHint')).toBeHidden();

    await page.locator('#cgSearch').fill('star');
    await expect(page.locator('#cgFilterHint')).toContainText('2 hidden by the search “star”.');

    await page.locator('#cgShowFiltered').click();
    await expect(page.locator('#cgSearch')).toHaveValue('');
    await expect(page.locator('#cgFilterHint')).toBeHidden();
    await expect(page.locator('#cgList .cgL2')).toHaveCount(2);
});

test('a selection a filter hides is dropped, so a bulk action cannot reach it', async ({ page }) => {
    await open(page);
    await page.locator('#cgRollup .cgGrpSel').check();
    await page.locator('#cgSearch').fill('brazil');

    await expect(page.locator('#cgTodoSelCount')).toHaveText('1');
});
