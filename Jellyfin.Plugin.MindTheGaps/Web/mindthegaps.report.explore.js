// Report page, part 8: "Explore a source" - an ad-hoc, unsaved run of one curated-kind source
// against explicit ids, and the modal that drives it.

// Explore one source ad-hoc: find the unowned items from a curated kind (studio, keyword,
// tmdblist, label, mdblist) for the picked ids and merge them into the report as "explored"
// gaps, without saving the ids to config or running a full scan. Mirrors startScan: POST to
// start, poll Explore/Status until it stops, then reload the report. The button shows progress
// and is disabled for the run. Started=false (a scan or another explore is already running) is
// surfaced as a graceful message rather than a stuck spinner. onDone, if given, runs once the
// exploration finishes successfully (the modal uses it to close itself).
function startExplore(page, kind, ids, btn, onDone) {
    if (!kind || !ids) { return; }
    var span = btn ? btn.querySelector('span') : null;
    var orig = span ? span.textContent : null;
    if (btn) { btn.disabled = true; }
    if (span) { span.textContent = 'Exploring…'; }

    function finish(msg) {
        if (btn) { btn.disabled = false; }
        if (span && orig != null) { span.textContent = orig; }
        if (msg) { Dashboard.alert(msg); }
    }

    function poll() {
        if (!pageActive(page)) { return; }
        ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('MindTheGaps/Explore/Status'), dataType: 'json' })
            .then(function (s) {
                if (s && s.Running) {
                    if (span) { span.textContent = 'Exploring… ' + Math.round(s.Progress || 0) + '%'; }
                    setTimeout(poll, 2000);
                } else {
                    finish();
                    load(page);
                    if (onDone) { onDone(); }
                    Dashboard.alert('Exploration finished. New gaps were merged into the report.');
                }
            })
            .catch(function () { finish('Lost contact while exploring. Refresh to see results.'); });
    }

    ApiClient.ajax({ type: 'POST', url: ApiClient.getUrl('MindTheGaps/Explore', { kind: kind, ids: ids }), dataType: 'json' })
        .then(function (s) {
            // The backend declines (Started=false) when a scan or another explore is already
            // running. Do not poll in that case; tell the user to wait for the running one.
            if (s && s.Started === false) {
                finish('A scan or another exploration is already running. Wait for it to finish, then try again.');
                return;
            }
            setTimeout(poll, 1000);
        })
        .catch(function () { finish('Could not start the exploration. Check the server logs.'); });
}

// Clear ad-hoc explorations from the report (all of them, no source filter), then reload and
// report how many gaps were removed.
function clearExplorations(page, btn) {
    var span = btn ? btn.querySelector('span') : null;
    var orig = span ? span.textContent : null;
    if (btn) { btn.disabled = true; }
    if (span) { span.textContent = 'Clearing…'; }

    function finish(msg) {
        if (btn) { btn.disabled = false; }
        if (span && orig != null) { span.textContent = orig; }
        if (msg) { Dashboard.alert(msg); }
    }

    ApiClient.ajax({ type: 'POST', url: ApiClient.getUrl('MindTheGaps/Explore/Clear'), dataType: 'json' })
        .then(function (removed) {
            var n = parseInt(removed, 10);
            if (isNaN(n)) { n = 0; }
            finish();
            load(page);
            Dashboard.alert(n === 1 ? '1 explored gap removed.' : n + ' explored gaps removed.');
        })
        .catch(function () { finish('Could not clear the explorations. Check the server logs.'); });
}

// ---- Explore a source modal ----
// A report-page popup to explore one curated source ad-hoc, so the user never touches the
// settings form to do it. The kind selector drives the source picker: a name-searchable kind
// (studio, keyword, label, mdblist) shows a chip picker that queries CuratedSearch as the user
// types; tmdblist shows a plain comma-separated id input, since TMDB has no list name search.
// Run posts to Explore and reuses startExplore's poll-and-reload flow; Clear reuses
// clearExplorations. State lives on the modal element so the picker survives reopening.

// Whether a kind is entered by raw id (no chip picker). Driven by the server's kinds list once
// loaded; falls back to the known raw kind (a TMDB list) until then.
function isRawKind(kind) {
    var modal = document.getElementById('cgExploreModal');
    if (modal && modal._kinds && Object.prototype.hasOwnProperty.call(modal._kinds, kind)) {
        return !modal._kinds[kind];
    }
    return kind === 'tmdblist';
}

// The picked numeric ids for the modal's current kind: the chip ids for a searchable kind, or
// the raw comma-separated input (trimmed) for a raw-id kind.
function exploreIds(page) {
    var kind = page.querySelector('#cgExploreKind').value;
    if (isRawKind(kind)) {
        var raw = page.querySelector('#cgExploreRaw');
        return raw ? (raw.value || '').trim() : '';
    }
    var modal = document.getElementById('cgExploreModal');
    var chips = (modal && modal._chips) || [];
    return chips.map(function (c) { return c.Id; }).join(',');
}

// Show the source picker that fits the chosen kind (the chip search or the raw-id input) and
// reset the picked chips, since ids do not carry across kinds.
function syncExploreKind(page) {
    var kind = page.querySelector('#cgExploreKind').value;
    var raw = isRawKind(kind);
    page.querySelector('#cgExploreSearchWrap').style.display = raw ? 'none' : '';
    page.querySelector('#cgExploreRawWrap').style.display = raw ? '' : 'none';
    var modal = document.getElementById('cgExploreModal');
    if (modal) { modal._chips = []; }
    renderExploreChips(page);
    var input = page.querySelector('#cgExploreSearch');
    if (input) { input.value = ''; }
    closeExploreSuggest(page);
}

// Render the picked-source chips (a removable list, same look as the settings chips).
function renderExploreChips(page) {
    var modal = document.getElementById('cgExploreModal');
    var chips = (modal && modal._chips) || [];
    page.querySelector('#cgExploreChips').innerHTML = chips.map(function (c, i) {
        var x = wrap('button', {
            type: 'button', 'class': 'cgChipX', 'data-i': i,
            'aria-label': 'Remove ' + (c.Name || ''), title: 'Remove ' + (c.Name || '')
        }, '&times;');
        return wrap('span', { 'class': 'cgChip', role: 'listitem' }, esc(c.Name) + x);
    }).join('');
}

function closeExploreSuggest(page) {
    var suggest = page.querySelector('#cgExploreSuggest');
    var input = page.querySelector('#cgExploreSearch');
    suggest.classList.remove('cgShown'); suggest.innerHTML = '';
    var modal = document.getElementById('cgExploreModal');
    if (modal) { modal._items = []; modal._sel = -1; }
    input.setAttribute('aria-expanded', 'false'); input.removeAttribute('aria-activedescendant');
}

// Wire the modal's source picker once: the chip search (type-ahead over CuratedSearch), the
// kind selector, the close paths, Run, and Clear. Called once per page bind.
function setupExploreModal(page) {
    var modal = document.getElementById('cgExploreModal');
    modal._chips = [];
    modal._items = [];
    modal._sel = -1;
    modal._seq = 0;
    modal._timer = 0;
    modal._kinds = null;
    // Populate the kind dropdown from the server's registered explore kinds, so a new source's
    // kind appears without editing this page, and remember which kinds are searchable (a chip
    // picker) versus raw-id entry. The static options stay as a fallback if this does not load.
    var kindSelect = page.querySelector('#cgExploreKind');
    ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('MindTheGaps/Explore/Kinds'), dataType: 'json' }).then(function (kinds) {
        if (!kinds || !kinds.length) { return; }
        var map = {};
        kindSelect.innerHTML = '';
        kinds.forEach(function (k) {
            map[k.Kind] = !!k.Searchable;
            var opt = document.createElement('option');
            opt.value = k.Kind;
            opt.textContent = k.Label;
            kindSelect.appendChild(opt);
        });
        modal._kinds = map;
        syncExploreKind(page);
    }, function () { /* keep the static options as a fallback */ });
    var input = page.querySelector('#cgExploreSearch');
    var suggest = page.querySelector('#cgExploreSuggest');

    function announce(msg) { var live = page.querySelector('#cgExploreLive'); if (live) { live.textContent = msg; } }
    function has(id) { return modal._chips.some(function (c) { return c.Id === id; }); }
    function addChip(c) {
        if (c && c.Id && !has(c.Id)) { modal._chips.push({ Id: c.Id, Name: c.Name || String(c.Id) }); renderExploreChips(page); announce('Added ' + (c.Name || c.Id)); }
        input.value = ''; closeExploreSuggest(page);
    }
    function removeAt(i) {
        var removed = modal._chips[i];
        modal._chips.splice(i, 1); renderExploreChips(page);
        if (removed) { announce('Removed ' + removed.Name); }
        input.focus();
    }
    function renderSuggest() {
        suggest.innerHTML = modal._items.length
            ? modal._items.map(function (it, i) {
                return h('div', {
                    'class': 'cgSuggestItem' + (i === modal._sel ? ' cgSuggestSel' : ''), role: 'option',
                    id: 'cgExploreSuggest-opt-' + i, 'aria-selected': i === modal._sel ? 'true' : 'false', 'data-i': i
                }, it.Name).outerHTML;
            }).join('')
            : h('div', { 'class': 'cgSuggestEmpty' }, 'No matches').outerHTML;
        suggest.classList.add('cgShown');
        input.setAttribute('aria-expanded', 'true');
        input.setAttribute('aria-activedescendant', modal._sel >= 0 ? ('cgExploreSuggest-opt-' + modal._sel) : '');
    }
    function search() {
        var q = input.value.trim();
        if (q.length < 2) { closeExploreSuggest(page); return; }
        var kind = page.querySelector('#cgExploreKind').value;
        var mySeq = ++modal._seq;
        ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('MindTheGaps/CuratedSearch', { kind: kind, query: q }), dataType: 'json' }).then(function (res) {
            if (mySeq !== modal._seq) { return; } // a newer keystroke superseded this response
            modal._items = (res || []).filter(function (it) { return !has(it.Id); });
            modal._sel = modal._items.length ? 0 : -1;
            renderSuggest();
        }, function () { closeExploreSuggest(page); });
    }

    input.addEventListener('input', function () { clearTimeout(modal._timer); modal._timer = setTimeout(search, 220); });
    input.addEventListener('keydown', function (e) {
        if (e.key === 'ArrowDown' && modal._items.length) { modal._sel = (modal._sel + 1) % modal._items.length; renderSuggest(); e.preventDefault(); }
        else if (e.key === 'ArrowUp' && modal._items.length) { modal._sel = (modal._sel - 1 + modal._items.length) % modal._items.length; renderSuggest(); e.preventDefault(); }
        else if (e.key === 'Enter') { e.preventDefault(); if (modal._sel >= 0 && modal._items[modal._sel]) { addChip(modal._items[modal._sel]); } }
        else if (e.key === 'Escape') { closeExploreSuggest(page); }
        else if (e.key === 'Backspace' && !input.value && modal._chips.length) { removeAt(modal._chips.length - 1); }
    });
    input.addEventListener('blur', function () { setTimeout(function () { closeExploreSuggest(page); }, 150); });
    suggest.addEventListener('mousedown', function (e) {
        var el = e.target.closest('.cgSuggestItem');
        if (el) { e.preventDefault(); addChip(modal._items[parseInt(el.getAttribute('data-i'), 10)]); }
    });
    page.querySelector('#cgExploreChips').addEventListener('click', function (e) {
        var x = e.target.closest('.cgChipX');
        if (x) { removeAt(parseInt(x.getAttribute('data-i'), 10)); }
    });
    page.querySelector('#cgExploreBox').addEventListener('click', function (e) {
        if (e.target === this || e.target === page.querySelector('#cgExploreChips')) { input.focus(); }
    });

    page.querySelector('#cgExploreKind').addEventListener('change', function () { syncExploreKind(page); });
    page.querySelector('#cgExploreClose').addEventListener('click', function () { closeExplore(page); });
    modal.addEventListener('click', function (e) { if (e.target === this) { closeExplore(page); } });
    page.querySelector('#cgExploreRun').addEventListener('click', function () {
        var kind = page.querySelector('#cgExploreKind').value;
        var ids = exploreIds(page);
        if (!ids) {
            Dashboard.alert(isRawKind(kind) ? 'Type at least one id.' : 'Pick at least one source before running.');
            return;
        }
        // startExplore reloads the report on completion; close the modal and confirm too.
        startExplore(page, kind, ids, this, function () { closeExplore(page); });
    });
    page.querySelector('#cgExploreClear').addEventListener('click', function () {
        if (!window.confirm('Remove every ad-hoc exploration from the report?')) { return; }
        clearExplorations(page, this);
    });
}

function openExplore(page) {
    var modal = document.getElementById('cgExploreModal');
    syncExploreKind(page);
    modal.style.display = 'flex';
    var input = page.querySelector('#cgExploreSearch');
    if (input && input.offsetParent !== null) { input.focus(); }
}

function closeExplore(page) {
    var modal = document.getElementById('cgExploreModal');
    if (modal && modal.style.display !== 'none') {
        modal.style.display = 'none';
        closeExploreSuggest(page);
    }
}

