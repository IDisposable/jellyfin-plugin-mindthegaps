// Builds a standalone HTML page for the report dashboard, the same way the .csproj's
// ConcatDashboard task builds the real one (splice css/common/body/js into the shell), but with
// ApiClient/Dashboard replaced by an in-page mock so a spec can drive the real report.js/report.css
// against fake gap data without a Jellyfin server. This is what caught the CSS containment bug
// (jellyfin-web's div.page.mainAnimatedPage sets "contain: size style", which per spec makes it the
// containing block for a position:fixed descendant instead of the viewport): the page is wrapped in
// a div carrying that exact declaration, so a spec exercises the real containing-block behavior, not
// an approximation of it.
const fs = require('fs');
const os = require('os');
const path = require('path');

const WEB_DIR = path.join(__dirname, '..', '..', 'Jellyfin.Plugin.MindTheGaps', 'Web');

// A mock ApiClient/Dashboard sufficient for the report page's own pageshow setup to run to
// completion (summary, gaps, resolutions, plugin config, acquisition config, public system info):
// enough surface for load()/ensureSlice() to resolve and render real rows through the real code
// path, not so much that this drifts into re-implementing the server.
function buildMockScript(summary, itemsByDomain) {
    return `
<script>
window.__uiTestErrors = [];
window.addEventListener('error', function (e) { window.__uiTestErrors.push(String(e.message)); });

var __SUMMARY__ = ${JSON.stringify(summary)};
var __ITEMS_BY_DOMAIN__ = ${JSON.stringify(itemsByDomain)};

window.ApiClient = {
    ajax: function (opts) {
        var url = opts.url || '';
        if (url.indexOf('MindTheGaps/Summary') !== -1) { return Promise.resolve(__SUMMARY__); }
        if (url.indexOf('MindTheGaps/Gaps') !== -1) {
            var domain = decodeURIComponent((url.match(/domain=([^&]+)/) || [])[1] || '');
            var items = __ITEMS_BY_DOMAIN__[domain] || [];
            return Promise.resolve({ Items: items, GeneratedUtc: '2026-01-01T00:00:00Z', GeneratedVersion: __SUMMARY__.GeneratedVersion });
        }
        if (url.indexOf('MindTheGaps/Resolutions') !== -1) { return Promise.resolve({}); }
        if (url.indexOf('MindTheGaps/AcquisitionConfig') !== -1) { return Promise.resolve({}); }
        if (url.indexOf('Plugins') !== -1) { return Promise.resolve([]); }
        return Promise.resolve({});
    },
    getUrl: function (p, query) {
        var q = query ? ('?' + Object.keys(query).map(function (k) { return k + '=' + encodeURIComponent(query[k]); }).join('&')) : '';
        return p + q;
    },
    getPluginConfiguration: function () { return Promise.resolve({}); },
    getPublicSystemInfo: function () { return Promise.resolve({ ServerName: 'Test' }); },
    serverId: function () { return 'test-server'; }
};
window.Dashboard = {
    alert: function (m) { console.log('Dashboard.alert: ' + m); },
    showLoadingMsg: function () {},
    hideLoadingMsg: function () {},
    navigate: function (u) { console.log('Dashboard.navigate: ' + u); },
    processErrorResponse: function () {}
};
</script>
`;
}

// Writes the harness to a fresh temp file and returns its path (a file:// URL a spec can
// page.goto()). summary is the MindTheGaps/Summary shape; itemsByDomain maps a domain name (as it
// appears in summary.Domains) to the array MindTheGaps/Gaps returns for that domain.
function buildHarness(summary, itemsByDomain) {
    const css = fs.readFileSync(path.join(WEB_DIR, 'mindthegaps.css'), 'utf8');
    const common = fs.readFileSync(path.join(WEB_DIR, 'mindthegaps.common.js'), 'utf8');
    const reportJs = fs.readFileSync(path.join(WEB_DIR, 'mindthegaps.report.js'), 'utf8');
    const shell = fs.readFileSync(path.join(WEB_DIR, 'mindthegaps.report.html'), 'utf8');
    const body = fs.readFileSync(path.join(WEB_DIR, 'mindthegaps.report.body.html'), 'utf8');

    let page = shell
        .replace('@@MTG_CSS@@', css)
        .replace('@@MTG_BODY@@', body)
        .replace('@@MTG_COMMON@@', common)
        .replace('@@MTG_JS@@', reportJs);

    // Wrap in jellyfin-web's real page wrapper, containment declaration and all: see the module
    // comment above for why this specific div is the point of the harness.
    page = page.replace(
        '<div id="MindTheGapsPage"',
        '<div class="page type-interior mainAnimatedPage" style="contain:size style;position:relative;width:100%;height:100vh;overflow:auto;">\n<div id="MindTheGapsPage"'
    );
    page = page.replace('</html>', '</div>\n</html>');
    page = page.replace('<script type="text/javascript">', buildMockScript(summary, itemsByDomain) + '<script type="text/javascript">');

    const outDir = fs.mkdtempSync(path.join(os.tmpdir(), 'mtg-ui-test-'));
    const outPath = path.join(outDir, 'harness.html');
    fs.writeFileSync(outPath, page);
    return outPath;
}

module.exports = { buildHarness };
