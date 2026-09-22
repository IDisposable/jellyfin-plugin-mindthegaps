// The Maintenance section's fulfillment queue: MindTheGaps/Todo/Demand already folds every user's TODO
// entries into one row per title (Gaps/TodoDemandAggregator.cs), so the client only renders it, offers the
// lazy where-to-watch lookup the report list already has, and posts MindTheGaps/Todo/Demand/MarkFetched to
// close a title out for every requester at once. This is the surface an administrator without a
// Radarr/Sonarr/Jellyseerr setup uses to see what is actually wanted before fetching it themselves.
const { test, expect } = require('@playwright/test');
const { openReport } = require('./support/report-page');
const { baseSummary, movieItem } = require('./support/fixtures');

const ANN = '11111111-1111-1111-1111-111111111111';
const VIC = '22222222-2222-2222-2222-222222222222';

const row = (Id, Name, extra) => Object.assign({
    Id, Name, Year: 1999, DomainName: 'Movies', TargetKindName: 'Movie', PatternName: 'Recommendation',
    Creator: null, ImageUrl: null, ReleaseDate: null, ProviderIds: { Tmdb: '1' }, Links: [],
    RequestCount: 1, OpenCount: 1, RequestedBy: ['Ann'], Entries: [{ OwnerId: ANN, GapId: Id }]
}, extra);

function demandData() {
    return {
        Items: [
            row('both', 'Both Want It', {
                RequestCount: 2, OpenCount: 2, RequestedBy: ['Ann', 'Vic'],
                Entries: [{ OwnerId: ANN, GapId: 'both' }, { OwnerId: VIC, GapId: 'both' }]
            }),
            row('solo', 'Solo Want'),
            row('done', 'Already Fetched', { OpenCount: 0 })
        ],
        SearchUrlTemplate: '',
        GeneratedUtc: '2026-01-01T00:00:00Z'
    };
}

async function open(page, demand) {
    const summary = baseSummary({ DomainPatternCounts: { Movies: { Recommendation: 1 }, Shows: {}, Music: {}, Books: {} }, TotalGaps: 1 });
    await openReport(page, summary, { Movies: [movieItem()] }, null, null, demand);
    await page.locator('#cgFulfillBtn').click();
    await expect(page.locator('#cgFulfillModal')).toBeVisible();
    await expect(page.locator('#cgFulfillBody .cgTodoRow').first()).toBeVisible();
}

const titles = (page) => page.locator('#cgFulfillBody .cgTodoTitle').allInnerTexts();

test('opens from the Maintenance section, sorted by demand, with who still wants each title', async ({ page }) => {
    await open(page, demandData());

    expect(await titles(page)).toEqual(['Both Want It (1999)', 'Solo Want (1999)']);
    await expect(page.locator('#cgFulfillBody .cgTodoRow[data-rowid="both"] .cgTodoOwner'))
        .toHaveText('Wanted by Ann, Vic (2 of 2 still open)');
});

test('a fully fetched title is hidden until Show fulfilled is ticked', async ({ page }) => {
    await open(page, demandData());
    await expect(page.locator('#cgFulfillBody .cgTodoRow')).toHaveCount(2);

    await page.locator('#cgFulfillShowDone').check();

    await expect(page.locator('#cgFulfillBody .cgTodoRow')).toHaveCount(3);
    await expect(page.locator('#cgFulfillBody .cgTodoRow[data-rowid="done"]')).toHaveClass(/cgTodoDone/);
    await expect(page.locator('#cgFulfillBody .cgTodoRow[data-rowid="done"] .cgTodoActions')).toHaveText('Fulfilled');
});

test('Mark fetched closes a title out for every requester in one action', async ({ page }) => {
    await open(page, demandData());

    await page.locator('#cgFulfillBody .cgTodoRow[data-rowid="both"] .cgFulfillDone').click();

    await expect.poll(() => page.evaluate(() => window.__markFetchedCalls.length)).toBe(1);
    const posted = await page.evaluate(() => window.__markFetchedCalls[0]);
    expect(posted.sort((a, b) => a.OwnerId.localeCompare(b.OwnerId))).toEqual([
        { OwnerId: ANN, GapId: 'both' },
        { OwnerId: VIC, GapId: 'both' }
    ].sort((a, b) => a.OwnerId.localeCompare(b.OwnerId)));
    // Fully fetched, so it drops out of view without ticking Show fulfilled.
    expect(await titles(page)).toEqual(['Solo Want (1999)']);
});

test('looking up where to watch uses the same lazy lookup the report list uses', async ({ page }) => {
    await open(page, demandData());

    await page.locator('#cgFulfillBody .cgTodoRow[data-rowid="solo"] .cgWatch').click();

    await expect(page.locator('#cgFulfillBody .cgTodoRow[data-rowid="solo"] .cgAvail')).toContainText('Netflix');
    const calls = await page.evaluate(() => window.__availabilityCalls);
    expect(calls.some((u) => u.includes('tmdbId=1') && u.includes('targetKind=Movie'))).toBe(true);
});

test('an empty queue says so', async ({ page }) => {
    await open(page, { Items: [], SearchUrlTemplate: '', GeneratedUtc: '2026-01-01T00:00:00Z' });
});

test('an empty queue with only fulfilled titles distinguishes the two empty states', async ({ page }) => {
    const summary = baseSummary({ DomainPatternCounts: { Movies: { Recommendation: 1 }, Shows: {}, Music: {}, Books: {} }, TotalGaps: 1 });
    await openReport(page, summary, { Movies: [movieItem()] }, null, null, { Items: [row('done', 'Already Fetched', { OpenCount: 0 })], SearchUrlTemplate: '', GeneratedUtc: '2026-01-01T00:00:00Z' });
    await page.locator('#cgFulfillBtn').click();
    await expect(page.locator('#cgFulfillModal')).toBeVisible();

    await expect(page.locator('#cgFulfillBody .cgTodoEmpty')).toContainText('Nothing outstanding');
});

test('closes via the close button, a backdrop click, and Escape', async ({ page }) => {
    await open(page, demandData());

    await page.locator('#cgFulfillClose').click();
    await expect(page.locator('#cgFulfillModal')).toBeHidden();

    await page.locator('#cgFulfillBtn').click();
    await expect(page.locator('#cgFulfillModal')).toBeVisible();
    await page.locator('#cgFulfillModal').click({ position: { x: 5, y: 5 } });
    await expect(page.locator('#cgFulfillModal')).toBeHidden();

    await page.locator('#cgFulfillBtn').click();
    await expect(page.locator('#cgFulfillModal')).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(page.locator('#cgFulfillModal')).toBeHidden();
});
