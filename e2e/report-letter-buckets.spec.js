// A section with many groups (titles recommending things, creators, sets) files them under collapsible
// letter buckets. With every letter shown a bucket is a first letter; with one letter chosen in the A-Z bar
// it is the next one ("Ab", "Al"), so the chosen letter's long list still splits.
const { test, expect } = require('@playwright/test');
const { buildHarness } = require('./support/build-harness');
const { baseSummary, movieItem } = require('./support/fixtures');

// Three recommendations from each owned title, named across A and B so both layers have something to split.
const OWNED = ['Absolutely Fabulous', 'Agent Carter', 'Ahsoka', 'Alien: Earth', 'Alphas', 'Altered Carbon',
    'Amadeus', 'American Dad!', 'Andor', 'The Americans', 'Angel', 'Archer', 'Arrested Development',
    'Atlanta', 'Avenue 5', 'Ally McBeal', 'Alias', 'Atypical', 'Arcane', 'Abbott Elementary', 'Ash vs Evil Dead',
    'Army of Darkness', 'Apollo 13', 'Avatar', 'Aliens', 'Amélie', 'Annie Hall', 'Apocalypse Now', 'Airplane!',
    'Aladdin', 'Babylon 5', 'Band of Brothers', 'Barry', 'Battlestar Galactica', 'Better Call Saul',
    'Black Mirror', 'Blue Bloods', 'Bones', 'Borgen', 'Breaking Bad', 'Broadchurch', 'Brooklyn Nine-Nine',
    'Bluey', 'Banshee', 'Berlin Station', 'Bodies'];

function items() {
    const out = [];
    OWNED.forEach((owned, i) => {
        for (let n = 0; n < 3; n++) {
            out.push(movieItem({
                Id: 'rec:' + i + ':' + n,
                Name: 'Suggestion ' + i + '-' + n,
                SourceItemType: 'Movie',
                SourceItemId: 'owned' + i,
                SourceItemName: owned
            }));
        }
    });
    return out;
}

async function open(page) {
    const recs = items();
    const summary = baseSummary({ DomainPatternCounts: { Movies: { Recommendation: recs.length }, Shows: {}, Music: {}, Books: {} }, TotalGaps: recs.length, DiscoverKinds: ['Movie'], MintableKinds: { Movie: 'Tmdb' } });
    await page.goto('file://' + buildHarness(summary, { Movies: recs }));
    await page.evaluate(() => document.querySelector('#MindTheGapsPage').dispatchEvent(new Event('pageshow')));
    await page.waitForSelector('#cgList .cgGroup');
}

const bucketLabels = (page) => page.locator('#cgList .cgLLetter > .cgHdr > .cgLabel').allTextContents();

test('with every letter shown, a long section is split by first letter, each built on opening', async ({ page }) => {
    await open(page);

    expect(await bucketLabels(page)).toEqual(['A', 'B', 'T']);
    await expect(page.locator('#cgList .cgL2')).toHaveCount(0);

    await page.locator('#cgList .cgLLetter[data-cglabel="B"] > .cgHdr').click();
    const sources = await page.locator('#cgList .cgLLetter[data-cglabel="B"] .cgL2 > .cgHdr > .cgLabel').allTextContents();
    expect(sources[0]).toBe('Babylon 5');
    expect(sources).toHaveLength(16);
});

test('with one letter chosen, the buckets split on the next letter, "The" titles by the following word', async ({ page }) => {
    await open(page);
    await page.locator('.cgJumpL[data-l="A"]').click();

    expect(await bucketLabels(page)).toEqual(['Ab', 'Ag', 'Ah', 'Ai', 'Al', 'Am', 'An', 'Ap', 'Ar', 'As', 'At', 'Av']);

    await page.locator('#cgList .cgLLetter[data-cglabel="Am"] > .cgHdr').click();
    const sources = await page.locator('#cgList .cgLLetter[data-cglabel="Am"] .cgL2 > .cgHdr > .cgLabel').allTextContents();
    expect(sources).toEqual(['Amadeus', 'American Dad!', 'Amélie', 'The Americans']);
});

test('a short section lists its groups directly', async ({ page }) => {
    await open(page);
    await page.locator('.cgJumpL[data-l="B"]').click();

    await expect(page.locator('#cgList .cgLLetter')).toHaveCount(0);
    await expect(page.locator('#cgList .cgL2')).toHaveCount(16);
});

test('an opened bucket and the group inside it stay open across a re-render', async ({ page }) => {
    await open(page);
    await page.locator('#cgList .cgLLetter[data-cglabel="A"] > .cgHdr').click();
    await page.locator('#cgList .cgL2[data-cglabel="Andor"] > .cgHdr').click();
    await expect(page.locator('#cgList .cgL2[data-cglabel="Andor"] .cgRow')).toHaveCount(3);

    await page.locator('#cgSort').evaluate((el) => el.dispatchEvent(new Event('change', { bubbles: true })));

    await expect(page.locator('#cgList .cgLLetter[data-cglabel="A"]')).not.toHaveClass(/cgCollapsed/);
    await expect(page.locator('#cgList .cgL2[data-cglabel="Andor"] .cgRow')).toHaveCount(3);
});

test('select all reaches the rows inside unopened buckets, without building them', async ({ page }) => {
    await open(page);
    await page.locator('#cgRollup .cgGrpSel').check();

    await expect(page.locator('#cgTodoSelCount')).toHaveText(String(items().length));
    await expect(page.locator('#cgList .cgRow')).toHaveCount(0);

    await page.locator('#cgList .cgLLetter[data-cglabel="B"] > .cgHdr').click();
    await page.locator('#cgList .cgL2[data-cglabel="Bones"] > .cgHdr').click();
    await expect(page.locator('#cgList .cgL2[data-cglabel="Bones"] .cgSel:checked')).toHaveCount(3);
});
