// In the expanded detail the buttons tile in one row, and the nested Resolve popover is a <details> among
// them. The <details> carries the tile margin, so its summary must take none of its own: a second margin puts
// the summary lower than the buttons beside it. This pins the alignment with the real dashboard CSS.
const { test, expect } = require('@playwright/test');
const { openReport } = require('./support/report-page');
const { baseSummary, movieItem } = require('./support/fixtures');

test('the Resolve button lines up with the buttons beside it', async ({ page }) => {
    const summary = baseSummary({ DomainPatternCounts: { Movies: { Recommendation: 1 }, Shows: {}, Music: {}, Books: {} }, TotalGaps: 1, MintableKinds: { Movie: 'Tmdb' } });
    await page.setViewportSize({ width: 1300, height: 700 });
    await openReport(page, summary, { Movies: [movieItem()] });
    await page.locator('.cgRow').first().locator('.cgTitle').evaluate((el) => el.click());

    const geometry = await page.evaluate(() => {
        const top = (el) => el.getBoundingClientRect().top;
        const bottom = (el) => el.getBoundingClientRect().bottom;
        const summary = document.querySelector('.cgTitleDetail .cgPopNested > summary');
        const details = summary.parentElement;
        const link = document.querySelector('.cgTitleDetail .cgLink');
        return { summaryTop: top(summary), detailsTop: top(details), linkTop: top(link), summaryBottom: bottom(summary), detailsBottom: bottom(details) };
    });

    expect(Math.abs(geometry.summaryTop - geometry.linkTop)).toBeLessThan(0.5);
    expect(Math.abs(geometry.summaryTop - geometry.detailsTop)).toBeLessThan(0.5);
    expect(Math.abs(geometry.summaryBottom - geometry.detailsBottom)).toBeLessThan(0.5);
});
