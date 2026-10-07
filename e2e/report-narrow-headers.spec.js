// A collapsed set's header holds the name, its count and coverage, and a row of buttons. On a phone the
// buttons alone fill the width, so below 40em the name takes the first line and the rest wraps beneath it.
const { test, expect } = require('@playwright/test');
const { openReport } = require('./support/report-page');
const { baseSummary, movieItem } = require('./support/fixtures');

function collectionItem() {
    return movieItem({
        Id: 'collection:10:105',
        Name: 'Back to the Future Part II',
        PatternName: 'SetCompletion',
        SourceItemType: 'Collection',
        SourceItemId: '10',
        SourceItemName: 'The Back to the Future Collection',
        SourceLinks: [{ Name: 'TMDB', Url: 'https://www.themoviedb.org/collection/264' }],
        SetOwnedCount: 1,
        SetTotalCount: 3
    });
}

async function openCollapsed(page, width) {
    const summary = baseSummary({ DomainPatternCounts: { Movies: { SetCompletion: 1 }, Shows: {}, Music: {}, Books: {} }, TotalGaps: 1 });
    await page.setViewportSize({ width, height: 800 });
    await openReport(page, summary, { Movies: [collectionItem()] });
    const hdr = page.locator('.cgGridWrap > .cgGroup > .cgHdr').first();
    await hdr.evaluate((el) => el.click());
    await expect(hdr.locator('..')).toHaveClass(/cgCollapsed/);
    return hdr.evaluate((el) => {
        const box = (sel) => el.querySelector(sel).getBoundingClientRect();
        const label = el.querySelector('.cgLabel');
        const buttons = el.querySelectorAll(':scope > a, :scope > button, :scope > details');
        const last = buttons[buttons.length - 1].getBoundingClientRect();
        return {
            labelTop: box('.cgLabel').top,
            labelBottom: box('.cgLabel').bottom,
            labelTruncated: label.scrollWidth > label.clientWidth,
            labelWidth: box('.cgLabel').width,
            countTop: box('.cgCount').top,
            countLeft: box('.cgCount').left,
            lastButtonTop: last.top,
            lastButtonRight: last.right,
            hdrRight: el.getBoundingClientRect().right
        };
    });
}

test('on a phone the set name gets its own line and the buttons wrap below it', async ({ page }) => {
    const g = await openCollapsed(page, 390);

    expect(g.labelTruncated).toBe(false);
    expect(g.countTop).toBeGreaterThanOrEqual(g.labelBottom - 0.5);
    expect(g.lastButtonRight).toBeLessThanOrEqual(g.hdrRight + 0.5);
});

test('on a wide screen the header stays one line', async ({ page }) => {
    const g = await openCollapsed(page, 1300);

    expect(Math.abs(g.countTop - g.labelTop)).toBeLessThan(8);
    expect(g.lastButtonTop).toBeLessThan(g.labelBottom);
});
