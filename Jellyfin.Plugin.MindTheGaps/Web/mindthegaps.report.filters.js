// Report page, part 4: the shared row filter, the domain tabs, and the Type (pattern) selector.

// The filters shared by the tab counts and the list, all of them except the pattern itself,
// so a tab's badge shows how many gaps would appear if you opened it under the current filters.
function buildFilter(page) {
    var type = page._domain;
    var term = (page.querySelector('#cgSearch').value || '').toLowerCase();
    var hideSpecials = page.querySelector('#cgHideSpecials').checked;
    var hideUpcoming = page.querySelector('#cgHideUpcoming').checked;
    var showResolved = page.querySelector('#cgShowResolved').checked;
    var streamable = page.querySelector('#cgStreamable').checked;
    return function (it) {
        if (activeDismissal(it) && !showResolved) { return false; }
        if (it.PatternName === 'CreatorWorks' && creatorDismissed(it.SourceItemId) && !showResolved) { return false; }
        if (it.PatternName === 'Recommendation' && effectiveRecSourceCount(it) === 0 && !showResolved) { return false; }
        if (type && categoryOf(it) !== type) { return false; }
        if (hideSpecials && it.Season != null && it.Season <= 0) { return false; }
        if (hideUpcoming && it.IsUpcoming) { return false; }
        // Hide "no sources" only once a title has actually been looked up. An un-checked gap is
        // "unknown", not "no sources", so keep it visible (with its Where-to-watch button)
        // rather than vanishing the whole un-enriched list behind this filter.
        if (streamable && it.AvailabilityChecked && !filterOffers(it.Availability).length) { return false; }
        // Match the title and the owning source (creator or recommending title), so searching a
        // person's name finds their filmography rows even though the row name is the missing film.
        if (term) {
            var haystack = ((it.Name || '') + ' ' + (it.SourceItemName || '')).toLowerCase();
            if (haystack.indexOf(term) === -1) { return false; }
        }
        return true;
    };
}

function renderTabs(page) {
    // Every domain the server says is covered (summary.Domains) gets a tab, in the server's display
    // order, even one with zero entries this scan (Music when nothing music is owned) - a tab is
    // never missing just because this scan found nothing for it. The per-domain totals come from the
    // summary, so an inactive tab shows a count without its items being loaded, summed across patterns.
    var domains = vocab().domains;
    // Stay on a domain that has any gaps at all, so toggling a filter down to zero does not yank
    // you to another tab; only fall back when the current domain is truly empty.
    if (!page._domain || !domainTotal(page, page._domain)) {
        page._domain = domains.filter(function (d) { return domainTotal(page, d); })[0] || domains[0];
    }
    page.querySelector('#cgTabs').innerHTML = domains.map(function (d) {
        var active = d === page._domain ? ' cgActive' : '';
        return h('button', {
            type: 'button', is: 'emby-button', 'class': 'raised cgTab' + active, 'data-domain': d
        }, d + ' (' + domainTotal(page, d) + ')').outerHTML;
    }).join('');
}

// Per-pattern gap counts for a domain, straight from the summary (no items downloaded), so a pattern
// to load can be picked before the network call that loads it rather than after.
function patternCountsFor(page, domain) {
    return (page._summary && page._summary.DomainPatternCounts && page._summary.DomainPatternCounts[domain]) || {};
}

// The raw total for a domain across every pattern, from the same summary counts, so a domain tab's
// badge and its "does this tab have anything at all" check need no items downloaded either.
function domainTotal(page, domain) {
    var counts = patternCountsFor(page, domain);
    return Object.keys(counts).reduce(function (sum, k) { return sum + counts[k]; }, 0);
}

// Decides which pattern a domain should load, without needing that domain's items downloaded first
// (each Gaps request is narrowed to one pattern now, so guessing wrong would mean a second fetch): an
// explicit ask (a restored view or a shared link) wins once, then the pattern last shown for this
// domain in this session, then the first pattern in tab order the summary says has anything in it.
// Records the choice, so renderTypeFilter (which only reads the record) shows what was actually fetched.
function pickPattern(page, domain) {
    page._patternByDomain = page._patternByDomain || {};
    var patterns = vocab().patterns;
    var chosen;
    if (page._wantPattern && patterns.indexOf(page._wantPattern) !== -1) {
        chosen = page._wantPattern;
        page._wantPattern = '';
    } else if (page._patternByDomain[domain] && patterns.indexOf(page._patternByDomain[domain]) !== -1) {
        chosen = page._patternByDomain[domain];
    } else {
        var counts = patternCountsFor(page, domain);
        chosen = patterns.filter(function (p) { return counts[p]; })[0] || patterns[0];
    }

    page._patternByDomain[domain] = chosen;
    return chosen;
}

// Build the "Pattern:" selector (Set completion / Creator works / Discover), worded for the active
// domain via patternLabel (e.g. "Series completion" under Shows) - the secondary axis now that
// domain is the primary tab.
function renderTypeFilter(page) {
    var wrap = page.querySelector('#cgTypeFilterWrap');
    var sel = page.querySelector('#cgTypeFilter');
    var patterns = vocab().patterns;
    var counts = patternCountsFor(page, page._domain);
    if (wrap) { wrap.style.display = 'inline-flex'; }
    sel.innerHTML = patterns.map(function (p) {
        return h('option', { value: p }, patternLabel(p, page._domain) + ' (' + (counts[p] || 0) + ')').outerHTML;
    }).join('');
    sel.value = page._pattern || patterns[0];
}

// The element that actually scrolls the report (or the window), cached on the page.
function scrollerFor(page) {
    if (page._scroller) { return page._scroller; }
    var s = document.scrollingElement || document.documentElement;
    for (var n = page.querySelector('#cgList'); n && n !== document.body; n = n.parentElement) {
        var oy = getComputedStyle(n).overflowY;
        if (oy === 'auto' || oy === 'scroll') { s = n; break; }
    }
    page._scroller = s;
    return s;
}

// A stable key for a group: the chain of its and its ancestors' labels, so its collapsed state
// can be matched back to the same group after a re-render.
function groupKey(el) {
    var parts = [];
    for (var n = el; n; n = n.parentElement ? n.parentElement.closest('.cgGroup') : null) {
        parts.unshift(n.getAttribute('data-cglabel') || '');
    }
    return parts.join('');
}

// Point each group header's aria-expanded at its current collapse state, after a render or a
// bulk class change that did not set it inline.
function syncGroupAria(listEl) {
    var hdrs = listEl.querySelectorAll('.cgHdr');
    for (var i = 0; i < hdrs.length; i++) {
        var g = hdrs[i].parentElement;
        hdrs[i].setAttribute('aria-expanded', g && g.classList.contains('cgCollapsed') ? 'false' : 'true');
    }
}

