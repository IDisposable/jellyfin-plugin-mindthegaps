// Report page, part 6: the A-Z letter bar and rollup line, the Maintenance actions (verify,
// clear-down, and bulk re-check), and the multi-select bar's Send/Request/Resolve actions.

// The A-Z selector: one entry per letter present, plus a leading "*" for all. Clicking a letter
// renders only that letter's entities (so a huge tab does not render at once); "*" renders the
// lot. Hidden when there is only one letter (nothing to choose).
function renderLetterBar(page, letters, sel, counts, total) {
    var bar = page.querySelector('#cgJump');
    if (letters.length < 2) { bar.innerHTML = ''; bar.style.display = 'none'; return; }
    var gapWord = function (n) { return n + (n === 1 ? ' gap' : ' gaps'); };
    var html = h('a', { 'class': 'cgJumpL cgJumpAll' + (sel === '*' ? ' cgJumpSel' : ''), 'data-l': '*', title: 'Show all letters (' + gapWord(total) + ')' }, '*').outerHTML;
    html += letters.map(function (L) {
        return h('a', { 'class': 'cgJumpL' + (sel === L ? ' cgJumpSel' : ''), 'data-l': L, title: gapWord((counts && counts[L]) || 0) }, L).outerHTML;
    }).join('');
    bar.innerHTML = html;
    bar.style.display = 'flex';
}

// A per-domain summary line for the current tab: gap and group counts, plus an owned-of-total
// coverage aggregate where sets carry counts (collections and series).
function rollupHtml(items) {
    if (!items.length) { return ''; }
    var noun = page_pattern_noun();
    var byCat = groupBy(items, categoryOf);
    byCat.order.sort(domainCompare());
    var parts = byCat.order.map(function (cat) {
        var catItems = byCat.map[cat];
        var groups = {}, ownedSum = 0, totalSum = 0;
        catItems.forEach(function (it) {
            var key = (it.SourceItemId || '') + '|' + (it.SourceItemName || '');
            if (!groups[key]) {
                groups[key] = true;
                if (it.SetTotalCount) { ownedSum += (it.SetOwnedCount || 0); totalSum += it.SetTotalCount; }
            }
        });
        var nGroups = Object.keys(groups).length;
        var cov = totalSum ? ' ' + h('span', { 'class': 'cgRollupCov' }, '(' + ownedSum + ' of ' + totalSum + ' owned, ' + Math.round(ownedSum / totalSum * 100) + '%)').outerHTML : '';
        var clear = clearBtn('domain', cat, 'everything shown for ' + cat);
        return h('b', null, cat).outerHTML + ': ' + catItems.length + ' gaps across ' + nGroups + ' ' + noun + (nGroups === 1 ? '' : 's') + cov + clear;
    });
    return parts.join(' &nbsp;&middot;&nbsp; ');
}

function page_pattern_noun() {
    var p = (reportPage() || {})._pattern;
    if (p === 'CreatorWorks') { return 'creator'; }
    if (p === 'Recommendation') { return 'source'; }
    return 'set';
}

// ---- Maintenance actions ----

// Poll the mint/remove background operation and report its result.
function pollRemoval() {
    ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('MindTheGaps/MintStatus'), dataType: 'json' })
        .then(function (s) {
            if (s && s.Running) { setTimeout(pollRemoval, 1500); }
            else { Dashboard.hideLoadingMsg(); Dashboard.alert((s && s.Message) || 'Done.'); }
        }, function () {
            Dashboard.hideLoadingMsg();
            Dashboard.alert('Lost contact while working. Check the server logs.');
        });
}

function runRemoval(path, confirmMsg) {
    if (confirmMsg && !window.confirm(confirmMsg)) { return; }
    Dashboard.showLoadingMsg();
    ApiClient.ajax({ type: 'POST', url: ApiClient.getUrl('MindTheGaps/' + path), dataType: 'json' })
        .then(function () { pollRemoval(); }, function () {
            Dashboard.hideLoadingMsg();
            Dashboard.alert('Action failed. Check the server logs.');
        });
}

// Best-effort fetch of which acquisition targets are configured; re-renders the report so the
// Send buttons appear/disappear to match. A failure just leaves no Send buttons.
function refreshAcqConfig(page) {
    ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('MindTheGaps/AcquisitionConfig'), dataType: 'json' })
        .then(function (c) {
            acqConfig = c || null;
            if (page && page._report) { applyAndRender(page); }
        }, function () { acqConfig = null; });
}

// The multi-select bar's Send/Request: every checked row's gap, rehydrated server-side by id
// (never shipped from the client), through the same bulk endpoints a per-row Send already calls
// one at a time. SendToArrBulk dispatches each gap to Radarr or Sonarr by its own kind, so one
// button covers a selection mixing movies and series.
function sendSelectedBulk(page, btn, endpoint, verb) {
    var ids = selectedGapIds(page);
    if (!ids.length) { return; }
    if (!window.confirm(verb + ' ' + ids.length + ' selected item(s)?')) { return; }
    var html = btn.innerHTML;
    btn.disabled = true;
    btn.textContent = verb + '…';
    ApiClient.ajax({
        type: 'POST',
        url: ApiClient.getUrl('MindTheGaps/' + endpoint),
        contentType: 'application/json',
        data: JSON.stringify(ids),
        dataType: 'json'
    }).then(function (r) {
        btn.innerHTML = html;
        btn.disabled = false;
        Dashboard.alert((r && r.Message) ? String(r.Message) : 'Done.');
    }).catch(function () {
        btn.innerHTML = html;
        btn.disabled = false;
        Dashboard.alert((verb === 'Request' ? 'Request' : 'Send') + ' failed. Check the server logs.');
    });
}

// The multi-select bar's Resolve: one note applies to every checked row at once (ResolveBatch),
// the same "ask once" shape the per-group batch-resolve button already uses.
function resolveSelected(page, btn) {
    var ids = selectedGapIds(page);
    if (!ids.length) { return; }
    var note = window.prompt('Resolve ' + ids.length + ' selected item(s) (not really missing).\nOptional note (e.g. why):', '');
    if (note === null) { return; }
    var html = btn.innerHTML;
    btn.disabled = true;
    ApiClient.ajax({
        type: 'POST',
        url: ApiClient.getUrl('MindTheGaps/ResolveBatch'),
        contentType: 'application/json',
        data: JSON.stringify({ Ids: ids, Kind: null, Note: note })
    }).then(function () {
        btn.innerHTML = html;
        btn.disabled = false;
        fetchResolved().then(function () { applyAndRender(page); });
    }).catch(function () {
        btn.innerHTML = html;
        btn.disabled = false;
        Dashboard.alert('Could not resolve those items. Check the server logs.');
    });
}

// The rows one clear-down click covers, taken from what the last render actually showed rather than
// from the DOM: a collapsed Creator works or Discover group has no rows rendered yet, so reading the
// DOM would silently verify nothing.
function rowsInScope(page, scope, key) {
    var shown = page._shown || [];
    if (scope === 'row') { return shown.filter(function (it) { return it.Id === key; }); }
    if (scope === 'domain') { return shown.filter(function (it) { return categoryOf(it) === key; }); }
    if (scope === 'kind') { return shown.filter(function (it) { return kindLabelOf(it) === key; }); }
    // Groups are rendered by SourceItemName, so the scope keys on that too. Keying on one member's
    // SourceItemId would cover only one of two same-named creators or collections whose rows share a
    // heading, silently leaving the other's rows behind.
    if (scope === 'group') { return shown.filter(function (it) { return groupKeyOf(it) === key; }); }
    if (scope === 'season') {
        // "<group name>|<season groupBy key>", where an episode with no season files under 'na',
        // matching how sourceBody groups them. Split on the last separator, since a name may contain one.
        var cut = key.lastIndexOf('|');
        var owner = key.slice(0, cut);
        var season = key.slice(cut + 1);
        return shown.filter(function (it) {
            return groupKeyOf(it) === owner && (it.Season == null ? 'na' : String(it.Season)) === season;
        });
    }

    return [];
}

// The heading a row is rendered under, matching what buildTree and setSourceCell group by.
function groupKeyOf(it) { return it.SourceItemName || '(no source)'; }

// How a clear-down names its scope in the messages it shows.
function scopeLabel(scope, key, items) {
    if (scope === 'row') { return ' (' + ((items[0] && items[0].Name) || 'this title') + ')'; }
    if (scope === 'domain' || scope === 'kind') { return ' under ' + key; }
    if (scope === 'season') { return ' in this season'; }
    return ' in this group';
}

// Ask the server which of these gaps the library now holds; it drops those from the report and
// returns their ids, which are pruned from every cached tab so a tab switch does not resurrect them.
function verifyGaps(page, ids) {
    return ApiClient.ajax({
        type: 'POST',
        url: ApiClient.getUrl('MindTheGaps/Verify'),
        contentType: 'application/json',
        data: JSON.stringify(ids)
    }).then(function (res) {
        var removed = {};
        ((res && res.RemovedIds) || []).forEach(function (id) { removed[id] = true; });
        if (!Object.keys(removed).length) { return res; }

        pruneSlices(page, removed);

        // Pruning can only adjust the counts for gaps we hold locally, and the whole point of the sweep is
        // that it also clears rows on tabs never loaded. Re-read the summary so the totals and tab badges
        // are the server's rather than a local approximation; failing that, keep the pruned estimate.
        return ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('MindTheGaps/Summary'), dataType: 'json' })
            .then(function (summary) {
                if (summary) { page._summary = summary; }
                return res;
            }, function () { return res; });
    });
}

// Drop removed gaps from the loaded tab and every cached slice, so the list reflects the change without
// re-fetching the report. The counts are not adjusted here: verifyGaps re-reads the summary, which is the
// only thing that knows about rows removed from tabs this browser never loaded. The loaded tab is the
// same object as its cached slice, so pruned reports are tracked to avoid filtering one twice.
function pruneSlices(page, removed) {
    var seen = [];
    var prune = function (report) {
        if (!report || !report.Items || seen.indexOf(report) !== -1) { return; }
        seen.push(report);
        report.Items = report.Items.filter(function (it) { return !removed[it.Id]; });
    };

    prune(page._report);
    Object.keys(page._slices || {}).forEach(function (p) { prune(page._slices[p]); });
}

// The distinct owning items behind a set of rows that the server can actually re-run, in the order
// they appear. A row whose owner has no per-item re-check contributes nothing, so the clear-down
// never prompts for a pass the server would skip.
function recheckableOwners(items) {
    var seen = {};
    var owners = [];
    items.forEach(function (it) {
        var src = it.SourceItemId || '';
        if (!ownerRecheckable(it) || seen[src]) { return; }
        seen[src] = true;
        owners.push(src);
    });
    return owners;
}

// The clear-down every scope runs, from a single row up to a whole tab: verify the rows in scope,
// drop what the library now holds, then offer to re-check the sources behind whatever survived. One
// routine, so every level behaves identically at a different width.
function clearDownScope(page, items, label) {
    if (!items.length) { Dashboard.alert('Nothing shown to check.'); return Promise.resolve(); }
    return verifyGaps(page, items.map(function (it) { return it.Id; })).then(function (res) {
        var cleared = (res && res.Owned) || 0;
        var left = items.length - cleared;
        // The server drops every gap about a title it confirms you own, so acquiring one film can clear
        // rows on tabs that are not even loaded (its collection, a studio set, a filmography). Say so,
        // rather than have the totals move by more than the rows that visibly went.
        var elsewhere = Math.max(0, ((res && res.Removed) || cleared) - cleared);
        var also = elsewhere ? ' (and ' + elsewhere + ' more elsewhere in the report)' : '';
        if (cleared) { applyAndRender(page); }
        if (!left) {
            Dashboard.alert('Cleared all ' + cleared + ' item(s)' + label + also + '; you have them all now.');
            return;
        }
        // Re-check only the sources that still have something missing, not every source in scope, so a
        // mostly-clear heading costs a handful of provider calls instead of one per set.
        var removed = (res && res.RemovedIds) || [];
        var owners = recheckableOwners(items.filter(function (it) { return removed.indexOf(it.Id) === -1; }));
        var msg = cleared
            ? 'Cleared ' + cleared + ' item(s)' + label + also + '. ' + left + ' still missing.'
            : left + ' item(s)' + label + ' still missing.';
        if (!owners.length) { Dashboard.alert(msg); return; }
        if (!window.confirm(msg + '\n\nRe-check the ' + owners.length + ' source(s) they belong to with their providers? That runs in the background and also picks up anything added since the last scan.')) { return; }
        return startBulkRecheck(page, owners);
    });
}

// Stage two of a kind-level clear-down: hand the whole batch to the background runner and poll it,
// since a heading can cover hundreds of sets and each one is a live provider call. The server builds
// the ownership index once for the batch and swaps each set in as it finishes, so a run that is
// interrupted still leaves the sets it got through up to date.
function startBulkRecheck(page, ownerIds) {
    return ApiClient.ajax({
        type: 'POST',
        url: ApiClient.getUrl('MindTheGaps/RecheckSources'),
        contentType: 'application/json',
        data: JSON.stringify(ownerIds)
    }).then(function (st) {
        if (st && !st.Started && st.Running) {
            Dashboard.alert('A re-check is already running; wait for it to finish and try again.');
            return;
        }
        Dashboard.showLoadingMsg();
        return pollBulkRecheck(page);
    }).catch(function () {
        Dashboard.alert('Could not start the re-check. Check the server logs.');
    });
}

function pollBulkRecheck(page) {
    return ApiClient.getJSON(ApiClient.getUrl('MindTheGaps/RecheckStatus')).then(function (st) {
        if (st && st.Running) {
            return new Promise(function (resolve) { setTimeout(resolve, 1000); }).then(function () { return pollBulkRecheck(page); });
        }
        // Finished: the report changed underneath us, so drop this domain's cached slices (plain and
        // pattern-scoped alike) and re-fetch the pattern in view.
        invalidateDomainSlices(page, page._domain);
        return ensureSlice(page, page._pattern, page._domain).then(function () {
            Dashboard.hideLoadingMsg();
            applyAndRender(page);
            Dashboard.alert('Re-check finished' + (st && st.Total ? ' (' + st.Done + ' of ' + st.Total + ' set(s))' : '') + '.');
        });
    }).catch(function () {
        Dashboard.hideLoadingMsg();
        Dashboard.alert('Lost contact with the re-check. Check the server logs.');
    });
}

