// Each MDBList chip carries a Public toggle, private until pressed. The saved config holds the public
// subset beside the list ids, which is what lets a list's titles reach the home Discover row.
const { test, expect } = require('@playwright/test');
const { buildSettingsHarness } = require('./support/build-harness');

async function open(page, config) {
    await page.goto('file://' + buildSettingsHarness(config));
    await page.evaluate(() => document.querySelector('#MindTheGapsSettingsPage').dispatchEvent(new Event('pageshow')));
    await page.locator('#cgProvMdbList > summary').click();
}

const publicToggle = (page, name) => page.getByRole('button', { name: 'Public: ' + name });

test('a saved public list loads pressed, the others not', async ({ page }) => {
    await open(page, { MdbListListIds: '10,20', MdbListPublicListIds: '20' });

    await expect(publicToggle(page, 'List 20')).toHaveAttribute('aria-pressed', 'true');
    await expect(publicToggle(page, 'List 20')).toHaveText('Public');
    await expect(publicToggle(page, 'List 10')).toHaveAttribute('aria-pressed', 'false');
    await expect(publicToggle(page, 'List 10')).toHaveText('Private');
});

test('pressing the toggle flips it, keeps focus on it, and saves the public subset', async ({ page }) => {
    await open(page, { MdbListListIds: '10,20', MdbListPublicListIds: '20' });

    await publicToggle(page, 'List 10').click();
    await expect(publicToggle(page, 'List 10')).toHaveAttribute('aria-pressed', 'true');
    await expect(publicToggle(page, 'List 10')).toBeFocused();
    await publicToggle(page, 'List 20').click();
    await expect(publicToggle(page, 'List 20')).toHaveAttribute('aria-pressed', 'false');

    await page.evaluate(() => document.querySelector('#MindTheGapsConfigForm').requestSubmit());
    await expect.poll(() => page.evaluate(() => window.__savedConfig && window.__savedConfig.MdbListPublicListIds)).toBe('10');
    expect(await page.evaluate(() => window.__savedConfig.MdbListListIds)).toBe('10,20');
    expect(await page.evaluate(() => window.__uiTestErrors)).toEqual([]);
});

test('removing a public list takes it out of the public subset', async ({ page }) => {
    await open(page, { MdbListListIds: '10,20', MdbListPublicListIds: '10,20' });

    await page.getByRole('button', { name: 'Remove List 10' }).click();
    await page.evaluate(() => document.querySelector('#MindTheGapsConfigForm').requestSubmit());
    await expect.poll(() => page.evaluate(() => window.__savedConfig && window.__savedConfig.MdbListPublicListIds)).toBe('20');
});
