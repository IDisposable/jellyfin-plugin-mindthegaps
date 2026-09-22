// Report page, part 3: grouping the flat gap list into the report's two-level tree (section per
// kind, group per entity) - source-link/coverage badges, A-Z letter helpers, kind labels and sort
// order, and buildTree/kindSection themselves.

// The Jellyfin collectionType to scope a search to, from a gap's media domain. Empty means an
// unscoped (all-libraries) search, used for a person/creator.
function domainScope(domainName) {
    switch (domainName) {
        case 'Movies': return 'movies';
        case 'Shows': return 'tvshows';
        case 'Music': return 'music';
        case 'Books': return 'books';
        case 'MusicVideos': return 'musicvideos';
        default: return '';
    }
}

// A Jellyfin search URL for a name, optionally scoped to a collectionType. Built from the
// current page URL so it targets this same server whatever the web root is.
function searchUrl(name, collectionType) {
    var base = window.location.href.split('#')[0] + '#/search?';
    var scope = collectionType ? 'collectionType=' + encodeURIComponent(collectionType) + '&' : '';
    return base + scope + 'query=' + encodeURIComponent(name).replace(/%20/g, '+');
}

// A magnifying-glass that opens a Jellyfin search for this title/creator in a new tab. Keeps
// the referrer (noopener only, not noreferrer) so the search page knows it came from here.
function searchIcon(name, collectionType) {
    if (!name) { return ''; }
    return ' ' + jellyfinLink(
        {
            'class': 'cgLink cgSearch emby-button', href: searchUrl(name, collectionType),
            title: 'Search this Jellyfin for “' + name + '”', 'aria-label': 'Search Jellyfin for ' + name
        },
        icon('search'));
}

function groupHtml(level, label, count, collapsed, inner, itemId, extra, lazyToken) {
    // A per-render id ties the header to its body for assistive tech (aria-controls), and
    // aria-expanded mirrors the collapse state (kept in sync on toggle and re-render).
    var bodyId = 'cgBody' + (++cgGroupSeq);
    var hdr = wrap('div', {
        'class': 'cgHdr cgHdr' + level, role: 'button', tabindex: '0',
        'aria-expanded': collapsed ? 'false' : 'true', 'aria-controls': bodyId
    }, h('span', { 'class': 'cgCaret' }).outerHTML
    + h('span', { 'class': 'cgLabel' }, label).outerHTML
    + ' ' + h('span', { 'class': 'cgCount' }, '(' + count + ')').outerHTML
    + (extra || '') + openIcon(itemId));
    // A deferred group ships an empty body plus a token; ensureGroupBody fills it on expand.
    return wrap('div', {
        'class': 'cgGroup cgL' + level + (collapsed ? ' cgCollapsed' : ''),
        'data-cglabel': label, 'data-cglazy': lazyToken
    }, hdr + wrap('div', { 'class': 'cgBody', id: bodyId }, inner));
}

// A coverage badge ("6 of 9 owned, 67%") for a set whose owned/total counts are known.
// A small dot for a group header when any of its items has a streaming source matching the
// current provider filters, so a collapsed group's streamability shows at a glance. The
// length guard keeps it cheap when no availability has been looked up (the common case).
var STREAM_DOT = ' <span class="cgStreamDot" title="Has a streamable title" aria-hidden="true"></span>';
function anyStream(items) {
    return items.some(function (it) { return it.Availability && it.Availability.length && filterOffers(it.Availability).length; });
}
function streamDot(items) { return anyStream(items) ? STREAM_DOT : ''; }

// A CSS-safe per-provider class so a stylesheet can target a specific service's link (inject a
// service icon, recolor it): "TMDB" -> "cgProvider-tmdb", "TheTVDB" -> "cgProvider-thetvdb",
// "MusicBrainz" -> "cgProvider-musicbrainz". The raw name is also on a data-provider attribute.
function providerClass(name) {
    var slug = (name || '').toLowerCase().replace(/[^a-z0-9]+/g, '');
    return slug ? ' cgProvider-' + slug : '';
}

// Links to the source's own page (an author on OpenLibrary, an actor or director on TMDB, an
// artist on Discogs/MusicBrainz, a studio/keyword/label/collection on its provider), shown on
// the group header so you can open the creator or set itself, not just its missing items.
function sourceLinks(item) {
    return ((item && item.SourceLinks) || []).map(function (l) {
        return ' ' + providerLink(l);
    }).join('');
}

function coverageBadge(item) {
    if (!item || !item.SetTotalCount || item.SetOwnedCount == null) { return ''; }
    var pct = Math.round(item.SetOwnedCount / item.SetTotalCount * 100);
    var full = item.SetOwnedCount + ' of ' + item.SetTotalCount + ' owned, ' + pct + '%';
    return ' ' + wrap('span', { 'class': 'cgCoverage', title: full },
        h('span', { 'class': 'cgCovFull' }, full).outerHTML + h('span', { 'class': 'cgCovPct' }, pct + '%').outerHTML);
}

// A compact Diagnose control for a season header, run against one of the season's episodes, so
// the popup can say whether the season belongs to the series you own or a same-named reboot.
function seasonDiagnoseBtn(gapId, name) {
    return ' ' + idAnchor('cgDiagnose', gapId, 'Is this season really part of the series you own?', {
        'data-name': name, 'aria-label': 'Diagnose this season'
    }, icon('troubleshoot'));
}

// Body of a source group: episode gaps get an extra collapsible Season level (season 0 is
// Specials); everything else lists rows directly.
function sourceBody(items) {
    var hasSeason = items.some(function (it) { return it.Season != null; });
    if (!hasSeason) { return sortRows(items).map(renderRow).join(''); }

    var bySeason = groupBy(items, function (it) { return it.Season == null ? 'na' : String(it.Season); });
    bySeason.order.sort(function (a, b) {
        var na = a === 'na' ? 1e9 : Number(a);
        var nb = b === 'na' ? 1e9 : Number(b);
        if (na <= 0) { na = 1e9 + na; }   // specials (0) and unknown sort after numbered seasons
        if (nb <= 0) { nb = 1e9 + nb; }
        return na - nb;
    });
    return bySeason.order.map(function (key) {
        var n = Number(key);
        var label = key === 'na' ? 'Other' : (n <= 0 ? 'Specials' : 'Season ' + n);
        var seasonItems = bySeason.map[key];
        // The season's open-in-Jellyfin link needs a season item, but only library-known
        // episodes carry one (a cross-check discovery for a wholly-unowned season has none).
        // Take it from any episode that has it, not just the first, so the link is not dropped
        // just because a linkless cross-check episode happens to sort first.
        var seasonId = '';
        for (var si = 0; si < seasonItems.length; si++) { if (seasonItems[si].SeasonItemId) { seasonId = seasonItems[si].SeasonItemId; break; } }
        // Always offer a search (for the series, since a "Season 3" query is useless) and an
        // open link: the season itself when owned, otherwise its series, so there is always
        // somewhere to go.
        var seriesName = seasonItems[0].SourceItemName;
        var openId = seasonId || seasonItems[0].SourceItemId;
        // Diagnose the season via one of its episodes: does it belong to the series you own, or
        // to a same-named reboot? Skip Specials/Other (a year comparison there is meaningless).
        var seasonDiag = (key !== 'na' && n > 0)
            ? seasonDiagnoseBtn(seasonItems[0].Id, (seriesName ? seriesName + ' ' : '') + label)
            : '';
        var seasonExtra = searchIcon(seriesName, 'tvshows')
            + openIcon(openId)
            + clearBtn('season', (seriesName || '') + '|' + key, 'this season')
            + seasonDiag + batchDismissBtns(label);
        return groupHtml(3, label, seasonItems.length, true, sortRows(seasonItems).map(renderRow).join(''), '', seasonExtra);
    }).join('');
}

// The grouping letter of a name: skip leading punctuation/whitespace, fold accents, a digit
// (or anything non-alphabetic) becomes '#'.
function firstLetter(name) {
    // First letter or number in any script (the u flag makes this code-point aware, so
    // surrogate-pair letters such as CJK extensions are handled whole).
    var m = (name || '').trim().match(/[\p{L}\p{N}]/u);
    if (!m) { return '#'; }
    var c = m[0];
    if (/\p{N}/u.test(c)) { return '#'; }
    // Fold Latin diacritics so 'caf\u00e9' files under C, but keep other scripts (Cyrillic, Greek,
    // CJK, ...) under their own letter rather than tossing them together.
    if (c.normalize) {
        var base = c.normalize('NFD').charAt(0);
        if ((base >= 'A' && base <= 'Z') || (base >= 'a' && base <= 'z')) { return base.toUpperCase(); }
    }
    return c.toUpperCase();
}

// A person is filed under both their first and last initial (Teri Hatcher -> T and H).
function personLetters(name) {
    var parts = (name || '').trim().split(/\s+/).filter(function (p) { return p; });
    if (!parts.length) { return ['#']; }
    var letters = [firstLetter(parts[0])];
    if (parts.length > 1) {
        var last = firstLetter(parts[parts.length - 1]);
        if (last !== letters[0]) { letters.push(last); }
    }
    return letters;
}

// A title is filed under its first letter, and if it leads with the article "The" also under the
// next word's letter (The Highlander -> T and H), so it is found either way.
function titleLetters(name) {
    var letters = [firstLetter(name)];
    var rest = (name || '').trim().replace(/^the\s+/i, '');
    if (rest && rest.length !== (name || '').trim().length) {
        var next = firstLetter(rest);
        if (letters.indexOf(next) === -1) { letters.push(next); }
    }
    return letters;
}

// A-Z, then '#' last.
function letterSort(a, b) {
    if (a === b) { return 0; }
    if (a === '#') { return 1; }
    if (b === '#') { return -1; }
    return a < b ? -1 : 1;
}

// The letters an item files under for the A-Z selector: a creator under their first and last
// initial, a recommendation under its title, a set under its source (series/collection) name.
// A title that leads with "The" also files under the next word's letter (see titleLetters).
function itemLetters(it, pattern) {
    if (pattern === 'CreatorWorks') { return personLetters(it.SourceItemName || it.Name); }
    if (pattern === 'Recommendation') { return titleLetters(it.SourceItemName || it.Name); }
    return titleLetters(it.SourceItemName || it.Name);
}

// The sorted distinct letters present across the items, for the current pattern.
function lettersOf(items, pattern) {
    var present = {};
    items.forEach(function (it) { itemLetters(it, pattern).forEach(function (L) { present[L] = true; }); });
    return Object.keys(present).sort(letterSort);
}

// How many items land under each letter, for the jump bar's pill counts. An item can land under two
// letters ("The Matrix" is findable at both T and M), so these do not sum to the item total; the "*"
// pill's count is the caller's own items.length instead.
function letterCounts(items, pattern) {
    var counts = {};
    items.forEach(function (it) {
        itemLetters(it, pattern).forEach(function (L) { counts[L] = (counts[L] || 0) + 1; });
    });
    return counts;
}

// The "kind of set" a SetCompletion gap completes, from its owning item's type, so the tab can
// group collections, studios, keywords, series, and discographies into separate sections.
// The wording for each Set completion kind. The set of kinds and their order come from the server
// (summary.SetKinds); this only says how each is spelled on screen. A kind with no entry here falls
// back to its own name, so a new one reads as itself rather than being pooled into a bucket.
var SET_KIND_LABELS = {
    BoxSet: 'Collections & franchises',
    Series: 'Series',
    MusicArtist: 'Discography',
    MusicLabel: 'Record labels',
    Studio: 'Studios',
    Keyword: 'Keywords',
    Subject: 'Subjects'
};
function setKindLabel(sourceItemType) {
    return SET_KIND_LABELS[sourceItemType] || sourceItemType || 'Other';
}

// The wording for each Discover section kind, the same contract as SET_KIND_LABELS: the kinds and their
// order come from the server (summary.DiscoverKinds), this only says how each is spelled. The two
// recommendation kinds share one label on purpose, so per-title suggestions read as one section however
// many owned titles are behind them.
var DISCOVER_KIND_LABELS = {
    EveryoneWatchlist: "Everyone's watchlist",
    TmdbAccountList: 'TMDB account lists',
    TraktWatchlist: 'Trakt watchlist',
    MdbListWatchlist: 'MDBList watchlist',
    JustWatchList: 'JustWatch watchlist',
    ImdbList: 'IMDb lists',
    DiscogsWantlist: 'Discogs wantlist',
    OpenLibraryShelf: 'OpenLibrary shelves',
    TvdbFavorites: 'TheTVDB favorites',
    List: 'TMDB lists',
    MdbList: 'MDBList community lists',
    TraktList: 'Trakt lists',
    TmdbMovieDiscover: 'TMDB discover feeds',
    Movie: 'Recommended by what you own',
    Series: 'Recommended by what you own'
};
function discoverKindLabel(sourceItemType) {
    return DISCOVER_KIND_LABELS[sourceItemType] || sourceItemType || 'Other';
}

// A row's section heading, which table depends on the tab it is on.
function kindLabelOf(it) {
    return it.PatternName === 'Recommendation' ? discoverKindLabel(it.SourceItemType) : setKindLabel(it.SourceItemType);
}

// Comparator for Discover section headings, ordered as the server lists their kinds.
function discoverKindCompare() {
    var order = [];
    vocab().discoverKinds.map(discoverKindLabel).forEach(function (l) {
        if (order.indexOf(l) === -1) { order.push(l); }
    });
    return function (a, b) { return rankIn(order, a) - rankIn(order, b); };
}

// Comparator for Set completion group headings, ordered as the server lists their kinds. Groups are
// keyed by label, so the served kinds are mapped through the wording once per sort rather than per
// comparison.
function setKindCompare() {
    var order = vocab().setKinds.map(setKindLabel);
    return function (a, b) { return rankIn(order, a) - rankIn(order, b); };
}

// Comparator for media domains, ordered as the server lists them.
function domainCompare() {
    var order = vocab().domains;
    return function (a, b) { return rankIn(order, a) - rankIn(order, b); };
}

// The H2 source group for the Markdown export, mirroring the on-screen tree: a set's kind for
// Set completion (Collections & franchises, Studios, Keywords, Series, Discography), otherwise
// the owning source (the creator, or the title a recommendation came from).
function exportGroupLabel(it) {
    return it.PatternName === 'CreatorWorks' ? (it.SourceItemName || '(no source)') : kindLabelOf(it);
}

// Order the export's source groups: set and discover kinds in their canonical order, creators
// alphabetically.
function exportGroupSort(pattern) {
    if (pattern === 'SetCompletion') {
        return setKindCompare();
    }

    if (pattern === 'Recommendation') {
        return discoverKindCompare();
    }

    return ci;
}

// A GitHub-style heading anchor (lowercase, punctuation dropped, whitespace to hyphens), so the
// contents links resolve in the same renderers (GitHub, VS Code preview) that auto-anchor headings.
function mdAnchor(text) {
    return String(text == null ? '' : text).toLowerCase().replace(/[^\w\s-]/g, '').replace(/\s/g, '-') || 'section';
}

// An allocator of unique heading anchors, called in heading order, de-duping like the renderer
// does (a repeated heading gets "-1", "-2", ...), so a table of contents link resolves.
function anchorAllocator() {
    var used = {};
    return function (text) {
        var base = mdAnchor(text), a = base, n = 1;
        while (used[a]) { a = base + '-' + n; n++; }
        used[a] = true;
        return a;
    };
}

// One collapsed set cell (a collection, series, studio, ...) for the Set completion grid.
function setSourceCell(src, srcItems) {
    var covItem = srcItems[0];
    for (var i = 0; i < srcItems.length; i++) { if (srcItems[i].SetTotalCount) { covItem = srcItems[i]; break; } }
    var isEpisodic = srcItems.some(function (it) { return it.Season != null; });
    // Scope the search to the right library kind for this set's domain: a movie collection
    // lives in box sets, a series in the shows libraries, an album artist in music.
    var domain = categoryOf(srcItems[0]);
    var searchScope = domain === 'Movies' ? 'boxsets' : domainScope(domain);
    // Trailing controls: streamable dot, coverage, search, open-in-Jellyfin, then the batch
    // dismiss buttons (episodic sets only). The open icon goes in extra (itemId is '' so
    // groupHtml does not also append it).
    var extra = streamDot(srcItems)
        + coverageBadge(covItem)
        + searchIcon(src, searchScope)
        + openIcon(srcItems[0].SourceItemId)
        + clearBtn('group', src, 'everything listed under ' + src)
        + (isEpisodic ? batchDismissBtns(src) : '')
        + sourceLinks(srcItems[0]);
    // For a series, put the show's year in the header so same-named reboots are distinguishable
    // ("Quantum Leap (1989)" vs "Quantum Leap (2022)"); the plain name still drives search and dismiss.
    var title = (isEpisodic && srcItems[0].SourceItemYear) ? src + ' (' + srcItems[0].SourceItemYear + ')' : src;
    return groupHtml(2, title, srcItems.length, true, sourceBody(srcItems), '', extra);
}

// Render the current pattern's entities for the items passed (already scoped to one domain and,
// via the A-Z selector, usually one letter): recommended titles as rows, creators as groups, or
// the set grid. The A-Z bar handles letters, so there is no in-tree letter grouping.
function buildTree(items) {
    if (!items.length) { return ''; }
    var pattern = items[0].PatternName;
    lazyBodies = {}; // tokens are per-render; drop the previous render's builders

    if (pattern === 'Recommendation') {
        // Two levels, as Set completion has: a section per kind of discovery source (your watchlists, the
        // lists you pointed it at, the recommender), then a group per list or recommending title inside it.
        // Flat, the list you keep and the title that happened to suggest something were peers with nothing
        // to tell them apart. A multi-source gap files under its primary source; its other sources stay on
        // the row ("Also recommended by").
        var byKind = groupBy(items, kindLabelOf);
        byKind.order.sort(discoverKindCompare());
        return byKind.order.map(function (kind) {
            var bySource = groupBy(byKind.map[kind], function (it) { return it.SourceItemName || '(no source)'; });
            bySource.order.sort(ci);
            var groups = bySource.order.map(function (src) {
                var sItems = bySource.map[src];
                var token = 'lz' + (++cgGroupSeq);
                lazyBodies[token] = function () { return sortRows(sItems).map(renderRow).join(''); };
                return groupHtml(2, src, sItems.length, true, '', sItems[0].SourceItemId,
                    streamDot(sItems)
                    + searchIcon(src, '')
                    + clearBtn('group', src, 'everything listed under ' + src)
                    + recSourceDismissBtn(sItems[0].SourceItemId, src)
                    + sourceLinks(sItems[0]), token);
            }).join('');
            return kindSection(kind, groups);
        }).join('') + emptyRunSections(byKind.map);
    }

    if (pattern === 'CreatorWorks') {
        var byCreator = groupBy(items, function (it) { return it.SourceItemName || '(no source)'; });
        byCreator.order.sort(ci);
        return byCreator.order.map(function (src) {
            var cItems = byCreator.map[src];
            // Defer the rows: a creator's body is built only when its header is expanded, so a
            // tab with tens of thousands of rows renders just the headers up front.
            var token = 'lz' + (++cgGroupSeq);
            lazyBodies[token] = function () { return sortRows(cItems).map(renderRow).join(''); };
            return groupHtml(2, src, cItems.length, true, '', cItems[0].SourceItemId,
                streamDot(cItems)
                + searchIcon(src, '')
                + clearBtn('group', src, 'everything listed under ' + src)
                + creatorDismissBtn(cItems[0].SourceItemId, src)
                + sourceLinks(cItems[0]), token);
        }).join('');
    }

    // SetCompletion: split by the kind of set, then lay each kind's collapsed sources out in a
    // responsive grid. One domain can hold several kinds (Movies has collections, studios, and
    // keywords), so each kind gets a heading; with a single kind the heading is dropped.
    var byKind = groupBy(items, function (it) { return setKindLabel(it.SourceItemType); });
    byKind.order.sort(setKindCompare());
    var multiKind = byKind.order.length > 1;
    return byKind.order.map(function (kind) {
        var bySrc = groupBy(byKind.map[kind], function (it) { return it.SourceItemName || '(no source)'; });
        bySrc.order.sort(ci);
        var srcHtml = bySrc.order.map(function (src) { return setSourceCell(src, bySrc.map[src]); }).join('');
        var grid = wrap('div', { 'class': 'cgGridWrap' }, srcHtml);
        // A single kind needs no header; with several kinds in one domain (Movies has
        // collections, studios, and keywords) each kind gets a collapsible header, reusing the
        // group machinery so its caret, keyboard toggle, and persisted state all come for free.
        if (!multiKind) { return grid; }
        return kindSection(kind, grid);
    }).join('');
}

// A collapsible section heading over a kind's groups, reusing the group machinery so its caret, keyboard
// toggle, and persisted collapse state all come for free. Shared by Set completion and Discover.
function kindSection(kind, body, noClear) {
    var hdr = wrap('div', { 'class': 'cgHdr cgKindHdr', role: 'button', tabindex: '0', 'aria-expanded': 'true' },
        h('span', { 'class': 'cgCaret' }).outerHTML + h('span', { 'class': 'cgLabel' }, kind).outerHTML
        + (noClear ? '' : clearBtn('kind', kind, 'everything under ' + kind)));
    return wrap('div', { 'class': 'cgGroup cgKindGroup', 'data-cglabel': 'kind:' + kind },
        hdr + wrap('div', { 'class': 'cgBody' }, body));
}

// Sections for the discovery lists that were read on the last scan and left nothing to show, so "you
// already own everything on it" and "it could not be read" stop looking exactly like "it never ran".
// Suppressed in a filtered view: a run carries no domain or letter, so it cannot honestly be placed in one.
function emptyRunSections(present) {
    var page = reportPage();
    if (!page || page._letter !== '*') { return ''; }
    if (page._domain) { return ''; }

    var byLabel = {};
    ((page._report && page._report.SourceRuns) || []).forEach(function (r) {
        var label = discoverKindLabel(r.Kind);
        if (present[label]) { return; }
        var e = byLabel[label] || (byLabel[label] = { failed: false, names: [] });
        if (r.Failed) { e.failed = true; }
        if (r.Name && e.names.indexOf(r.Name) === -1) { e.names.push(r.Name); }
    });

    return Object.keys(byLabel).sort(discoverKindCompare()).map(function (label) {
        var e = byLabel[label];
        var msg = e.failed
            ? e.names.join(', ') + ' could not be read on the last scan, so nothing from it is listed here.'
            : 'Read on the last scan, and you own everything on it.';
        return kindSection(label, h('p', { 'class': 'fieldDescription cgEmptyRun' }, msg).outerHTML, true);
    }).join('');
}

