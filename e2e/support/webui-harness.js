// Builds a minimal fake jellyfin-web item-detail page and loads the real mindthegaps.webui.js against
// it, the same way build-harness.js loads the real report.js against a fake copy of this plugin's own
// dashboard shell. Unlike the dashboard harness, webui.js is injected into jellyfin-web's own native
// pages (Person/Movie/Series/Home), not a plugin config page, so this fakes just enough of that page's
// real structure (the .page wrapper with the same CSS containment, #similarCollapsible as the anchor
// the script inserts before) for the script's own DOM queries and insertion points to behave exactly as
// they would live, without a real Jellyfin server.
const fs = require('fs');
const os = require('os');
const path = require('path');

const WEB_DIR = path.join(__dirname, '..', '..', 'Jellyfin.Plugin.MindTheGaps', 'Web');

function buildMockScript(item, missingResult, sendResult, discoverResult, todoResult) {
    return `
<script>
window.__uiTestErrors = [];
window.addEventListener('error', function (e) { window.__uiTestErrors.push(String(e.message)); });

var __ITEM__ = ${JSON.stringify(item)};
var __MISSING_RESULT__ = ${JSON.stringify(missingResult)};
var __DISCOVER_RESULT__ = ${JSON.stringify(discoverResult)};
var __SEND_RESULT__ = ${JSON.stringify(sendResult || { Success: true, Message: 'Sent 1 item(s).' })};
var __TODO_RESULT__ = ${JSON.stringify(todoResult === undefined ? 1 : todoResult)};
window.__lastSendUrl = null;
window.__lastTodoUrl = null;

window.ApiClient = {
    getCurrentUserId: function () { return 'user-1'; },
    getItem: function (userId, itemId) {
        return __ITEM__ ? Promise.resolve(__ITEM__) : Promise.reject(new Error('no such item'));
    },
    ajax: function (opts) {
        var url = opts.url || '';
        if (url.indexOf('/Missing') !== -1 || url.indexOf('/Related') !== -1) {
            return __MISSING_RESULT__ ? Promise.resolve(__MISSING_RESULT__) : Promise.reject(new Error('404'));
        }
        if (url.indexOf('/Discover') !== -1) {
            return __DISCOVER_RESULT__ ? Promise.resolve(__DISCOVER_RESULT__) : Promise.reject(new Error('404'));
        }
        if (url.indexOf('/Todo') !== -1) {
            window.__lastTodoUrl = url;
            return Promise.resolve(__TODO_RESULT__);
        }
        if (url.indexOf('/Send') !== -1) {
            window.__lastSendUrl = url;
            return Promise.resolve(__SEND_RESULT__);
        }
        return Promise.reject(new Error('unhandled url: ' + url));
    },
    getUrl: function (p, query) {
        var q = query ? ('?' + Object.keys(query).map(function (k) { return k + '=' + encodeURIComponent(query[k]); }).join('&')) : '';
        return p + q;
    },
    serverId: function () { return 'test-server'; }
};
window.Dashboard = {
    alert: function (m) { console.log('Dashboard.alert: ' + m); }
};
</script>
`;
}

// item: the fake ApiClient.getItem() result (null to simulate an id the item lookup fails for).
// missingResult: the fake MindTheGaps/Person/{id}/Missing (or Item/.../Related) payload (null for the
// surface being off). todoResult: the fake MindTheGaps/.../Todo payload (an int; defaults to 1).
function buildWebUiHarness(item, missingResult, sendResult, todoResult) {
    const webui = fs.readFileSync(path.join(WEB_DIR, 'mindthegaps.webui.js'), 'utf8');

    const page = `<!doctype html>
<html>
<head><meta charset="utf-8"></head>
<body>
<div class="page type-interior mainAnimatedPage itemDetailPage" id="itemDetailPage" style="contain:size style;position:relative;width:100%;height:100vh;overflow:auto;">
    <div class="detailPageContent">
        <div id="similarCollapsible"></div>
    </div>
</div>
${buildMockScript(item, missingResult, sendResult, null, todoResult)}
<script>${webui}</script>
</body>
</html>`;

    const outDir = fs.mkdtempSync(path.join(os.tmpdir(), 'mtg-webui-test-'));
    const outPath = path.join(outDir, 'harness.html');
    fs.writeFileSync(outPath, page);
    return outPath;
}

// discoverResult: the fake MindTheGaps/Home/Discover payload (null for the surface being off). The
// #homeTab .sections container starts empty, the way jellyfin-web's own home view does before its
// sections are laid out; a spec adds a child to it to simulate that happening, the same signal
// mindthegaps.webui.js's MutationObserver waits for before inserting its own row.
function buildWebUiHomeHarness(discoverResult, sendResult, todoResult) {
    const webui = fs.readFileSync(path.join(WEB_DIR, 'mindthegaps.webui.js'), 'utf8');

    const page = `<!doctype html>
<html>
<head><meta charset="utf-8"></head>
<body>
<div class="page type-interior mainAnimatedPage libraryPage homePage" id="indexPage" style="contain:size style;position:relative;width:100%;height:100vh;overflow:auto;">
    <div id="homeTab">
        <div class="sections"></div>
    </div>
</div>
${buildMockScript(null, null, sendResult, discoverResult, todoResult)}
<script>${webui}</script>
</body>
</html>`;

    const outDir = fs.mkdtempSync(path.join(os.tmpdir(), 'mtg-webui-test-'));
    const outPath = path.join(outDir, 'harness.html');
    fs.writeFileSync(outPath, page);
    return outPath;
}

module.exports = { buildWebUiHarness, buildWebUiHomeHarness };
