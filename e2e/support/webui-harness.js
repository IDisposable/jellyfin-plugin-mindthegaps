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

// detailResult: the fake MindTheGaps/WebUi/Detail payload (undefined defaults to a fixed detail record;
// null simulates TMDB having nothing for that id; use { reject: true } to simulate the call failing).
// profilesResult: the fake MindTheGaps/WebUi/Profiles payload (undefined defaults to two profiles; use
// { reject: true } to simulate the call failing, which the dialog treats as "no picker, use the default").
function buildMockScript(item, missingResult, sendResult, discoverResult, todoResult, detailResult, profilesResult) {
    var defaultDetail = {
        Title: 'A Missing Movie', Kind: 'Movie', TmdbId: 603, Year: 1999,
        Tagline: 'Welcome to the Real World.', Overview: 'A test overview.',
        Genres: ['Action', 'Sci-Fi'], RuntimeMinutes: 136, VoteAverage: 8.2, Status: 'Released',
        NumberOfSeasons: null, Networks: [],
        PosterUrl: 'https://example.com/poster.jpg', BackdropUrl: 'https://example.com/backdrop.jpg',
        TmdbUrl: 'https://www.themoviedb.org/movie/603', ImdbUrl: 'https://www.imdb.com/title/tt0133093/',
        JustWatchUrl: 'https://www.justwatch.com/us/search?q=A%20Missing%20Movie',
        YoutubeTrailerKey: 'vKQi3bBA1y8'
    };
    var defaultProfiles = { Profiles: [{ Id: 1, Name: 'HD-1080p' }, { Id: 2, Name: 'Ultra-HD' }], DefaultId: 1 };

    return `
<script>
window.__uiTestErrors = [];
window.addEventListener('error', function (e) { window.__uiTestErrors.push(String(e.message)); });

var __ITEM__ = ${JSON.stringify(item)};
var __MISSING_RESULT__ = ${JSON.stringify(missingResult)};
var __DISCOVER_RESULT__ = ${JSON.stringify(discoverResult)};
var __SEND_RESULT__ = ${JSON.stringify(sendResult || { Success: true, Message: 'Sent 1 item(s).' })};
var __TODO_RESULT__ = ${JSON.stringify(todoResult === undefined ? 1 : todoResult)};
var __DETAIL_RESULT__ = ${JSON.stringify(detailResult === undefined ? defaultDetail : detailResult)};
var __PROFILES_RESULT__ = ${JSON.stringify(profilesResult === undefined ? defaultProfiles : profilesResult)};
window.__lastSendUrl = null;
window.__lastTodoUrl = null;
window.__lastDetailUrl = null;
window.__lastProfilesUrl = null;

window.ApiClient = {
    getCurrentUserId: function () { return 'user-1'; },
    getItem: function (userId, itemId) {
        return __ITEM__ ? Promise.resolve(__ITEM__) : Promise.reject(new Error('no such item'));
    },
    ajax: function (opts) {
        var url = opts.url || '';
        if (url.indexOf('/WebUi/Detail') !== -1) {
            window.__lastDetailUrl = url;
            return __DETAIL_RESULT__ && __DETAIL_RESULT__.reject ? Promise.reject(new Error('tmdb down')) : Promise.resolve(__DETAIL_RESULT__);
        }
        if (url.indexOf('/WebUi/Profiles') !== -1) {
            window.__lastProfilesUrl = url;
            return __PROFILES_RESULT__ && __PROFILES_RESULT__.reject ? Promise.reject(new Error('arr down')) : Promise.resolve(__PROFILES_RESULT__);
        }
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
// detailResult/profilesResult: the dialog's own lookups, see buildMockScript's header for the defaults.
function buildWebUiHarness(item, missingResult, sendResult, todoResult, detailResult, profilesResult) {
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
${buildMockScript(item, missingResult, sendResult, null, todoResult, detailResult, profilesResult)}
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
function buildWebUiHomeHarness(discoverResult, sendResult, todoResult, detailResult, profilesResult) {
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
${buildMockScript(null, null, sendResult, discoverResult, todoResult, detailResult, profilesResult)}
<script>${webui}</script>
</body>
</html>`;

    const outDir = fs.mkdtempSync(path.join(os.tmpdir(), 'mtg-webui-test-'));
    const outPath = path.join(outDir, 'harness.html');
    fs.writeFileSync(outPath, page);
    return outPath;
}

module.exports = { buildWebUiHarness, buildWebUiHomeHarness };
