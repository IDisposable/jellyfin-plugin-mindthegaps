// An administrator sees every user's TODO list in the report's TODO popup and manages what is on it. The
// popup reads MindTheGaps/Todo/All (every list, each entry with its owner), starts on the caller's own list,
// and lets the caller switch to another user's or to everyone's; each action names the list it is for.
const { test, expect } = require('@playwright/test');
const { openReport } = require('./support/report-page');
const { baseSummary, movieItem } = require('./support/fixtures');

const ANN = '11111111-1111-1111-1111-111111111111';
const VIC = '22222222-2222-2222-2222-222222222222';

const entry = (Id, Name, OwnerId, OwnerName, extra) => Object.assign({
    Id, Name, Year: 1999, DomainName: 'Movies', TargetKindName: 'Movie', PatternName: 'Recommendation',
    Creator: null, ProviderIds: { Tmdb: '1' }, Links: [], Done: false, AddedUtc: '2026-01-01T00:00:00Z',
    OwnerId, OwnerName
}, extra);

function todoData() {
    const items = [
        entry('a:1', 'Ann Film', ANN, 'Ann'),
        entry('v:1', 'Vic Film', VIC, 'Vic'),
        entry('v:2', 'Vic Other', VIC, 'Vic', { Done: true }),
        entry('shared', 'Shared Film', ANN, 'Ann'),
        entry('shared', 'Shared Film', VIC, 'Vic')
    ];
    return {
        CallerId: ANN,
        Owners: [
            { UserId: ANN, UserName: 'Ann', Count: 2, Open: 2 },
            { UserId: VIC, UserName: 'Vic', Count: 3, Open: 2 }
        ],
        Items: items,
        SearchUrlTemplate: '',
        GeneratedUtc: '2026-01-01T00:00:00Z'
    };
}

async function open(page, todo) {
    const summary = baseSummary({ DomainPatternCounts: { Movies: { Recommendation: 1 }, Shows: {}, Music: {}, Books: {} }, TotalGaps: 1 });
    await openReport(page, summary, { Movies: [movieItem()] }, todo);
    await page.locator('#cgTodoBtn').click();
    await expect(page.locator('#cgTodoModal')).toBeVisible();
    await expect(page.locator('#cgTodoBody .cgTodoRow').first()).toBeVisible();
}

const titles = (page) => page.locator('#cgTodoBody .cgTodoTitle').allInnerTexts();
const calls = (page) => page.evaluate(() => window.__todoCalls);

test('opens on the caller\'s own list, with a chooser once others have lists', async ({ page }) => {
    await open(page, todoData());

    await expect(page.locator('#cgTodoTitle')).toHaveText('My TODO list');
    await expect(page.locator('#cgTodoWhoRow')).toBeVisible();
    expect((await titles(page)).sort()).toEqual(['Ann Film (1999)', 'Shared Film (1999)']);
    await expect(page.locator('#cgTodoWho option')).toHaveText(['My list (2)', 'Vic (3)', 'Everyone (5)']);
});

test('the chooser is not shown when only the caller has a list', async ({ page }) => {
    const data = todoData();
    data.Owners = [data.Owners[0]];
    data.Items = data.Items.filter((it) => it.OwnerId === ANN);
    await open(page, data);

    await expect(page.locator('#cgTodoWhoRow')).toBeHidden();
    await expect(page.locator('#cgTodoTitle')).toHaveText('My TODO list');
});

test('another user\'s list shows their entries under their name', async ({ page }) => {
    await open(page, todoData());

    await page.locator('#cgTodoWho').selectOption(VIC);

    await expect(page.locator('#cgTodoTitle')).toHaveText("Vic's TODO list");
    expect((await titles(page)).sort()).toEqual(['Shared Film (1999)', 'Vic Film (1999)', 'Vic Other (1999)']);
});

test('everyone\'s lists show together, each entry saying whose list it is on', async ({ page }) => {
    await open(page, todoData());

    await page.locator('#cgTodoWho').selectOption('*');

    await expect(page.locator('#cgTodoTitle')).toHaveText("Everyone's TODO lists");
    await expect(page.locator('#cgTodoBody .cgTodoRow')).toHaveCount(5);
    await expect(page.locator('#cgTodoBody .cgTodoOwner')).toHaveCount(5);
    await expect(page.locator('#cgTodoBody .cgTodoRow[data-id="a:1"] .cgTodoOwner')).toHaveText("On Ann's list");
    await expect(page.locator('#cgTodoBody .cgTodoRow[data-id="v:1"] .cgTodoOwner')).toHaveText("On Vic's list");
});

test('a list that is not the caller\'s shows no owner note', async ({ page }) => {
    await open(page, todoData());
    await expect(page.locator('#cgTodoBody .cgTodoOwner')).toHaveCount(0);
});

test('deleting an entry from someone else\'s list names that user, and only that entry goes', async ({ page }) => {
    await open(page, todoData());
    await page.locator('#cgTodoWho').selectOption('*');

    await page.locator('#cgTodoBody .cgTodoRow[data-id="shared"][data-owner="' + VIC + '"] .cgTodoDelete').click();

    await expect(page.locator('#cgTodoBody .cgTodoRow')).toHaveCount(4);
    await expect(page.locator('#cgTodoBody .cgTodoRow[data-id="shared"][data-owner="' + ANN + '"]')).toHaveCount(1);
    await expect(page.locator('#cgTodoBody .cgTodoRow[data-id="shared"][data-owner="' + VIC + '"]')).toHaveCount(0);
    expect((await calls(page)).some((u) => /Todo\/Remove/.test(u) && u.includes('userId=' + VIC) && u.includes('id=shared'))).toBe(true);
    await expect(page.locator('#cgTodoWho option')).toHaveText(['My list (2)', 'Vic (2)', 'Everyone (4)']);
});

test('ticking an entry on someone else\'s list names that user', async ({ page }) => {
    await open(page, todoData());
    await page.locator('#cgTodoWho').selectOption(VIC);

    await page.locator('#cgTodoBody .cgTodoRow[data-id="v:1"] .cgTodoDoneBox').check();

    await expect(page.locator('#cgTodoBody .cgTodoRow[data-id="v:1"]')).toHaveClass(/cgTodoDone/);
    expect((await calls(page)).some((u) => /Todo\/SetDone/.test(u) && u.includes('userId=' + VIC) && u.includes('done=true'))).toBe(true);
});

test('verifying one entry names its owner', async ({ page }) => {
    await open(page, todoData());
    await page.locator('#cgTodoWho').selectOption(VIC);

    await page.locator('#cgTodoBody .cgTodoRow[data-id="v:1"] .cgTodoVerify').click();

    await expect(page.locator('#cgTodoBody .cgTodoRow[data-id="v:1"] .cgTodoNote')).toHaveText('Not in your library yet.');
    expect((await calls(page)).some((u) => /Todo\/Verify\?/.test(u) && u.includes('userId=' + VIC))).toBe(true);
});

test('Verify all checks the list in view, or every list under everyone', async ({ page }) => {
    await open(page, todoData());
    await page.locator('#cgTodoWho').selectOption(VIC);
    await page.locator('#cgTodoVerifyAll').click();
    await expect.poll(async () => (await calls(page)).filter((u) => /Todo\/VerifyAll/.test(u)).length).toBe(1);
    expect((await calls(page)).filter((u) => /Todo\/VerifyAll/.test(u))[0]).toContain('userId=' + VIC);

    await page.locator('#cgTodoWho').selectOption('*');
    await page.locator('#cgTodoVerifyAll').click();
    await expect.poll(async () => (await calls(page)).filter((u) => /Todo\/VerifyAll/.test(u)).length).toBe(3);
    const all = (await calls(page)).filter((u) => /Todo\/VerifyAll/.test(u)).slice(1);
    expect(all.some((u) => u.includes('userId=' + ANN))).toBe(true);
    expect(all.some((u) => u.includes('userId=' + VIC))).toBe(true);
});

test('the chosen list stays chosen after Verify all reloads it', async ({ page }) => {
    await open(page, todoData());
    await page.locator('#cgTodoWho').selectOption(VIC);

    await page.locator('#cgTodoVerifyAll').click();
    await expect.poll(async () => (await calls(page)).filter((u) => /Todo\/All/.test(u)).length).toBe(2);

    await expect(page.locator('#cgTodoTitle')).toHaveText("Vic's TODO list");
    await expect(page.locator('#cgTodoWho')).toHaveValue(VIC);
});

test('the export covers the list in view and names the owner under everyone', async ({ page }) => {
    await open(page, todoData());
    await page.locator('#cgTodoWho').selectOption('*');

    const download = page.waitForEvent('download');
    await page.locator('#cgTodoExport').click();
    const file = await download;
    let text = '';
    for await (const chunk of await file.createReadStream()) { text += chunk; }

    expect(text).toContain("# Mind the Gaps: Everyone's TODO lists");
    expect(text).toContain("Vic Film \\(1999\\) \\(on Vic's list\\)");
    expect(text).toContain("Ann Film \\(1999\\) \\(on Ann's list\\)");
});

test('an empty list says whose it is', async ({ page }) => {
    const data = todoData();
    data.Owners = [{ UserId: ANN, UserName: 'Ann', Count: 0, Open: 0 }, { UserId: VIC, UserName: 'Vic', Count: 0, Open: 0 }];
    data.Items = [];
    const summary = baseSummary({ DomainPatternCounts: { Movies: { Recommendation: 1 }, Shows: {}, Music: {}, Books: {} }, TotalGaps: 1 });
    await openReport(page, summary, { Movies: [movieItem()] }, data);
    await page.locator('#cgTodoBtn').click();

    await expect(page.locator('#cgTodoBody .cgTodoEmpty')).toContainText('Your TODO list is empty');
    await page.locator('#cgTodoWho').selectOption(VIC);
    await expect(page.locator('#cgTodoBody .cgTodoEmpty')).toHaveText('Vic has nothing on their TODO list.');
    await page.locator('#cgTodoWho').selectOption('*');
    await expect(page.locator('#cgTodoBody .cgTodoEmpty')).toHaveText('Nobody has anything on their TODO list yet.');
});
