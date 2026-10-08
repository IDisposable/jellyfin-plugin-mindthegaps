// The settings form folds into a section per heading: the plain toggles and the two sections whose
// children are already collapsed provider groups start open, the rest closed. Search opens every
// section and group holding a match. A floating button scrolls the form back to the top once it has
// been scrolled down, inside jellyfin-web's own contain: size wrapper.
const { test, expect } = require('@playwright/test');
const { buildSettingsHarness } = require('./support/build-harness');

async function open(page, config) {
    await page.goto('file://' + buildSettingsHarness(config || {}));
    await page.evaluate(() => document.querySelector('#MindTheGapsSettingsPage').dispatchEvent(new Event('pageshow')));
    await expect(page.locator('#cgSecScan')).toBeVisible();
}

const isOpen = (page, id) => page.locator('#' + id).evaluate((el) => el.open);

test('every form section is collapsible, and only the scan, sources and acquisition sections start open', async ({ page }) => {
    await open(page);
    const ids = await page.locator('#MindTheGapsConfigForm > details.cgSection').evaluateAll((els) => els.map((e) => e.id));
    expect(ids).toEqual(['cgSecScan', 'cgSecSources', 'cgSecAvailability', 'cgSecImages', 'cgSecDiagnostics', 'cgSecWebUi', 'cgSecAcquisition', 'cgSecLinks', 'cgSecRegion', 'cgSecLimits']);
    await expect(page.locator('#MindTheGapsConfigForm > .verticalSection')).toHaveCount(0);

    expect(await isOpen(page, 'cgSecScan')).toBe(true);
    expect(await isOpen(page, 'cgSecSources')).toBe(true);
    expect(await isOpen(page, 'cgSecAcquisition')).toBe(true);
    expect(await isOpen(page, 'cgSecLimits')).toBe(false);
    await expect(page.locator('#MaxRelatedPerItem')).toBeHidden();

    await page.locator('#cgSecLimits > summary').click();
    await expect(page.locator('#MaxRelatedPerItem')).toBeVisible();
    await page.locator('#cgSecLimits > summary').click();
    await expect(page.locator('#MaxRelatedPerItem')).toBeHidden();
});

test('a closed section still loads and saves its fields', async ({ page }) => {
    await open(page, { MaxRelatedPerItem: 42 });
    expect(await isOpen(page, 'cgSecLimits')).toBe(false);
    await expect(page.locator('#MaxRelatedPerItem')).toHaveValue('42');
});

test('search opens the closed section and provider group holding a match', async ({ page }) => {
    await open(page);
    expect(await isOpen(page, 'cgSecRegion')).toBe(false);
    expect(await isOpen(page, 'cgProvJustWatch')).toBe(false);

    await page.locator('#cgSettingsSearch').fill('ISO 3166');
    expect(await isOpen(page, 'cgSecRegion')).toBe(true);
    await expect(page.locator('#MetadataCountryCode')).toBeVisible();

    await page.locator('#cgSettingsSearch').fill('session token');
    expect(await isOpen(page, 'cgProvJustWatch')).toBe(true);
    await expect(page.locator('#JustWatchToken')).toBeVisible();
});

test('search reaches the Web UI placement dropdowns', async ({ page }) => {
    await open(page);
    await page.locator('#cgSettingsSearch').fill('Studio pages');
    expect(await isOpen(page, 'cgSecWebUi')).toBe(true);
    await expect(page.locator('#StudioPagePlacement')).toBeVisible();
    await expect(page.locator('#PersonPagePlacement')).toBeHidden();
});

test('the back-to-top button appears once scrolled down and returns to the top', async ({ page }) => {
    await open(page);
    const btn = page.locator('#cgSettingsScrollTop');
    await expect(btn).toBeHidden();

    // Opening every section makes the form long enough to scroll however tall the viewport is.
    await page.locator('details').evaluateAll((els) => els.forEach((d) => { d.open = true; }));
    const wrapper = page.locator('.mainAnimatedPage');
    await wrapper.evaluate((el) => { el.scrollTop = 2000; el.dispatchEvent(new Event('scroll')); });
    await expect(btn).toBeVisible();

    // contain: size makes the wrapper the button's containing block: it has to land inside the
    // wrapper's visible box, not somewhere off in the scrolled content.
    const box = await btn.boundingBox();
    const viewport = page.viewportSize();
    expect(box.y + box.height).toBeLessThanOrEqual(viewport.height);
    expect(box.x + box.width).toBeLessThanOrEqual(viewport.width);

    await btn.click();
    await expect.poll(() => wrapper.evaluate((el) => el.scrollTop)).toBe(0);
    await expect(btn).toBeHidden();
    expect(await page.evaluate(() => window.__uiTestErrors)).toEqual([]);
});
