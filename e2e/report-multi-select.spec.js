// The multi-select bar's bulk actions: Mint and Add to TODO are pre-existing; this pins the newer
// Acquire/Request/Resolve trio, which reuse the same checked-row selection (selectedGapIds) the other
// two already use. Acquire/Request only appear once their own acquisition target is configured
// (AcquisitionConfig), the same rule a per-row Send button already follows.
const { test, expect } = require('@playwright/test');
const { openReport } = require('./support/report-page');
const { baseSummary, movieItem } = require('./support/fixtures');

function twoMovies() {
    return [
        movieItem({ Id: 'justwatch:watchlist:164', Name: "Breakfast at Tiffany's" }),
        movieItem({ Id: 'justwatch:watchlist:200', Name: 'Another Movie', ProviderIds: { Tmdb: '200' } })
    ];
}

async function selectAll(page) {
    await page.locator('#cgSelectAll').click();
}

test('Acquire and Request stay hidden until their target is configured', async ({ page }) => {
    const summary = baseSummary({ DomainPatternCounts: { Movies: { Recommendation: 2 }, Shows: {}, Music: {}, Books: {} }, TotalGaps: 2, MintableKinds: { Movie: 'Tmdb' } });
    await openReport(page, summary, { Movies: twoMovies() });
    await selectAll(page);

    await expect(page.locator('#cgSendArrSelected')).toBeHidden();
    await expect(page.locator('#cgSendSeerrSelected')).toBeHidden();
    // Mint/Todo/Resolve have no such gate: they show regardless of acquisition configuration.
    await expect(page.locator('#cgMintSelected')).toBeEnabled();
    await expect(page.locator('#cgTodoSelected')).toBeEnabled();
    await expect(page.locator('#cgResolveSelected')).toBeEnabled();
});

test('Acquire selected sends every checked row to the acquisition pipeline', async ({ page }) => {
    const summary = baseSummary({ DomainPatternCounts: { Movies: { Recommendation: 2 }, Shows: {}, Music: {}, Books: {} }, TotalGaps: 2, MintableKinds: { Movie: 'Tmdb' } });
    await openReport(page, summary, { Movies: twoMovies() }, undefined, undefined, undefined, { RadarrConfigured: true });
    await selectAll(page);

    page.once('dialog', (d) => d.accept());
    await page.locator('#cgSendArrSelected').click();

    const calls = await page.evaluate(() => window.__bulkCalls);
    expect(calls).toHaveLength(1);
    expect(calls[0].url).toContain('SendToArrBulk');
    // Row order on screen is whatever the tree groups/sorts to, not insertion order, so compare as a set.
    expect(JSON.parse(calls[0].body).sort()).toEqual(['justwatch:watchlist:164', 'justwatch:watchlist:200'].sort());
});

test('declining the confirm sends nothing', async ({ page }) => {
    const summary = baseSummary({ DomainPatternCounts: { Movies: { Recommendation: 2 }, Shows: {}, Music: {}, Books: {} }, TotalGaps: 2, MintableKinds: { Movie: 'Tmdb' } });
    await openReport(page, summary, { Movies: twoMovies() }, undefined, undefined, undefined, { RadarrConfigured: true });
    await selectAll(page);

    page.once('dialog', (d) => d.dismiss());
    await page.locator('#cgSendArrSelected').click();

    expect(await page.evaluate(() => window.__bulkCalls)).toHaveLength(0);
});

test('Request selected posts to the request pipeline, independent of Acquire', async ({ page }) => {
    const summary = baseSummary({ DomainPatternCounts: { Movies: { Recommendation: 2 }, Shows: {}, Music: {}, Books: {} }, TotalGaps: 2, MintableKinds: { Movie: 'Tmdb' } });
    await openReport(page, summary, { Movies: twoMovies() }, undefined, undefined, undefined, { SeerrConfigured: true });
    await selectAll(page);

    await expect(page.locator('#cgSendArrSelected')).toBeHidden();
    await expect(page.locator('#cgSendSeerrSelected')).toBeVisible();

    page.once('dialog', (d) => d.accept());
    await page.locator('#cgSendSeerrSelected').click();

    const calls = await page.evaluate(() => window.__bulkCalls);
    expect(calls).toHaveLength(1);
    expect(calls[0].url).toContain('SendToSeerrBulk');
});

test('Resolve selected asks for one note and applies it to every checked row', async ({ page }) => {
    const summary = baseSummary({ DomainPatternCounts: { Movies: { Recommendation: 2 }, Shows: {}, Music: {}, Books: {} }, TotalGaps: 2, MintableKinds: { Movie: 'Tmdb' } });
    await openReport(page, summary, { Movies: twoMovies() });
    await selectAll(page);

    page.once('dialog', (d) => d.accept('Owned on physical media'));
    await page.locator('#cgResolveSelected').click();

    const calls = await page.evaluate(() => window.__bulkCalls);
    expect(calls).toHaveLength(1);
    expect(calls[0].url).toContain('ResolveBatch');
    const body = JSON.parse(calls[0].body);
    expect(body.Ids.sort()).toEqual(['justwatch:watchlist:164', 'justwatch:watchlist:200'].sort());
    expect(body.Note).toBe('Owned on physical media');
    expect(body.Kind).toBeNull();
});

test('cancelling the resolve prompt (dismiss) sends nothing', async ({ page }) => {
    const summary = baseSummary({ DomainPatternCounts: { Movies: { Recommendation: 2 }, Shows: {}, Music: {}, Books: {} }, TotalGaps: 2, MintableKinds: { Movie: 'Tmdb' } });
    await openReport(page, summary, { Movies: twoMovies() });
    await selectAll(page);

    page.once('dialog', (d) => d.dismiss());
    await page.locator('#cgResolveSelected').click();

    expect(await page.evaluate(() => window.__bulkCalls)).toHaveLength(0);
});
