// Drives the real dashboard report.js/report.css against a mocked ApiClient/Dashboard (see
// e2e/support/build-harness.js) instead of a live Jellyfin server. Chromium only: it is what
// Jellyfin's own web client targets and what surfaced the CSS containment bug this suite exists to
// catch (see CLAUDE.md's "position: fixed is not safe" note); add other engines if a bug ever shows
// up that is specific to one.
const { defineConfig, devices } = require('@playwright/test');

module.exports = defineConfig({
    testDir: './e2e',
    fullyParallel: true,
    forbidOnly: !!process.env.CI,
    retries: process.env.CI ? 1 : 0,
    reporter: process.env.CI ? [['github'], ['html', { open: 'never' }]] : 'list',
    use: {
        trace: 'retain-on-failure'
    },
    projects: [
        { name: 'chromium', use: { ...devices['Desktop Chrome'] } }
    ]
});
