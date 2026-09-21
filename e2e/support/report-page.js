// Shared setup every report spec needs: load the harness, fire the 'pageshow' event the way
// jellyfin-web fires it on the real dashboard page element (report.js's setup only runs once, on
// that event, guarded by page._cgBound), then expand every lazily-built group so its rows actually
// exist in the DOM (renderRow only builds a group's body when it is opened; see ensureGroupBody).
const { buildHarness } = require('./build-harness');

async function openReport(page, summary, itemsByDomain, todo) {
    const harnessPath = buildHarness(summary, itemsByDomain, todo);
    await page.goto('file://' + harnessPath);
    await page.evaluate(() => {
        document.querySelector('#MindTheGapsPage').dispatchEvent(new Event('pageshow'));
    });
    await page.waitForSelector('#cgList .cgGroup, #cgList .cgRow', { timeout: 5000 });
    await page.evaluate(() => {
        for (let i = 0; i < 5; i++) {
            const collapsed = document.querySelectorAll('#cgList .cgHdr[aria-expanded="false"]');
            if (!collapsed.length) { break; }
            collapsed.forEach((h) => h.click());
        }
    });
    await page.waitForSelector('#cgList .cgRow', { timeout: 5000 });
}

module.exports = { openReport };
