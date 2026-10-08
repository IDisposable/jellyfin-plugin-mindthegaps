// The settings form folds into a section per heading: the plain toggles and the two sections whose
// children are already collapsed provider groups start open, the rest closed. Each section's toggle
// is a button inside a real h2, so the headings stay headings to a screen reader. Search opens every
// section and group holding a match and announces how many matched. Each field is described by its
// help text, and each reveal button names the secret it shows. A floating button scrolls the form
// back to the top once it has been scrolled down, inside jellyfin-web's own contain: size wrapper.
const { test, expect } = require('@playwright/test');
const { buildSettingsHarness } = require('./support/build-harness');

async function open(page, config) {
    await page.goto('file://' + buildSettingsHarness(config || {}));
    await page.evaluate(() => document.querySelector('#MindTheGapsSettingsPage').dispatchEvent(new Event('pageshow')));
    await expect(page.locator('#cgSecScan')).toBeVisible();
}

const toggle = (page, id) => page.locator('#' + id + ' > h2 > .cgSectionToggle');
const sectionOpen = async (page, id) => (await toggle(page, id).getAttribute('aria-expanded')) === 'true';
const groupOpen = (page, id) => page.locator('#' + id).evaluate((el) => el.open);

test('every form section is collapsible, and only the scan, sources and acquisition sections start open', async ({ page }) => {
    await open(page);
    const ids = await page.locator('#MindTheGapsConfigForm > section.cgSection').evaluateAll((els) => els.map((e) => e.id));
    expect(ids).toEqual(['cgSecScan', 'cgSecSources', 'cgSecAvailability', 'cgSecImages', 'cgSecDiagnostics', 'cgSecWebUi', 'cgSecAcquisition', 'cgSecLinks', 'cgSecRegion', 'cgSecLimits']);
    await expect(page.locator('#MindTheGapsConfigForm > .verticalSection')).toHaveCount(0);

    expect(await sectionOpen(page, 'cgSecScan')).toBe(true);
    expect(await sectionOpen(page, 'cgSecSources')).toBe(true);
    expect(await sectionOpen(page, 'cgSecAcquisition')).toBe(true);
    expect(await sectionOpen(page, 'cgSecLimits')).toBe(false);
    await expect(page.locator('#MaxRelatedPerItem')).toBeHidden();

    await toggle(page, 'cgSecLimits').click();
    expect(await sectionOpen(page, 'cgSecLimits')).toBe(true);
    await expect(page.locator('#MaxRelatedPerItem')).toBeVisible();
    await toggle(page, 'cgSecLimits').click();
    expect(await sectionOpen(page, 'cgSecLimits')).toBe(false);
    await expect(page.locator('#MaxRelatedPerItem')).toBeHidden();
});

test('a section heading stays a heading, and its toggle works from the keyboard', async ({ page }) => {
    await open(page);
    await expect(page.getByRole('heading', { level: 2, name: 'Limits' })).toHaveCount(1);
    await expect(page.getByRole('button', { name: 'Limits', expanded: false })).toHaveCount(1);

    await toggle(page, 'cgSecLimits').focus();
    await page.keyboard.press('Enter');
    await expect(page.getByRole('button', { name: 'Limits', expanded: true })).toHaveCount(1);
    await expect(page.locator('#MaxRelatedPerItem')).toBeVisible();
});

test('a closed section still loads and saves its fields', async ({ page }) => {
    await open(page, { MaxRelatedPerItem: 42 });
    expect(await sectionOpen(page, 'cgSecLimits')).toBe(false);
    await expect(page.locator('#MaxRelatedPerItem')).toHaveValue('42');
});

test('search opens the closed section and provider group holding a match, and announces the count', async ({ page }) => {
    await open(page);
    expect(await sectionOpen(page, 'cgSecRegion')).toBe(false);
    expect(await groupOpen(page, 'cgProvJustWatch')).toBe(false);

    await page.locator('#cgSettingsSearch').fill('ISO 3166');
    expect(await sectionOpen(page, 'cgSecRegion')).toBe(true);
    await expect(page.locator('#MetadataCountryCode')).toBeVisible();
    await expect(page.locator('#cgSettingsSearchStatus')).toHaveText('1 setting matches.');

    await page.locator('#cgSettingsSearch').fill('session token');
    expect(await groupOpen(page, 'cgProvJustWatch')).toBe(true);
    await expect(page.locator('#JustWatchToken')).toBeVisible();

    await page.locator('#cgSettingsSearch').fill('zzzz no such setting');
    await expect(page.locator('#cgSettingsSearchStatus')).toHaveText('No settings match.');
    await page.locator('#cgSettingsSearch').fill('');
    await expect(page.locator('#cgSettingsSearchStatus')).toHaveText('');
});

test('search reaches the Web UI placement dropdowns', async ({ page }) => {
    await open(page);
    await page.locator('#cgSettingsSearch').fill('Studio pages');
    expect(await sectionOpen(page, 'cgSecWebUi')).toBe(true);
    await expect(page.locator('#StudioPagePlacement')).toBeVisible();
    await expect(page.locator('#PersonPagePlacement')).toBeHidden();
});

test('a field is described by the help text beneath it', async ({ page }) => {
    await open(page);
    await expect(page.locator('#DetailedApiLogging')).toHaveAccessibleDescription(/api keys and tokens are never logged/);
    await expect(page.locator('#StudioPagePlacement')).toHaveAccessibleDescription(/Movies only/);
    await expect(page.locator('#MaxRelatedPerItem')).toHaveAccessibleDescription(/Caps how many similar titles/);
    // A field with no help text of its own gets no description.
    expect(await page.locator('#SeerrUrl').getAttribute('aria-describedby')).toBeNull();
});

test('a reveal button names the secret it reveals and reports whether it is showing', async ({ page }) => {
    await open(page);
    await page.locator('#cgProvTmdb > summary').click();
    const btn = page.locator('#RevealTmdbApiKey');
    await expect(btn).toHaveAccessibleName('Show TMDB API key (optional)');
    await expect(btn).toHaveAttribute('aria-pressed', 'false');
    await expect(btn).toHaveAttribute('aria-controls', 'TmdbApiKey');

    await btn.click();
    await expect(btn).toHaveAttribute('aria-pressed', 'true');
    await expect(page.locator('#TmdbApiKey')).toHaveClass(/cgSecretShown/);
    await expect(btn).toHaveAccessibleName('Show TMDB API key (optional)');
});

test('the back-to-top button appears once scrolled down and returns to the top', async ({ page }) => {
    await open(page);
    const btn = page.locator('#cgSettingsScrollTop');
    await expect(btn).toBeHidden();

    // Opening every section makes the form long enough to scroll however tall the viewport is.
    await page.evaluate(() => {
        document.querySelectorAll('.cgSection.cgCollapsed > h2 > .cgSectionToggle').forEach((b) => b.click());
        document.querySelectorAll('details').forEach((d) => { d.open = true; });
    });
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
