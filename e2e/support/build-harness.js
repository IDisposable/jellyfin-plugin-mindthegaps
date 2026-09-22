// Builds a standalone HTML page for the report dashboard, the same way the .csproj's
// BuildDashboard task builds the real one (splice the body into the shell, wrap common + page js in one
// scope), but inlining the stylesheet and the script bundle in place of the tags that reference them by
// URL, since the served files are not reachable from a file:// page, and with
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

// The report page's script, split by concern (see the Dashboard pages section of CLAUDE.md); mirrors
// the $(MtgReportJs) list in the .csproj's BuildDashboard invocation, in the same order, so the harness
// concatenates exactly what the real build embeds.
const REPORT_JS_FILES = [
    'mindthegaps.report.markup.js',
    'mindthegaps.report.diagnose.js',
    'mindthegaps.report.tree.js',
    'mindthegaps.report.filters.js',
    'mindthegaps.report.export.js',
    'mindthegaps.report.actions.js',
    'mindthegaps.report.render.js',
    'mindthegaps.report.explore.js',
    'mindthegaps.report.todo.js',
    'mindthegaps.report.wiring.js'
];

// A mock ApiClient/Dashboard sufficient for the report page's own pageshow setup to run to
// completion (summary, gaps, resolutions, plugin config, acquisition config, public system info):
// enough surface for load()/ensureSlice() to resolve and render real rows through the real code
// path, not so much that this drifts into re-implementing the server.
function buildMockScript(summary, itemsByDomain, todo, demand) {
    return `
<script>
window.__uiTestErrors = [];
window.addEventListener('error', function (e) { window.__uiTestErrors.push(String(e.message)); });

var __SUMMARY__ = ${JSON.stringify(summary)};
var __ITEMS_BY_DOMAIN__ = ${JSON.stringify(itemsByDomain)};
var __TODO__ = ${JSON.stringify(todo || null)};
var __DEMAND__ = ${JSON.stringify(demand || null)};
window.__todoCalls = [];
window.__detailCalls = [];
window.__gapsCalls = [];
window.__markFetchedCalls = [];
window.__availabilityCalls = [];

function leanReport(items) {
    var sets = [];
    var setIndex = {};
    var kinds = [];
    var shared = function (field) { return items.length > 0 && items.every(function (i) { return i[field] === items[0][field]; }); };
    var samePattern = shared('PatternName');
    var sameDomain = shared('DomainName');
    var rows = items.map(function (it) {
        var row = Object.assign({}, it);
        var k = kinds.indexOf(it.TargetKindName);
        if (k === -1) { k = kinds.push(it.TargetKindName) - 1; }
        row.TargetKindRef = k;
        delete row.TargetKindName;
        if (samePattern) { delete row.PatternName; }
        if (sameDomain) { delete row.DomainName; }
        delete row.Overview;
        delete row.Links;
        delete row.SourceLinks;
        row.HasOverview = !!it.Overview;
        if (it.SourceLinks && it.SourceLinks.length) {
            var key = JSON.stringify(it.SourceLinks);
            if (setIndex[key] === undefined) { setIndex[key] = sets.length; sets.push(it.SourceLinks); }
            row.SourceLinksRef = setIndex[key];
        }
        if (it.Availability && it.Availability.length) {
            row.Availability = it.Availability.map(function (o) { return { Provider: o.Provider, MonetizationType: o.MonetizationType, LogoUrl: o.LogoUrl }; });
            var watch = it.Availability.map(function (o) { return o.Url; }).find(function (u) { return (u || '').indexOf('https://www.themoviedb.org/') === 0; });
            if (watch) { row.WatchUrl = watch; }
        } else {
            delete row.Availability;
        }
        return row;
    });
    return {
        Items: rows,
        SourceLinkSets: sets,
        TargetKinds: kinds,
        PatternName: samePattern ? items[0].PatternName : undefined,
        DomainName: sameDomain ? items[0].DomainName : undefined,
        SourceRuns: []
    };
}

function todoCall(url) {
    window.__todoCalls.push(url);
    var userId = decodeURIComponent((url.match(/userId=([^&]+)/) || [])[1] || '');
    var id = decodeURIComponent((url.match(/[?&]id=([^&]+)/) || [])[1] || '');
    if (url.indexOf('Todo/All') !== -1) { return __TODO__ ? Promise.resolve(JSON.parse(JSON.stringify(__TODO__))) : Promise.reject({ status: 403 }); }
    var mine = function (it) { return it.OwnerId === userId; };
    if (url.indexOf('Todo/Remove') !== -1) {
        __TODO__.Items = __TODO__.Items.filter(function (it) { return !(mine(it) && it.Id === id); });
        __TODO__.Owners.forEach(function (o) { o.Count = __TODO__.Items.filter(function (it) { return it.OwnerId === o.UserId; }).length; });
        return Promise.resolve(1);
    }
    if (url.indexOf('Todo/SetDone') !== -1) {
        __TODO__.Items.forEach(function (it) { if (mine(it) && it.Id === id) { it.Done = /done=true/.test(url); } });
        return Promise.resolve({});
    }
    if (url.indexOf('Todo/VerifyAll') !== -1) {
        var count = __TODO__.Items.filter(mine).length;
        return Promise.resolve({ Checked: count, Owned: 0, Items: [] });
    }
    return Promise.resolve({ Owned: false, Entry: null });
}

// Marking a row fetched flips the matching entries in __DEMAND__ itself (a real MarkFetched moves a
// title's OpenCount to 0), so a spec can reload/re-render and see the row actually close out, the same
// way todoCall mutates __TODO__ in place for Remove/SetDone.
function markFetchedCall(url, body) {
    var refs = JSON.parse(body || '[]');
    window.__markFetchedCalls.push(refs);
    var updated = 0;
    (__DEMAND__ ? __DEMAND__.Items : []).forEach(function (row) {
        var hit = refs.some(function (r) { return (row.Entries || []).some(function (e) { return e.OwnerId === r.OwnerId && e.GapId === r.GapId; }); });
        if (hit) { row.OpenCount = 0; updated += refs.length; }
    });
    return Promise.resolve({ Requested: refs.length, Updated: updated });
}

window.ApiClient = {
    ajax: function (opts) {
        var url = opts.url || '';
        if (url.indexOf('MindTheGaps/Summary') !== -1) { return Promise.resolve(__SUMMARY__); }
        if (url.indexOf('MindTheGaps/GapDetail') !== -1) {
            window.__detailCalls.push(url);
            var wantedId = decodeURIComponent((url.match(/id=([^&]+)/) || [])[1] || '');
            var found = null;
            Object.keys(__ITEMS_BY_DOMAIN__).forEach(function (d) {
                (__ITEMS_BY_DOMAIN__[d] || []).forEach(function (it) { if (it.Id === wantedId) { found = it; } });
            });
            return found ? Promise.resolve(found) : Promise.reject({ status: 404 });
        }
        if (url.indexOf('MindTheGaps/Gaps') !== -1) {
            var domain = decodeURIComponent((url.match(/domain=([^&]+)/) || [])[1] || '');
            var items = __ITEMS_BY_DOMAIN__[domain] || [];
            var fullRequested = /[?&]full=true/.test(url);
            window.__gapsCalls.push(url);
            // The real server returns list rows (Model/GapRow.cs): no overview, no external links, no
            // offer deeplinks, and a set's source links once in SourceLinkSets. Serving full fixtures
            // instead would let the page read a field the list does not carry without a test noticing.
            var body = fullRequested
                ? { Items: items, SourceRuns: [] }
                : leanReport(items);
            return Promise.resolve(Object.assign(body, { GeneratedUtc: '2026-01-01T00:00:00Z', GeneratedVersion: __SUMMARY__.GeneratedVersion }));
        }
        if (url.indexOf('MindTheGaps/Todo/Demand/MarkFetched') !== -1) { return markFetchedCall(url, opts.data); }
        if (url.indexOf('MindTheGaps/Todo/Demand') !== -1) {
            return __DEMAND__ ? Promise.resolve(JSON.parse(JSON.stringify(__DEMAND__))) : Promise.reject({ status: 403 });
        }
        if (url.indexOf('MindTheGaps/Todo') !== -1) { return todoCall(url); }
        if (url.indexOf('MindTheGaps/Availability') !== -1) {
            window.__availabilityCalls.push(url);
            return Promise.resolve([{ Provider: 'Netflix', MonetizationType: 'flatrate', LogoUrl: null, Url: 'https://www.themoviedb.org/movie/1/watch' }]);
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
function buildHarness(summary, itemsByDomain, todo, demand) {
    const css = fs.readFileSync(path.join(WEB_DIR, 'mindthegaps.css'), 'utf8');
    const common = fs.readFileSync(path.join(WEB_DIR, 'mindthegaps.common.js'), 'utf8');
    const reportJs = REPORT_JS_FILES.map((f) => fs.readFileSync(path.join(WEB_DIR, f), 'utf8')).join('\n');
    const shell = fs.readFileSync(path.join(WEB_DIR, 'mindthegaps.report.html'), 'utf8');
    const body = fs.readFileSync(path.join(WEB_DIR, 'mindthegaps.report.body.html'), 'utf8');

    const bundle = '(function () {\n' + common + '\n' + reportJs + '\n})();\n';
    let page = shell
        .replace('@@MTG_BODY@@', () => body)
        .replace(/<link rel="stylesheet" href="[^"]*mindthegaps\.css[^"]*"\s*\/?>/, () => '<style>' + css + '</style>')
        .replace(/<script type="text\/javascript" src="[^"]*report\.js[^"]*"><\/script>/, () => '<script type="text/javascript">' + bundle + '</script>');
    if (page.includes('@@MTG_') || !page.includes('function wrap(tag, attrs, innerHtml)')) {
        throw new Error('the report shell does not match what the harness expects: update build-harness.js alongside mindthegaps.report.html');
    }

    // Wrap in jellyfin-web's real page wrapper, containment declaration and all: see the module
    // comment above for why this specific div is the point of the harness.
    page = page.replace(
        '<div id="MindTheGapsPage"',
        '<div class="page type-interior mainAnimatedPage" style="contain:size style;position:relative;width:100%;height:100vh;overflow:auto;">\n<div id="MindTheGapsPage"'
    );
    page = page.replace('</html>', '</div>\n</html>');
    page = page.replace('<script type="text/javascript">', buildMockScript(summary, itemsByDomain, todo, demand) + '<script type="text/javascript">');

    const outDir = fs.mkdtempSync(path.join(os.tmpdir(), 'mtg-ui-test-'));
    const outPath = path.join(outDir, 'harness.html');
    fs.writeFileSync(outPath, page);
    return outPath;
}

module.exports = { buildHarness };
