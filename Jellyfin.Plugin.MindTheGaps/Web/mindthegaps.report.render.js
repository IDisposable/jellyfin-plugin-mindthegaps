// Report page, part 7: the main render pipeline (applyAndRender: filter, sort, build the visible
// tree), selection, saved views, loading/refreshing the report, and starting a scan or
// availability pass.

function applyAndRender(page) {
    var report = page._report || { Items: [] };
    currentSort = page.querySelector('#cgSort').value || 'title';
    renderTabs(page);
    renderTypeFilter(page);
    var pass = buildFilter(page);
    var items = (report.Items || []).filter(function (it) {
        return it.PatternName === page._pattern && pass(it);
    });

    // Resolve the A-Z selection: keep the current letter if still present, else default to "*"
    // for a small list (show everything) or the first letter for a large one (render one letter
    // at a time). displayItems is what the list renders; the rollup still summarises the whole tab.
    var letters = lettersOf(items, page._pattern);
    var letter = page._letter;
    if (letter !== '*' && letters.indexOf(letter) === -1) { letter = null; }
    if (!letter) { letter = (letters.length <= 1 || items.length <= 400) ? '*' : letters[0]; }
    page._letter = letter;
    var displayItems = letter === '*' ? items
        : items.filter(function (it) { return itemLetters(it, page._pattern).indexOf(letter) !== -1; });

    // What the list is about to show, so a group's clear-down verifies exactly the rows on screen
    // (including in a collapsed group, whose rows are not in the DOM until it is expanded).
    page._shown = displayItems;

    var streamable = page.querySelector('#cgStreamable').checked;
    var empty;
    if (streamable && !(report.Items || []).some(function (it) { return it.AvailabilityChecked; })) {
        empty = h('p', { 'class': 'fieldDescription' }, 'No "where to watch" data yet, so this filter has nothing to act on. Look it up in the background, then it fills in here.').outerHTML
            + wrap('button', { is: 'emby-button', type: 'button', id: 'cgEnableAvail', 'class': 'raised button-submit' },
                h('span', null, 'Look up where to watch').outerHTML);
    } else {
        // The domain has gaps overall (summary count) but none pass the filters: name the
        // filters that are on so the user knows what to relax, rather than a dead-end blank.
        // The pattern selector is not a "hide" filter here, so it is handled separately: if the
        // chosen pattern is empty but the domain has gaps under another pattern, say so. Both
        // checks come from the summary counts, not report.Items, which is already narrowed to
        // this exact domain+pattern by the fetch and so cannot see what another pattern has.
        var patternCounts = patternCountsFor(page, page._domain);
        var rawForPattern = patternCounts[page._pattern] || 0;
        var rawForDomain = Object.keys(patternCounts).reduce(function (sum, k) { return sum + patternCounts[k]; }, 0);
        var otherPatternHasRaw = rawForPattern === 0 && rawForDomain > 0;
        var active = [];
        if ((page.querySelector('#cgSearch').value || '').trim()) { active.push('the search box'); }
        if (page.querySelector('#cgHideSpecials').checked) { active.push('"Hide specials"'); }
        if (page.querySelector('#cgHideUpcoming').checked) { active.push('"Hide upcoming"'); }
        if (streamable) { active.push('"Hide items with no sources"'); }
        if (otherPatternHasRaw) {
            empty = h('p', { 'class': 'fieldDescription' }, 'No ' + patternLabel(page._pattern, page._domain) + ' gaps in this domain. Pick another pattern from the menu above.').outerHTML;
        } else if (rawForPattern > 0 && active.length) {
            var list = active.length === 1 ? active[0]
                : active.slice(0, -1).join(', ') + ' or ' + active[active.length - 1];
            empty = h('p', { 'class': 'fieldDescription' }, 'No gaps match the current filters. Try clearing ' + list + '.').outerHTML;
        } else if (rawForPattern > 0) {
            // No filters on, yet nothing shows: the rows are all dismissed.
            empty = h('p', { 'class': 'fieldDescription' }, 'Every gap on this tab is dismissed. Turn on "Show dismissed" to see them.').outerHTML;
        } else {
            empty = h('p', { 'class': 'fieldDescription' }, 'No gaps on this tab. Pick another tab, or rescan to refresh.').outerHTML;
        }
    }

    var listEl = page.querySelector('#cgList');

    // Snapshot what the user has expanded/collapsed/selected and where they are scrolled, so a
    // re-render (resolving a row, toggling a filter) does not throw it all away.
    var collapsed = {}, checkedSel = {};
    var pg = listEl.querySelectorAll('.cgGroup');
    for (var gi = 0; gi < pg.length; gi++) { collapsed[groupKey(pg[gi])] = pg[gi].classList.contains('cgCollapsed'); }
    var psel = listEl.querySelectorAll('.cgSel:checked');
    for (var sk = 0; sk < psel.length; sk++) { checkedSel[psel[sk].getAttribute('data-gapid')] = true; }
    var scroller = scrollerFor(page);
    var scrollY = scroller.scrollTop;

    // With nothing to list, Discover still says what its lists did, so a tab that is empty because every
    // list was read and holds nothing reads that way rather than as a tab that never ran.
    var noneHtml = (page._pattern === 'Recommendation' && emptyRunSections({})) || empty;
    listEl.innerHTML = displayItems.length ? buildTree(displayItems) : noneHtml;

    // Restore the snapshot onto whichever groups/rows still exist after the rebuild.
    var ng = listEl.querySelectorAll('.cgGroup');
    for (var ngi = 0; ngi < ng.length; ngi++) {
        var k = groupKey(ng[ngi]);
        if (k in collapsed) { ng[ngi].classList.toggle('cgCollapsed', collapsed[k]); }
        // A group restored to expanded needs its deferred body built now, so its rows are
        // present for the open-row and selection restore (and visible) after the rebuild.
        if (!ng[ngi].classList.contains('cgCollapsed')) { ensureGroupBody(ng[ngi]); }
    }
    syncGroupAria(listEl);
    var nsel = listEl.querySelectorAll('.cgSel');
    for (var nsi = 0; nsi < nsel.length; nsi++) { if (checkedSel[nsel[nsi].getAttribute('data-gapid')]) { nsel[nsi].checked = true; } }
    scroller.scrollTop = scrollY;

    renderLetterBar(page, letters, letter, letterCounts(items, page._pattern), items.length);
    var rollup = page.querySelector('#cgRollup');
    var rh = items.length ? rollupHtml(items) : '';
    rollup.innerHTML = rh;
    rollup.style.display = rh ? 'block' : 'none';
    renderHiddenCreators(page);
    refreshSelectBar(page);
    updateSelection(page);
}

function updateSelection(page) {
    var n = page.querySelectorAll('#cgList .cgSel:checked').length;
    page.querySelector('#cgSelCount').textContent = n;
    page.querySelector('#cgMintSelected').disabled = n === 0;
    page.querySelector('#cgTodoSelCount').textContent = n;
    page.querySelector('#cgTodoSelected').disabled = n === 0;

    // Send/Request appear at all only once acqConfig says the matching target is set up (the same rule
    // a per-row Send button already follows), and are disabled with nothing checked either way.
    var arrBtn = page.querySelector('#cgSendArrSelected');
    var canArr = !!(acqConfig && (acqConfig.RadarrConfigured || acqConfig.SonarrConfigured));
    arrBtn.style.display = canArr ? '' : 'none';
    arrBtn.disabled = n === 0;
    page.querySelector('#cgSendArrSelCount').textContent = n;

    var seerrBtn = page.querySelector('#cgSendSeerrSelected');
    var canSeerr = !!(acqConfig && acqConfig.SeerrConfigured);
    seerrBtn.style.display = canSeerr ? '' : 'none';
    seerrBtn.disabled = n === 0;
    page.querySelector('#cgSendSeerrSelCount').textContent = n;

    page.querySelector('#cgResolveSelCount').textContent = n;
    page.querySelector('#cgResolveSelected').disabled = n === 0;
}

// Show the multi-select bar once any selectable row exists. Deferred creator-works bodies have
// no rows until expanded, so the bar is re-evaluated when a group is opened, not just on render.
function refreshSelectBar(page) {
    page.querySelector('#cgSelectBar').style.display = page.querySelector('#cgList .cgSel') ? 'flex' : 'none';
}

// The ids of the checked rows. Mint rehydrates each from the stored report server-side, so the
// client only needs to name them, not ship the whole gap object.
function selectedGapIds(page) {
    var out = [];
    var cbs = page.querySelectorAll('#cgList .cgSel:checked');
    for (var i = 0; i < cbs.length; i++) {
        var id = cbs[i].getAttribute('data-gapid');
        if (id) { out.push(id); }
    }
    return out;
}

// Persist the report filters per browser (not server config; these are personal view prefs).
var STORAGE_KEY = 'mindthegaps.filters';

function saveFilters(page) {
    try {
        var state = {
            pattern: page.querySelector('#cgTypeFilter').value,
            sort: page.querySelector('#cgSort').value,
            hideSpecials: page.querySelector('#cgHideSpecials').checked,
            hideUpcoming: page.querySelector('#cgHideUpcoming').checked,
            showResolved: page.querySelector('#cgShowResolved').checked,
            streamable: page.querySelector('#cgStreamable').checked,
            compact: page.querySelector('#cgCompact').checked,
            letter: page._letter,
            mon: {}
        };
        var cbs = page.querySelectorAll('.cgMon');
        for (var i = 0; i < cbs.length; i++) { state.mon[cbs[i].getAttribute('data-mon')] = cbs[i].checked; }
        state.knownProviders = knownProviders;
        state.disabledProviders = disabledProviders;
        state.providersExpanded = providersExpanded;
        localStorage.setItem(STORAGE_KEY, JSON.stringify(state));
    } catch (e) { /* localStorage unavailable; ignore */ }
}

function restoreFilters(page) {
    var state;
    try { state = JSON.parse(localStorage.getItem(STORAGE_KEY) || '{}'); } catch (e) { state = {}; }
    // The Pattern options are built per domain tab, so remember the wanted pattern and let
    // renderTypeFilter/pickPattern apply it once that domain's patterns are known.
    page._wantPattern = state.pattern || '';
    if (state.sort != null) { page.querySelector('#cgSort').value = state.sort; }
    if (state.hideSpecials != null) { page.querySelector('#cgHideSpecials').checked = !!state.hideSpecials; }
    if (state.hideUpcoming != null) { page.querySelector('#cgHideUpcoming').checked = !!state.hideUpcoming; }
    if (state.showResolved != null) { page.querySelector('#cgShowResolved').checked = !!state.showResolved; }
    if (state.streamable != null) { page.querySelector('#cgStreamable').checked = !!state.streamable; }
    page._compact = !!state.compact;
    page.querySelector('#cgCompact').checked = page._compact;
    page.querySelector('#cgReportPanel').classList.toggle('cgCompactMode', page._compact);
    if (state.letter != null) { page._letter = state.letter; }
    if (state.mon) {
        var cbs = page.querySelectorAll('.cgMon');
        for (var i = 0; i < cbs.length; i++) {
            var k = cbs[i].getAttribute('data-mon');
            if (state.mon[k] != null) { cbs[i].checked = !!state.mon[k]; }
        }
    }
    // A list saved by a browser that held provider names as TMDB gave them is folded to services too.
    knownProviders = Array.isArray(state.knownProviders) ? providerFamilies(state.knownProviders) : [];
    disabledProviders = state.disabledProviders || {};
    providersExpanded = !!state.providersExpanded;
    renderProviderFilter(page);
}

// Named saved views: a snapshot of every filter (and the active tab) the user can name and
// re-apply later. Stored per browser, separate from the live filter state.
var VIEWS_KEY = 'mindthegaps.views';

function loadViews() {
    try { return JSON.parse(localStorage.getItem(VIEWS_KEY) || '{}') || {}; } catch (e) { return {}; }
}

function storeViews(views) {
    try { localStorage.setItem(VIEWS_KEY, JSON.stringify(views)); } catch (e) { /* ignore */ }
}

function captureView(page) {
    var mon = {};
    var cbs = page.querySelectorAll('.cgMon');
    for (var i = 0; i < cbs.length; i++) { mon[cbs[i].getAttribute('data-mon')] = cbs[i].checked; }
    return {
        pattern: page._pattern,
        type: page._domain,
        sort: page.querySelector('#cgSort').value,
        search: page.querySelector('#cgSearch').value || '',
        hideSpecials: page.querySelector('#cgHideSpecials').checked,
        hideUpcoming: page.querySelector('#cgHideUpcoming').checked,
        showResolved: page.querySelector('#cgShowResolved').checked,
        streamable: page.querySelector('#cgStreamable').checked,
        compact: page.querySelector('#cgCompact').checked,
        letter: page._letter,
        mon: mon,
        disabledProviders: disabledProviders
    };
}

// The share link carries only what differs from the defaults and omits the streaming-provider
// filter: it is the bulk of the data and is server-specific, so a recipient's providers are not
// the sharer's anyway. This keeps the link well under URL length limits. Saved views
// (localStorage, no length limit) keep the full state via captureView.
function compactView(page) {
    var v = captureView(page);
    delete v.disabledProviders;
    if (!v.type) { delete v.type; }
    if (!v.search) { delete v.search; }
    if (v.letter == null) { delete v.letter; }
    if (!v.hideSpecials) { delete v.hideSpecials; }
    if (!v.hideUpcoming) { delete v.hideUpcoming; }
    if (!v.showResolved) { delete v.showResolved; }
    if (!v.streamable) { delete v.streamable; }
    if (!v.compact) { delete v.compact; }
    if (v.mon) {
        var allOn = true;
        for (var k in v.mon) { if (!v.mon[k]) { allOn = false; break; } }
        if (allOn) { delete v.mon; }
    }
    return v;
}

// Build a link to the current view by stamping the captured view object into a "cgview" query
// param on the page's hash (Jellyfin is a hash router), so a paste re-opens the same tab and
// filters. Any existing cgview is replaced.
function shareUrl(page) {
    var encoded = encodeURIComponent(JSON.stringify(compactView(page)));
    var href = window.location.href;
    var hashIdx = href.indexOf('#');
    var base = hashIdx === -1 ? href : href.slice(0, hashIdx);
    var hash = hashIdx === -1 ? '/configurationpage?name=MindTheGaps' : href.slice(hashIdx + 1);
    var qIdx = hash.indexOf('?');
    var path = qIdx === -1 ? hash : hash.slice(0, qIdx);
    var query = qIdx === -1 ? '' : hash.slice(qIdx + 1);
    var params = query ? query.split('&').filter(function (p) { return p && p.indexOf('cgview=') !== 0; }) : [];
    params.push('cgview=' + encoded);
    return base + '#' + path + '?' + params.join('&');
}

// Read and remove a shared view from the current URL (consume-once): decode the "cgview" param,
// then strip it from the address bar with replaceState (no hashchange, so the router is undisturbed)
// so a later reload falls back to the per-browser saved filters instead of snapping back.
function consumeUrlView() {
    var hash = window.location.hash || '';
    var qIdx = hash.indexOf('?');
    if (qIdx === -1) { return null; }
    var parts = hash.slice(qIdx + 1).split('&');
    var view = null;
    var kept = [];
    for (var i = 0; i < parts.length; i++) {
        if (parts[i].indexOf('cgview=') === 0) {
            try { view = JSON.parse(decodeURIComponent(parts[i].slice('cgview='.length))); } catch (e) { view = null; }
        } else if (parts[i]) {
            kept.push(parts[i]);
        }
    }
    if (view) {
        try {
            var path = hash.slice(0, qIdx);
            var newHash = kept.length ? path + '?' + kept.join('&') : path;
            window.history.replaceState(null, '', window.location.pathname + window.location.search + newHash);
        } catch (e) { /* replaceState may be blocked; harmless, the view still applies */ }
    }
    return view;
}

// Read and remove a deep-link to one diagnosis (cgdiag=<gapId>, optional cgdeep=1), so a link
// from an exported audit opens the modal on load. Consume-once, like consumeUrlView.
function consumeUrlDiag() {
    var hash = window.location.hash || '';
    var qIdx = hash.indexOf('?');
    if (qIdx === -1) { return null; }
    var parts = hash.slice(qIdx + 1).split('&');
    var id = null, deep = false, kept = [];
    for (var i = 0; i < parts.length; i++) {
        if (parts[i].indexOf('cgdiag=') === 0) {
            id = decodeURIComponent(parts[i].slice('cgdiag='.length));
        } else if (parts[i].indexOf('cgdeep=') === 0) {
            deep = parts[i].slice('cgdeep='.length) === '1';
        } else if (parts[i]) {
            kept.push(parts[i]);
        }
    }
    if (id) {
        try {
            var path = hash.slice(0, qIdx);
            var newHash = kept.length ? path + '?' + kept.join('&') : path;
            window.history.replaceState(null, '', window.location.pathname + window.location.search + newHash);
        } catch (e) { /* replaceState may be blocked; harmless */ }
    }
    return id ? { id: id, deep: deep } : null;
}

function applyView(page, v) {
    if (!v) { return; }
    if (v.type) {
        page._domain = v.type;
        pruneOtherDomains(page, page._domain);
    }

    page._letter = v.letter != null ? v.letter : null;
    // The Pattern options are rebuilt per domain tab, so route the wanted pattern through _wantPattern.
    page._wantPattern = v.pattern || '';
    if (v.sort != null) { page.querySelector('#cgSort').value = v.sort; }
    page.querySelector('#cgSearch').value = v.search || '';
    page.querySelector('#cgHideSpecials').checked = !!v.hideSpecials;
    page.querySelector('#cgHideUpcoming').checked = !!v.hideUpcoming;
    page.querySelector('#cgShowResolved').checked = !!v.showResolved;
    page.querySelector('#cgStreamable').checked = !!v.streamable;
    page._compact = !!v.compact;
    page.querySelector('#cgCompact').checked = page._compact;
    page.querySelector('#cgReportPanel').classList.toggle('cgCompactMode', page._compact);
    if (v.mon) {
        var cbs = page.querySelectorAll('.cgMon');
        for (var i = 0; i < cbs.length; i++) {
            var k = cbs[i].getAttribute('data-mon');
            if (v.mon[k] != null) { cbs[i].checked = !!v.mon[k]; }
        }
    }
    disabledProviders = v.disabledProviders || {};
    renderProviderFilter(page);
    saveFilters(page);
    // A saved view can switch the domain, so make sure that tab's items are loaded first.
    page._pattern = pickPattern(page, page._domain);
    return ensureSlice(page, page._pattern, page._domain).then(function () { applyAndRender(page); });
}

// Lists creators and recommendation sources dismissed wholesale (with a Restore), so one can
// be brought back even after a rescan has dropped its gaps from the report. Hidden when none.
function renderHiddenCreators(page) {
    var el = page.querySelector('#cgHiddenCreators');

    // Whole-source dismissals only make sense on the two pattern tabs that have them: a creator
    // on Creator works, a seed title on Recommendations. Hide the picker entirely elsewhere.
    if (page._pattern !== 'CreatorWorks' && page._pattern !== 'Recommendation') {
        el.style.display = 'none'; el.innerHTML = ''; return;
    }

    var entries = [];
    Object.keys(resolvedMap).forEach(function (k) {
        if (page._pattern === 'CreatorWorks' && k.indexOf('creator:') === 0) {
            entries.push({ key: k, name: resolvedMap[k].Note || k.slice(8) });
        } else if (page._pattern === 'Recommendation' && k.indexOf('recsource:') === 0) {
            entries.push({ key: k, name: resolvedMap[k].Note || k.slice(10) });
        }
    });
    if (!entries.length) { el.style.display = 'none'; el.innerHTML = ''; return; }
    entries.sort(function (a, b) { return ci(a.name, b.name); });

    var label = page._pattern === 'CreatorWorks' ? 'Muted creators:' : 'Muted sources:';
    var help = page._pattern === 'CreatorWorks'
        ? 'Creators you dismissed wholesale are not scanned for missing films. Pick one and Bring back to scan it again.'
        : 'Owned titles you dismissed as a recommendation seed produce no suggestions. Pick one and Bring back to suggest from it again.';
    el.style.display = '';
    el.title = help;
    el.innerHTML = h('span', { style: 'opacity:.7;margin-left:1em;', title: help }, label).outerHTML + ' '
        + wrap('select', { is: 'emby-select', id: 'cgHiddenCreatorSel', 'class': 'emby-select', style: 'width:auto;', title: help },
            entries.map(function (en) { return h('option', { value: en.key }, en.name).outerHTML; }).join(''))
        + ' ' + wrap('button', { is: 'emby-button', type: 'button', id: 'cgRestoreCreatorBtn', 'class': 'raised', style: 'margin:0;', title: help }, h('span', null, 'Bring back').outerHTML);
}

function renderViews(page) {
    var views = loadViews();
    var names = Object.keys(views).sort(function (a, b) { return ci(a, b); });
    page.querySelector('#cgViews').innerHTML = h('option', { value: '' }, '(choose a saved view)').outerHTML
        + names.map(function (n) { return h('option', { value: n }, n).outerHTML; }).join('');
}

// A slice's cache key: pattern alone when no domain is known, else domain+pattern.
function sliceKey(pattern, domain) { return domain ? domain + '|' + pattern : pattern; }

// Drops every cached slice for a domain, both key forms, so a change underneath the report (a
// rescan, a mint, a bulk recheck) cannot leave a stale pattern-scoped slice behind uninvalidated.
function invalidateDomainSlices(page, domain) {
    if (!page._slices) { return; }
    Object.keys(page._slices).forEach(function (k) {
        if (k === domain || k.indexOf(domain + '|') === 0) { delete page._slices[k]; }
    });
}

// Drops every OTHER domain's cached slices, so a library with a lot of both movies and shows does not
// hold both fully in memory at once just because the browser visited both tabs this session: only the
// domain now in view stays cached (across its own patterns), and switching back to a domain that was
// evicted re-fetches it rather than reusing a stale hold on memory.
function pruneOtherDomains(page, keepDomain) {
    if (!page._slices) { return; }
    Object.keys(page._slices).forEach(function (k) {
        if (k !== keepDomain && k.indexOf(keepDomain + '|') !== 0) { delete page._slices[k]; }
    });
}

// What repeats across a tab's rows travels once (Model/GapRowReport.cs): the pattern and domain every
// row shares, each distinct target kind, and each set's source links. Put them back on the rows, so the
// rest of the page reads one flat shape whatever the wire did.
function expandRows(report) {
    var kinds = report.TargetKinds || [];
    var linkSets = report.SourceLinkSets || [];
    (report.Items || []).forEach(function (it) {
        if (it.PatternName == null) { it.PatternName = report.PatternName; }
        if (it.DomainName == null) { it.DomainName = report.DomainName; }
        it.TargetKindName = kinds[it.TargetKindRef];
        if (it.SourceLinksRef != null) { it.SourceLinks = linkSets[it.SourceLinksRef] || []; }
    });
}

// Fetch one domain's (and, once known, one pattern's) items on demand, cached per domain+pattern, so
// a large report is not shipped whole; the browser only loads the tab and pattern being viewed. Sets
// page._report to that slice.
function ensureSlice(page, pattern, domain, loadId) {
    page._slices = page._slices || {};
    var key = sliceKey(pattern, domain);
    if (page._slices[key]) {
        page._report = page._slices[key];
        return Promise.resolve(page._report);
    }
    Dashboard.showLoadingMsg();
    var query = domain ? { pattern: pattern, domain: domain } : { pattern: pattern };
    return ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('MindTheGaps/Gaps', query), dataType: 'json' })
        .then(function (report) {
            if (loadId != null && page._loadSeq !== loadId) { throw { stale: true }; }
            expandRows(report);
            page._slices[key] = report;
            page._report = report;
            // Seed the provider filter from this slice's offers too (a tab not yet loaded when
            // the summary was built still contributes once opened).
            var offers = [];
            (report.Items || []).forEach(function (it) {
                if (it.Availability && it.Availability.length) { offers = offers.concat(it.Availability); }
            });
            noteProviders(page, offers);
            Dashboard.hideLoadingMsg();
            return report;
        }, function (error) {
            if (loadId != null && page._loadSeq !== loadId) { throw { stale: true }; }
            Dashboard.hideLoadingMsg();
            throw error;
        });
}

function load(page) {
    var loadId = (page._loadSeq || 0) + 1;
    page._loadSeq = loadId;
    Dashboard.showLoadingMsg();
    // Drop any cached slices so a reload (after a scan, mint, or availability pass) re-fetches.
    page._slices = {};
    ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('MindTheGaps/Summary'), dataType: 'json' })
        .then(function (summary) {
            if (page._loadSeq !== loadId) { throw { stale: true }; }
            page._summary = summary;
            // Seed the provider filter from the providers seen across the whole report, so it is
            // populated before any one tab (or "Where to watch" click) loads.
            noteProviders(page, (summary.Providers || []).map(function (n) { return { Provider: n }; }));
            updateAvailButton(page);
            var when = summary.GeneratedUtc && summary.GeneratedUtc.indexOf('0001') !== 0
                ? new Date(summary.GeneratedUtc).toLocaleString()
                : 'never';
            page.querySelector('#cgSummary').textContent =
                (summary.TotalGaps || 0) + ' gaps found. Last scan: ' + when + '.';
            // Fresh install with nothing scanned yet: send the admin to settings to configure and run
            // the first scan. The flag keeps it a one-time nudge, not a redirect on every reload.
            if (when === 'never' && !page._autoSettingsDone) {
                page._autoSettingsDone = true;
                Dashboard.navigate('configurationpage?name=MindTheGapsSettings');
            }
            // Pick a domain that has gaps before loading its slice.
            if (!page._domain || !domainTotal(page, page._domain)) {
                var domains = vocab().domains;
                page._domain = domains.filter(function (d) { return domainTotal(page, d); })[0] || domains[0];
            }
            return fetchResolved().then(function () {
                // A shared link (cgview in the URL) overrides the default tab and the
                // per-browser filters, once: it is stripped from the address bar on read.
                var shared = consumeUrlView();
                var render;
                if (shared) {
                    render = applyView(page, shared);
                } else {
                    page._pattern = pickPattern(page, page._domain);
                    render = ensureSlice(page, page._pattern, page._domain, loadId).then(function () {
                        if (page._loadSeq !== loadId) { throw { stale: true }; }
                        applyAndRender(page);
                    });
                }
                return render.then(function () {
                    if (page._loadSeq !== loadId) { throw { stale: true }; }
                    checkStale(page, summary);
                    Dashboard.hideLoadingMsg();
                    // A deep-link (cgdiag in the URL, e.g. from an exported audit) opens that
                    // gap's diagnosis straight away, optionally running the deeper pass.
                    var diag = consumeUrlDiag();
                    if (diag) { openDiagnose(diag.id, '', diag.deep); }
                });
            });
        })
        .catch(function (error) {
            if ((error && error.stale) || page._loadSeq !== loadId) { return; }
            // The report is the first thing every admin hits; if it cannot load (the summary,
            // the resolutions, or the first tab's slice), surface it rather than spin forever
            // behind the loading overlay.
            Dashboard.hideLoadingMsg();
            page.querySelector('#cgSummary').textContent =
                'Could not load the report. Check the server logs, then reload.';
        });
}

// Load the gap-resolution map (gaps marked not-really-missing). Best-effort: the returned
// promise always resolves with resolvedMap set (empty on failure), so the caller can chain
// .then() to decide what to do next (initial render, or re-render after a resolve/clear).
function fetchResolved() {
    return ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('MindTheGaps/Resolutions'), dataType: 'json' })
        .then(function (res) { resolvedMap = res || {}; }, function () { resolvedMap = {}; });
}

// Nudge for a rescan when the saved report was built by a different plugin version (after
// an upgrade the persisted links/fields may be stale until rebuilt).
function checkStale(page, report) {
    var stale = page.querySelector('#cgStale');
    var rescanBar = page.querySelector('#cgRescanBar');
    stale.style.display = 'none';
    rescanBar.style.display = '';
    var generated = report && report.GeneratedUtc && report.GeneratedUtc.indexOf('0001') !== 0;
    if (!generated) { return; }
    ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('Plugins'), dataType: 'json' }).then(function (plugins) {
        var norm = function (s) { return (s || '').replace(/-/g, '').toLowerCase(); };
        var me = (plugins || []).filter(function (p) { return norm(p.Id) === norm(pluginId); })[0];
        var cur = me && me.Version;
        if (cur && report.GeneratedVersion !== cur) {
            var built = report.GeneratedVersion ? ('version ' + report.GeneratedVersion) : 'an older version';
            page.querySelector('#cgStaleMsg').textContent =
                'This list was built by ' + built + '. You are on ' + cur + '. Rescan to rebuild it with the current version.';
            stale.style.display = 'flex';
            // The banner carries its own Rescan, so hide the standalone one to avoid two
            // identical buttons stacked together.
            rescanBar.style.display = 'none';
        }
    }).catch(function () { /* version check is best-effort */ });
}

// Show the installed plugin version next to the settings gear (best-effort).
function showVersion(page) {
    var el = page.querySelector('#cgVersion');
    if (!el) { return; }
    ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('Plugins'), dataType: 'json' }).then(function (plugins) {
        var norm = function (s) { return (s || '').replace(/-/g, '').toLowerCase(); };
        var me = (plugins || []).filter(function (p) { return norm(p.Id) === norm(pluginId); })[0];
        if (me && me.Version) { el.textContent = 'v' + me.Version; }
    }).catch(function () { /* best-effort */ });
}

// True while this dashboard page is still the active, attached one. Jellyfin keeps page
// elements around and marks inactive ones with the 'hide' class, so a poll loop started here
// must stop (and stop alerting) once the user has navigated elsewhere.
function pageActive(page) {
    return !!page && document.body.contains(page) && !page.classList.contains('hide');
}

// Reflect the where-to-watch backlog on the toolbar button from the summary: how many titles
// still need a lookup, or that the backlog is cleared, or that availability is off in settings.
// Also the one place that shows or hides the monetization filter panel: it has nothing to filter
// (no offers exist yet) unless availability is actually turned on, so it stays hidden until then
// rather than always showing five checkboxes and an empty provider list.
function updateAvailButton(page) {
    var btn = page.querySelector('#cgLookupAvail');
    if (!btn) { return; }
    var span = btn.querySelector('span');
    var s = page._summary || {};
    var panel = page.querySelector('#cgAvailPanel');
    if (panel) { panel.style.display = s.AvailabilityEnabled ? '' : 'none'; }
    if (!s.AvailabilityEnabled) {
        if (span) { span.textContent = 'Look up where to watch'; }
        btn.disabled = true;
        btn.title = 'Availability is turned off in settings.';
        return;
    }
    var pending = s.AvailabilityPending || 0;
    if (pending > 0) {
        if (span) { span.textContent = 'Look up where to watch (' + pending + ')'; }
        btn.disabled = false;
        btn.title = pending + ' title' + (pending === 1 ? '' : 's') + ' still need a where-to-watch lookup. Runs in the background; results fill in as it goes.';
    } else {
        if (span) { span.textContent = 'Where to watch: all checked'; }
        btn.disabled = true;
        btn.title = 'Every watchable gap has been looked up. Rescan to find new ones.';
    }
}

// Kick off the background "where to watch" pass and poll until it finishes, then reload so
// newly-enriched rows appear. The pass saves incrementally, so a reload mid-run shows partial
// results too. Shared by the toolbar button and the empty-state nudge.
function startAvailability(page, btn) {
    var span = btn ? btn.querySelector('span') : null;
    var orig = span ? span.textContent : null;
    if (btn) { btn.disabled = true; }

    function done(msg) {
        if (btn) { btn.disabled = false; if (span && orig) { span.textContent = orig; } }
        if (msg) { Dashboard.alert(msg); }
    }

    function poll() {
        if (!pageActive(page)) { return; }
        ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('MindTheGaps/Availability/Status'), dataType: 'json' })
            .then(function (s) {
                if (s && s.Running) {
                    if (span) {
                        span.textContent = s.Total
                            ? 'Looking up… ' + (s.Processed || 0) + '/' + s.Total
                            : 'Looking up… ' + Math.round(s.Progress || 0) + '%';
                    }
                    setTimeout(poll, 2000);
                } else {
                    done();
                    load(page);
                    if (s && s.Message) { Dashboard.alert(s.Message); }
                }
            })
            .catch(function () { done('Lost contact during the look-up. Refresh to see results.'); });
    }

    ApiClient.ajax({ type: 'POST', url: ApiClient.getUrl('MindTheGaps/Availability/Enrich'), dataType: 'json' })
        .then(function () { setTimeout(poll, 1000); })
        .catch(function () { done('Could not start the look-up. Check the server logs.'); });
}

// Start a background scan and poll to completion, showing progress on whichever button started
// it. Shared by the toolbar "Rescan now" and the stale-banner "Rescan now" (the latter's bar is
// hidden while the banner shows, so it must drive its own button, not the toolbar's).
function startScan(page, btn) {
    var span = btn ? btn.querySelector('span') : null;
    var orig = span ? span.textContent : null;
    if (btn) { btn.disabled = true; }
    if (span) { span.textContent = 'Scanning…'; }

    function finish(msg) {
        if (btn) { btn.disabled = false; }
        if (span && orig != null) { span.textContent = orig; }
        if (msg) { Dashboard.alert(msg); }
    }

    function poll() {
        if (!pageActive(page)) { return; }
        ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('MindTheGaps/ScanStatus'), dataType: 'json' })
            .then(function (s) {
                if (s && s.Running) {
                    if (span) { span.textContent = 'Scanning… ' + Math.round(s.Progress || 0) + '%'; }
                    setTimeout(poll, 2000);
                } else {
                    finish();
                    load(page);
                }
            })
            .catch(function () { finish('Lost contact while scanning. Refresh to see results.'); });
    }

    ApiClient.ajax({ type: 'POST', url: ApiClient.getUrl('MindTheGaps/Scan'), dataType: 'json' })
        .then(function () { setTimeout(poll, 1000); })
        .catch(function () { finish('Could not start the scan. Check the server logs.'); });
}

