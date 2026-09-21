// The report list loads rows (Model/GapRow.cs), not whole gaps: the overview, external links and each
// offer's deeplink stay on the server until a row is opened (MindTheGaps/GapDetail), and the Markdown
// export asks for the tab's full gaps in one request. The harness serves lean rows exactly as the real
// endpoint does, so these specs fail if the page reads a field the list does not carry, or fetches
// detail eagerly instead of on demand.
const { test, expect } = require('@playwright/test');
const { openReport } = require('./support/report-page');
const { baseSummary, movieItem } = require('./support/fixtures');

// Bytes that are not an image, so loading fails at once and without a network. A data: URL is never deferred
// by loading="lazy", so the failure does not wait on when the browser decides the image is near the viewport.
const BROKEN_IMAGE = 'data:image/png;base64,bm90IGFuIGltYWdl';

async function setup(page, overrides) {
    const summary = baseSummary({ DomainPatternCounts: { Movies: { Recommendation: 1 }, Shows: {}, Music: {}, Books: {} }, TotalGaps: 1 });
    await openReport(page, summary, { Movies: [movieItem(overrides)] });
}

const detailCalls = (page) => page.evaluate(() => window.__detailCalls.length);

test('loading a tab fetches no per-row detail', async ({ page }) => {
    await setup(page);
    await expect(page.locator('.cgRow').first()).toBeVisible();
    expect(await detailCalls(page)).toBe(0);
});

test('opening the Information popover fetches the detail once and shows the links', async ({ page }) => {
    await setup(page);
    const info = page.locator('.cgRow').first().locator('.cgPop[data-pop="info"]');
    await info.locator('> summary').evaluate((el) => el.click());
    await expect(info.locator('> .cgPopBody')).toContainText('IMDb');
    expect(await detailCalls(page)).toBe(1);

    // Opening the same row's expanded detail reuses what was fetched.
    await page.locator('.cgRow').first().locator('.cgTitle').evaluate((el) => el.click());
    await expect(page.locator('.cgRow').first().locator('.cgTitleDetail')).toContainText('IMDb');
    expect(await detailCalls(page)).toBe(1);
});

test('the Watch popover of a row with no offers needs no detail', async ({ page }) => {
    await setup(page);
    const watch = page.locator('.cgRow').first().locator('.cgPop[data-pop="watch"]');
    await watch.locator('> summary').evaluate((el) => el.click());
    await expect(watch.locator('> .cgPopBody')).toContainText(/JustWatch|Look up where to watch/);
    expect(await detailCalls(page)).toBe(0);
});

test('the Watch popover of a row with offers fetches the deeplinks', async ({ page }) => {
    await setup(page, {
        Availability: [{ Provider: 'Netflix', MonetizationType: 'flatrate', Url: 'https://www.netflix.com/title/1' }],
        AvailabilityChecked: true
    });
    const watch = page.locator('.cgRow').first().locator('.cgPop[data-pop="watch"]');
    await watch.locator('> summary').evaluate((el) => el.click());
    await expect(watch.locator('> .cgPopBody a[href="https://www.netflix.com/title/1"]')).toHaveCount(1);
    expect(await detailCalls(page)).toBe(1);
});

test('the export asks for full gaps once and writes the links the list does not carry', async ({ page }) => {
    await setup(page);
    const download = page.waitForEvent('download');
    await page.locator('#cgExport').click();
    const file = await download;
    const stream = await file.createReadStream();
    let text = '';
    for await (const chunk of stream) { text += chunk; }

    expect(text).toContain('imdb.com/title/tt0054698');
    const fullCalls = await page.evaluate(() => window.__gapsCalls.filter((u) => /full=true/.test(u)).length);
    expect(fullCalls).toBe(1);
});

test.describe('service icons on the collapsed line', () => {
    const offer = (Provider, MonetizationType, LogoUrl) => ({ Provider, MonetizationType, LogoUrl, Url: 'https://example.test/watch' });

    test('show at most two services, streamable ones first, with the rest counted', async ({ page }) => {
        await setup(page, {
            Availability: [
                offer('Apple TV', 'buy'),
                offer('Netflix', 'flatrate', BROKEN_IMAGE),
                offer('Netflix', 'rent'),
                offer('Tubi', 'free'),
                offer('Hulu', 'flatrate')
            ],
            AvailabilityChecked: true
        });
        const svcs = page.locator('.cgRow').first().locator('.cgSvcs');
        await expect(svcs.locator('.cgSvc')).toHaveCount(2);
        await expect(svcs.locator('.cgSvcMore')).toHaveText('+2');
        // The two subscription services lead; Tubi (free) and the buy/rent-only Apple TV are the remainder.
        await expect(svcs.locator('.cgSvcMore')).toHaveAttribute('title', 'Tubi, Apple TV');
    });

    test('a logo that fails to load falls back to the service initial', async ({ page }) => {
        await setup(page, {
            Availability: [offer('Netflix', 'flatrate', BROKEN_IMAGE)],
            AvailabilityChecked: true
        });
        await expect(page.locator('.cgRow').first().locator('.cgSvc.cgSvcText')).toHaveText('N');
    });

    test('a disabled provider is dropped before the two are chosen, so it never takes a slot', async ({ page }) => {
        await setup(page, {
            Availability: [offer('Netflix', 'flatrate'), offer('Hulu', 'flatrate'), offer('Tubi', 'free')],
            AvailabilityChecked: true
        });
        const icons = page.locator('.cgRow').first().locator('.cgSvcs .cgSvc');
        await expect(icons.nth(0)).toHaveAttribute('title', 'Netflix');
        await expect(icons.nth(1)).toHaveAttribute('title', 'Hulu');
        await expect(page.locator('.cgRow').first().locator('.cgSvcMore')).toHaveText('+1');

        await page.locator('.cgProv[data-prov="Netflix"]').evaluate((el) => { el.checked = false; el.dispatchEvent(new Event('change', { bubbles: true })); });

        // Hulu and Tubi fill the two slots; nothing is left over to count.
        await expect(icons.nth(0)).toHaveAttribute('title', 'Hulu');
        await expect(icons.nth(1)).toHaveAttribute('title', 'Tubi');
        await expect(page.locator('.cgRow').first().locator('.cgSvcMore')).toHaveCount(0);
    });

    test('the icons follow the monetization filter', async ({ page }) => {
        await setup(page, { Availability: [offer('Apple TV', 'buy')], AvailabilityChecked: true });
        const svcs = page.locator('.cgRow').first().locator('.cgSvcs .cgSvc');
        await expect(svcs).toHaveCount(1);
        await page.locator('.cgMon[data-mon="buy"]').evaluate((el) => { el.checked = false; el.dispatchEvent(new Event('change', { bubbles: true })); });
        await expect(page.locator('.cgRow').first().locator('.cgSvcs')).toHaveCount(0);
    });
});

// TMDB lists a service's tiers and its resellers' channels as separate providers. They are one service to the
// person reading the list, so a row shows it once and the filter has a single entry for it.
test.describe('one service, however TMDB splits it', () => {
    const offer = (Provider, LogoUrl) => ({ Provider, MonetizationType: 'flatrate', LogoUrl, Url: 'https://example.test/watch' });
    const NETFLIX_LOGO = 'data:image/png;base64,bm90IGFuIGltYWdl';

    test('a service and its ad tier share one icon, wearing the base service logo', async ({ page }) => {
        await setup(page, {
            Availability: [offer('Netflix Standard with Ads', 'https://example.test/ads.png'), offer('Netflix', NETFLIX_LOGO), offer('Hulu')],
            AvailabilityChecked: true
        });
        const svcs = page.locator('.cgRow').first().locator('.cgSvcs');
        await expect(svcs.locator('.cgSvc')).toHaveCount(2);
        await expect(svcs.locator('.cgSvcMore')).toHaveCount(0);
        // Netflix is first and keeps its own name and logo, not the ad tier's.
        await expect(svcs.locator('.cgSvc').first()).toHaveAttribute('title', 'Netflix');
        await expect(svcs.locator('.cgSvc').nth(1)).toHaveAttribute('title', 'Hulu');
    });

    test('a reseller channel folds into its service', async ({ page }) => {
        await setup(page, { Availability: [offer('HBO Max Amazon Channel'), offer('HBO Max')], AvailabilityChecked: true });
        await expect(page.locator('.cgRow').first().locator('.cgSvcs .cgSvc')).toHaveCount(1);
        await expect(page.locator('.cgRow').first().locator('.cgSvcs .cgSvc')).toHaveAttribute('title', 'HBO Max');
    });

    test('a name that only looks related stays its own service', async ({ page }) => {
        await setup(page, { Availability: [offer('Netflix'), offer('Netflix Kids')], AvailabilityChecked: true });
        await expect(page.locator('.cgRow').first().locator('.cgSvcs .cgSvc')).toHaveCount(2);
    });

    test('the filter lists one entry per service, and one checkbox switches off every variant', async ({ page }) => {
        await setup(page, { Availability: [offer('Netflix'), offer('Netflix Standard with Ads')], AvailabilityChecked: true });
        await expect(page.locator('.cgProv')).toHaveCount(1);
        await expect(page.locator('.cgProv')).toHaveAttribute('data-prov', 'Netflix');
        await expect(page.locator('.cgRow').first().locator('.cgSvcs .cgSvc')).toHaveCount(1);

        await page.locator('.cgProv[data-prov="Netflix"]').evaluate((el) => { el.checked = false; el.dispatchEvent(new Event('change', { bubbles: true })); });

        await expect(page.locator('.cgRow').first().locator('.cgSvcs')).toHaveCount(0);
    });

    test('a filter list saved with the provider names as TMDB gave them is folded on load', async ({ page }) => {
        await page.addInitScript(() => {
            localStorage.setItem('mindthegaps.filters', JSON.stringify({
                knownProviders: ['Netflix', 'Netflix Standard with Ads', 'HBO Max', 'HBO Max Amazon Channel'],
                disabledProviders: {}
            }));
        });
        await setup(page, {});
        await expect(page.locator('.cgProv')).toHaveCount(2);
        await expect(page.locator('.cgProv[data-prov="Netflix"]')).toHaveCount(1);
        await expect(page.locator('.cgProv[data-prov="HBO Max"]')).toHaveCount(1);
    });
});

test.describe('the service icons link to the title on TMDB', () => {
    const WATCH = 'https://www.themoviedb.org/movie/949-heat/watch?locale=US';
    const offer = (Provider, MonetizationType) => ({ Provider, MonetizationType, Url: WATCH });

    test('the icons are one link to the TMDB watch page, in a new tab', async ({ page }) => {
        await setup(page, { Availability: [offer('Netflix', 'flatrate'), offer('Hulu', 'flatrate'), offer('Tubi', 'free')], AvailabilityChecked: true });

        const link = page.locator('.cgRow').first().locator('a.cgSvcs');
        await expect(link).toHaveCount(1);
        await expect(link).toHaveAttribute('href', WATCH);
        await expect(link).toHaveAttribute('target', '_blank');
        await expect(link).toHaveAttribute('rel', /noopener/);
        await expect(link).toHaveAttribute('title', 'Where to watch on TMDB: Netflix, Hulu, Tubi');
        await expect(link.locator('.cgSvc')).toHaveCount(2);
        await expect(link.locator('.cgSvcMore')).toHaveText('+1');
    });

    test('a row whose offers do not link to TMDB shows plain icons, not a link', async ({ page }) => {
        await setup(page, {
            Availability: [{ Provider: 'Netflix', MonetizationType: 'flatrate', Url: 'https://example.test/watch' }],
            AvailabilityChecked: true
        });

        const row = page.locator('.cgRow').first();
        await expect(row.locator('.cgSvcs')).toHaveCount(1);
        await expect(row.locator('a.cgSvcs')).toHaveCount(0);
    });

    test('clicking the icons follows the link and does not pin the row open', async ({ page }) => {
        await setup(page, { Availability: [offer('Netflix', 'flatrate')], AvailabilityChecked: true });
        await page.locator('a.cgSvcs').evaluate((a) => a.addEventListener('click', (e) => e.preventDefault()));

        await page.locator('a.cgSvcs').click();

        await expect(page.locator('.cgTitleDetail.cgPinned')).toHaveCount(0);
    });
});

// The rest of the page carries a hand on every list item, which would put one on the empty parts of a row.
// Only what acts on a click keeps it.
test.describe('the pointer on a row', () => {
    test('is a hand on the title, the service icons and the icon buttons, and not on the rest of the row', async ({ page }) => {
        await setup(page, {
            Availability: [{ Provider: 'Netflix', MonetizationType: 'flatrate', Url: 'https://www.themoviedb.org/movie/949/watch' }],
            AvailabilityChecked: true
        });
        await page.addStyleTag({ content: '.listItem { cursor: pointer; }' });

        const row = page.locator('.cgRow').first();
        const cursor = (locator) => locator.evaluate((el) => getComputedStyle(el).cursor);
        expect(await cursor(row)).toBe('default');
        expect(await cursor(row.locator('.cgMeta'))).toBe('default');
        expect(await cursor(row.locator('.cgTitle'))).toBe('pointer');
        expect(await cursor(row.locator('a.cgSvcs'))).toBe('pointer');
        expect(await cursor(row.locator('.cgIcons summary').first())).toBe('pointer');
    });
});
