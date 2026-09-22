// Report page, part 9: the signed-in user's own TODO list, and the admin-only fulfillment queue
// (everyone's TODO list folded into one demand-sorted list for the person actually buying things).

// ---- My TODO list ----
// A personal "go find this" list, separate from the report's dismissals. Add a gap with the
// per-row TODO button or the multi-select bar; the modal lists the saved entries, grouped by
// domain, with a done toggle, search and provider links, Verify (checks the library now), and
// Delete. The server holds the entries (Todo endpoints); the report's gap snapshot is not needed.

// Domain ordering for the TODO sections, mirroring the report's Movies/Shows/Music/Books order,
// then anything else after.
var TODO_DOMAIN_ORDER = ['Movies', 'Shows', 'Music', 'Books'];
function todoDomainRank(name) {
    var i = TODO_DOMAIN_ORDER.indexOf(name);
    return i < 0 ? TODO_DOMAIN_ORDER.length : i;
}

// "name year" for an Amazon / web search, trimmed so a missing year leaves no trailing space.
function todoSearchTerm(entry) {
    return ((entry.Name || '') + ' ' + (entry.Year || '')).trim();
}

// The web-search URL: the configured template with {0} replaced by the encoded
// "name year creator". Empty when no template is set, so the link is dropped.
function todoWebSearchUrl(template, entry) {
    if (!template) { return ''; }
    var term = ((entry.Name || '') + ' ' + (entry.Year || '') + ' ' + (entry.Creator || '')).trim();
    return template.replace('{0}', encodeURIComponent(term));
}

function todoAmazonUrl(entry) {
    return 'https://www.amazon.com/s?k=' + encodeURIComponent(todoSearchTerm(entry));
}

// The "whose list" value that shows every user's list at once (a user id otherwise).
var TODO_EVERYONE = '*';

// Read every user's list (an administrator's view), keep the chosen one in view, and render. The chosen list
// starts as the caller's own and stays put across reloads while that user still has a list.
function loadTodo(modal) {
    return ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('MindTheGaps/Todo/All'), dataType: 'json' })
        .then(function (data) {
            data = data || { Items: [], Owners: [] };
            modal._data = data;
            modal._template = data.SearchUrlTemplate || '';
            var owners = data.Owners || [];
            var known = modal._who === TODO_EVERYONE || owners.some(function (o) { return o.UserId === modal._who; });
            if (!modal._who || !known) { modal._who = data.CallerId; }
            renderTodoWho(modal);
            renderTodo(modal);
        });
}

// Bring each owner's counts in line with the entries held, after one is removed.
function todoRecount(modal) {
    var data = modal._data || {};
    (data.Owners || []).forEach(function (o) {
        var mine = (data.Items || []).filter(function (it) { return it.OwnerId === o.UserId; });
        o.Count = mine.length;
        o.Open = mine.filter(function (it) { return !it.Done; }).length;
    });
}

// The lists in view: the one chosen, or every list that has something on it.
function todoOwnersInView(modal) {
    if (modal._who !== TODO_EVERYONE) { return [modal._who]; }
    return ((modal._data && modal._data.Owners) || []).filter(function (o) { return o.Count > 0; })
        .map(function (o) { return o.UserId; });
}

// The entries in view.
function todoVisible(modal) {
    var items = (modal._data && modal._data.Items) || [];
    if (modal._who === TODO_EVERYONE) { return items; }
    return items.filter(function (it) { return it.OwnerId === modal._who; });
}

// The "whose list" chooser: shown only when more than one user has a list.
function renderTodoWho(modal) {
    var data = modal._data || {};
    var owners = data.Owners || [];
    var row = document.getElementById('cgTodoWhoRow');
    var select = document.getElementById('cgTodoWho');
    row.style.display = owners.length > 1 ? 'flex' : 'none';
    var total = 0;
    var html = owners.map(function (o) {
        total += o.Count;
        return h('option', { value: o.UserId }, (o.UserId === data.CallerId ? 'My list' : o.UserName) + ' (' + o.Count + ')').outerHTML;
    }).join('');
    html += h('option', { value: TODO_EVERYONE }, 'Everyone (' + total + ')').outerHTML;
    select.innerHTML = html;
    select.value = modal._who;
}

// Check the entries in view against the library, one list at a time, tick the ones now held, and re-render
// from a fresh read. Both the "Verify all" button and the export run this, so a list you are about to read
// (on screen or in a file) has just been reconciled with the library.
function verifyAllTodo(modal) {
    var checked = 0;
    var owned = 0;
    return todoOwnersInView(modal).reduce(function (chain, ownerId) {
        return chain.then(function () {
            return ApiClient.ajax({ type: 'POST', url: ApiClient.getUrl('MindTheGaps/Todo/VerifyAll', { userId: ownerId }), dataType: 'json' })
                .then(function (res) {
                    checked += (res && res.Checked) || 0;
                    owned += (res && res.Owned) || 0;
                });
        });
    }, Promise.resolve()).then(function () {
        return loadTodo(modal).then(function () { return { Checked: checked, Owned: owned }; });
    });
}

// POST helper for the single-id Todo endpoints (Remove / SetDone / Verify), all query-string args.
function todoPost(path, args) {
    return ApiClient.ajax({ type: 'POST', url: ApiClient.getUrl('MindTheGaps/' + path, args), dataType: 'json' });
}

// Add ids to the TODO list, then confirm. ids is an array of gap ids.
function todoAdd(ids, btn) {
    if (!ids || !ids.length) { return; }
    var html = btn ? btn.innerHTML : '';
    if (btn) { btn.disabled = true; }
    ApiClient.ajax({
        type: 'POST',
        url: ApiClient.getUrl('MindTheGaps/Todo/Add'),
        contentType: 'application/json',
        data: JSON.stringify(ids),
        dataType: 'json'
    }).then(function (count) {
        if (btn) { btn.innerHTML = html; btn.disabled = false; }
        Dashboard.alert('Added ' + (count == null ? ids.length : count) + ' to your TODO list.');
    }).catch(function () {
        if (btn) { btn.innerHTML = html; btn.disabled = false; }
        Dashboard.alert('Could not add to the TODO list. Check the server logs.');
    });
}

// The source column. A gap's source item is whatever surfaced it, which differs per pattern: the
// series for an episode, the title that recommended it, the creator, the set it completes. The cell
// carries the label the report's section button uses, worded for the entry's domain and trimmed of
// its "works"/"completion" tail. An entry with no snapshotted pattern shows the bare name.
function todoSourceLabel(entry) {
    if (!entry.PatternName) { return ''; }
    return patternLabel(entry.PatternName, entry.DomainName).replace(/\s+(works|completion)$/i, '');
}

// Plain "Label: Name", for the Markdown export.
function todoSourceText(entry) {
    var src = entry.Creator || '';
    var label = src ? todoSourceLabel(entry) : '';
    return label ? label + ': ' + src : src;
}

// The same for a row, with the label dimmed so the name stays the thing you scan for.
function todoSourceCell(entry) {
    var src = entry.Creator || '';
    if (!src) { return ''; }
    var label = todoSourceLabel(entry);
    return (label ? wrap('span', { 'class': 'cgTodoSrcKind' }, esc(label) + ':') + ' ' : '') + esc(src);
}

// A JustWatch search for a movie or show carrying no JustWatch link of its own (the JustWatch plugin
// only links owned items). Empty for any other kind, and for an entry that already has the link.
function todoJustWatchUrl(entry) {
    if (entry.TargetKindName !== 'Movie' && entry.TargetKindName !== 'Series') { return ''; }
    var hasJw = (entry.Links || []).some(function (l) { return /justwatch/i.test((l.Name || '') + ' ' + (l.Url || '')); });
    if (hasJw) { return ''; }
    return 'https://www.justwatch.com/' + jwLocale() + '/search?q=' + encodeURIComponent(entry.Name || '');
}

// One entry's links in render order: the Amazon and web searches, the gap's own provider links, then
// the JustWatch fallback. The row and the Markdown export both build from this. Provider marks a link
// that came from the gap itself; the row gives those the provider-button treatment.
function todoLinks(entry, template) {
    var out = [{ Name: 'Amazon', Url: todoAmazonUrl(entry), Title: 'Search Amazon' }];
    var webUrl = todoWebSearchUrl(template, entry);
    if (webUrl) { out.push({ Name: 'Web search', Url: webUrl, Title: 'Web search' }); }
    (entry.Links || []).forEach(function (l) { if (l && l.Url) { out.push({ Name: l.Name, Url: l.Url, Provider: true }); } });
    var jwUrl = todoJustWatchUrl(entry);
    if (jwUrl) { out.push({ Name: 'JustWatch search', Url: jwUrl, Title: 'Search JustWatch for where to watch' }); }
    return out;
}

// One TODO row: the done checkbox, the title and year, the source, the links cell, and the
// Verify/Delete actions. Built with the same h/wrap/newTab/providerLink helpers as the report.
function todoRowHtml(entry, template, showOwner) {
    var done = !!entry.Done;
    var check = h('input', {
        type: 'checkbox', 'class': 'cgTodoDoneBox', 'data-id': entry.Id, 'data-owner': entry.OwnerId,
        title: 'Mark done', 'aria-label': 'Mark done'
    });
    if (done) { check.setAttribute('checked', 'checked'); }
    var titleMeta = (entry.Name || '') + (entry.Year ? ' (' + entry.Year + ')' : '');
    var ownerNote = showOwner ? wrap('span', { 'class': 'cgTodoOwner' }, esc('On ' + (entry.OwnerName || 'a user') + "'s list")) : '';
    var titleCell = wrap('td', { 'class': 'cgTodoTitle' }, esc(titleMeta) + ownerNote);
    var creatorCell = wrap('td', { 'class': 'cgTodoCreator' }, todoSourceCell(entry));

    var links = todoLinks(entry, template).map(function (l) {
        return l.Provider
            ? providerLink(l)
            : newTab(true, { 'class': 'cgLink', href: l.Url, title: l.Title }, esc(l.Name));
    });
    var note = entry.Done && entry.DoneUtc
        ? wrap('div', { 'class': 'cgTodoNote' }, 'Done')
        : '';
    var linksCell = wrap('td', null, wrap('div', { 'class': 'cgTodoLinks' }, links.join('')) + note);

    var actions = actionBtn('cgTodoVerify', { 'data-id': entry.Id, 'data-owner': entry.OwnerId, title: 'Check your library for this title now' }, 'Verify')
        + actionBtn('cgTodoDelete', { 'data-id': entry.Id, 'data-owner': entry.OwnerId, title: 'Remove from the TODO list' }, 'Delete');
    var actionsCell = wrap('td', { 'class': 'cgTodoActions' }, actions);

    return wrap('tr', { 'class': 'cgTodoRow' + (done ? ' cgTodoDone' : ''), 'data-id': entry.Id, 'data-owner': entry.OwnerId },
        wrap('td', { 'class': 'cgTodoCheck' }, check.outerHTML) + titleCell + creatorCell + linksCell + actionsCell);
}

// Render the loaded TODO list into the modal body, grouped into per-domain sections.
function renderTodo(modal) {
    var body = document.getElementById('cgTodoBody');
    var items = todoVisible(modal);
    var everyone = modal._who === TODO_EVERYONE;
    var owner = ((modal._data && modal._data.Owners) || []).filter(function (o) { return o.UserId === modal._who; })[0];
    var mine = !everyone && (!owner || owner.UserId === (modal._data || {}).CallerId);
    document.getElementById('cgTodoTitle').textContent = everyone ? "Everyone's TODO lists"
        : (mine ? 'My TODO list' : owner.UserName + "'s TODO list");
    if (!items.length) {
        var empty = everyone ? 'Nobody has anything on their TODO list yet.'
            : (mine ? 'Your TODO list is empty. Add gaps with the TODO button on a row or the multi-select bar.'
                : owner.UserName + ' has nothing on their TODO list.');
        body.innerHTML = h('div', { 'class': 'cgTodoEmpty' }, empty).outerHTML;
        return;
    }
    var template = modal._template || '';
    var byDomain = groupBy(items, function (it) { return it.DomainName || 'Other'; });
    byDomain.order.sort(function (a, b) {
        var ra = todoDomainRank(a), rb = todoDomainRank(b);
        return ra !== rb ? ra - rb : ci(a, b);
    });
    var html = '';
    byDomain.order.forEach(function (domain) {
        html += h('div', { 'class': 'cgTodoSection' }, domain).outerHTML;
        var rows = byDomain.map[domain].map(function (e) { return todoRowHtml(e, template, everyone); }).join('');
        html += wrap('table', { 'class': 'cgTodoTable' }, wrap('tbody', null, rows));
    });
    body.innerHTML = html;
}

function openTodo() {
    var modal = document.getElementById('cgTodoModal');
    var body = document.getElementById('cgTodoBody');
    body.innerHTML = h('p', { 'class': 'fieldDescription' }, 'Loading your TODO list...').outerHTML;
    modal.style.display = 'flex';
    modal._who = null;
    loadTodo(modal)
        .catch(function () {
            body.innerHTML = h('p', { 'class': 'fieldDescription' }, 'Could not load the TODO list. Check the server logs.').outerHTML;
        });
}

function closeTodo() {
    var modal = document.getElementById('cgTodoModal');
    if (modal && modal.style.display !== 'none') {
        modal.style.display = 'none';
        document.getElementById('cgTodoBody').innerHTML = '';
    }
}

// Whether a URL is a themoviedb.org page over https, the same rule Services/Tmdb/TmdbLinks.IsWatchUrl
// applies server-side to pick a title's "where to watch" page out of its offers.
function isTmdbWatchUrl(url) {
    return typeof url === 'string' && /^https:\/\/([^/]*\.)?themoviedb\.org\//i.test(url);
}

// The row's streaming-service icons: the same compact serviceIcons() a report row shows once its
// availability is resolved, kept live across re-renders (a "Show fulfilled" toggle re-renders the whole
// list) since the result is cached on the row itself, not just in the DOM. Unresolved rows show a small
// "Checking..." note rather than the report's per-row button, since fulfillmentQueue primes every
// watchable row automatically on load (see primeFulfillmentAvailability): a household's queue is small
// enough that there is no need to make the administrator ask for each one.
function demandWatchCell(row) {
    var tmdb = row.ProviderIds && row.ProviderIds.Tmdb;
    var watchable = !!tmdb && (row.TargetKindName === 'Movie' || row.TargetKindName === 'Series');
    var body = watchable
        ? (row.AvailabilityChecked ? serviceIcons(row) : wrap('span', { 'class': 'cgDimmed' }, 'Checking...'))
        : '';
    return wrap('span', { 'class': 'cgFulfillWatch', 'data-rowid': row.Id }, body);
}

// One fulfillment queue row: title/year, who still wants it, the links a todo row would show plus the
// streaming-service icons (see demandWatchCell), and a Mark fetched action that closes the title out for
// every requester at once.
function demandRowHtml(row, template) {
    var titleMeta = (row.Name || '') + (row.Year ? ' (' + row.Year + ')' : '');
    var titleCell = wrap('td', { 'class': 'cgTodoTitle' }, esc(titleMeta));
    var who = wrap('span', { 'class': 'cgTodoOwner' },
        esc('Wanted by ' + (row.RequestedBy || []).join(', ') + ' (' + row.OpenCount + ' of ' + row.RequestCount + ' still open)'));
    var creatorCell = wrap('td', { 'class': 'cgTodoCreator' }, todoSourceCell(row) + who);

    var links = todoLinks(row, template).map(function (l) {
        return l.Provider
            ? providerLink(l)
            : newTab(true, { 'class': 'cgLink', href: l.Url, title: l.Title }, esc(l.Name));
    });
    var jwSearch = (row.TargetKindName === 'Movie' || row.TargetKindName === 'Series')
        ? newTab(false, {
            'class': 'cgLink cgPopLink emby-button', href: 'https://www.justwatch.com/' + jwLocale() + '/search?q=' + encodeURIComponent(row.Name || ''),
            title: 'Search JustWatch for where to watch'
        }, 'Search JustWatch')
        : '';
    var linksCell = wrap('td', null, demandWatchCell(row) + wrap('div', { 'class': 'cgTodoLinks' }, links.join('')) + jwSearch);

    var fulfilled = row.OpenCount === 0;
    var actions = fulfilled
        ? wrap('span', { 'class': 'cgTodoNote' }, 'Fulfilled')
        : actionBtn('cgFulfillDone', { 'data-rowid': row.Id, title: 'Mark this title fetched for everyone who wants it' }, 'Mark fetched');
    var actionsCell = wrap('td', { 'class': 'cgTodoActions' }, actions);

    return wrap('tr', { 'class': 'cgTodoRow' + (fulfilled ? ' cgTodoDone' : ''), 'data-rowid': row.Id },
        titleCell + creatorCell + linksCell + actionsCell);
}

// Render the loaded queue, grouped into per-domain sections like the TODO modal. Fully fulfilled rows
// (nobody still waiting) are hidden unless "Show fulfilled" is ticked, the same convention as the
// report's own "Show dismissed" filter.
function renderFulfillment(modal) {
    var body = document.getElementById('cgFulfillBody');
    var showDone = document.getElementById('cgFulfillShowDone').checked;
    var items = ((modal._data && modal._data.Items) || []).filter(function (r) { return showDone || r.OpenCount > 0; });
    if (!items.length) {
        body.innerHTML = h('div', { 'class': 'cgTodoEmpty' },
            (modal._data && modal._data.Items && modal._data.Items.length)
                ? 'Nothing outstanding; everything on a TODO list has been marked fetched.'
                : 'Nobody has anything on their TODO list yet.').outerHTML;
        return;
    }
    var template = modal._template || '';
    var byDomain = groupBy(items, function (it) { return it.DomainName || 'Other'; });
    byDomain.order.sort(function (a, b) {
        var ra = todoDomainRank(a), rb = todoDomainRank(b);
        return ra !== rb ? ra - rb : ci(a, b);
    });
    var html = '';
    byDomain.order.forEach(function (domain) {
        html += h('div', { 'class': 'cgTodoSection' }, domain).outerHTML;
        var rows = byDomain.map[domain].map(function (r) { return demandRowHtml(r, template); }).join('');
        html += wrap('table', { 'class': 'cgTodoTable' }, wrap('tbody', null, rows));
    });
    body.innerHTML = html;
}

function loadFulfillment(modal) {
    return ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('MindTheGaps/Todo/Demand'), dataType: 'json' })
        .then(function (data) {
            modal._data = data || { Items: [] };
            modal._template = (data && data.SearchUrlTemplate) || '';
            renderFulfillment(modal);
        });
}

// Looks up "where to watch" for every watchable row that does not have it yet (everything, on first
// load), one at a time so a large queue does not burst TMDB, and patches each row's icons in place as
// each answer comes back rather than waiting for the whole queue or re-rendering the list. The result is
// cached on the row itself (row.AvailabilityChecked), so a later re-render (the "Show fulfilled" toggle)
// or a second open of the modal within the same page load does not look anything up twice.
function primeFulfillmentAvailability(page, modal) {
    var cssEsc = window.CSS && CSS.escape ? CSS.escape : function (s) { return s; };
    var pending = ((modal._data && modal._data.Items) || []).filter(function (row) {
        var tmdb = row.ProviderIds && row.ProviderIds.Tmdb;
        return !row.AvailabilityChecked && tmdb && (row.TargetKindName === 'Movie' || row.TargetKindName === 'Series');
    });
    return pending.reduce(function (chain, row) {
        return chain.then(function () {
            return ApiClient.ajax({
                type: 'GET',
                url: ApiClient.getUrl('MindTheGaps/Availability', { tmdbId: row.ProviderIds.Tmdb, targetKind: row.TargetKindName }),
                dataType: 'json'
            }).then(function (offers) {
                noteProviders(page, offers);
                row.Availability = (offers || []).map(function (o) { return { Provider: o.Provider, MonetizationType: o.MonetizationType, LogoUrl: o.LogoUrl }; });
                row.WatchUrl = (offers || []).map(function (o) { return o.Url; }).filter(isTmdbWatchUrl)[0];
                row.AvailabilityChecked = true;
            }).catch(function () {
                // Left unchecked, so a later prime (a fresh open of the modal) tries again.
            }).then(function () {
                var slot = document.querySelector('#cgFulfillBody .cgFulfillWatch[data-rowid="' + cssEsc(row.Id) + '"]');
                if (slot) { slot.outerHTML = demandWatchCell(row); }
            });
        });
    }, Promise.resolve());
}

function openFulfillment(page) {
    var modal = document.getElementById('cgFulfillModal');
    var body = document.getElementById('cgFulfillBody');
    body.innerHTML = h('p', { 'class': 'fieldDescription' }, 'Loading the fulfillment queue...').outerHTML;
    modal.style.display = 'flex';
    loadFulfillment(modal)
        .then(function () { return primeFulfillmentAvailability(page, modal); })
        .catch(function () {
            body.innerHTML = h('p', { 'class': 'fieldDescription' }, 'Could not load the fulfillment queue. Check the server logs.').outerHTML;
        });
}

function closeFulfillment() {
    var modal = document.getElementById('cgFulfillModal');
    if (modal && modal.style.display !== 'none') {
        modal.style.display = 'none';
        document.getElementById('cgFulfillBody').innerHTML = '';
    }
}

// Find a stored TODO entry by id and owner (the in-modal data the render used), so a row update keeps the
// section ordering and the export in step without a reload. The same gap can be on several users' lists.
function todoEntryById(modal, id, owner) {
    var items = (modal._data && modal._data.Items) || [];
    for (var i = 0; i < items.length; i++) { if (items[i].Id === id && items[i].OwnerId === owner) { return items[i]; } }
    return null;
}

// Apply a Done state to a row's markup and its stored entry (no full re-render, so the row stays
// put). note is an optional line under the links (the Verify result).
function todoApplyDone(modal, id, owner, done, note) {
    var entry = todoEntryById(modal, id, owner);
    if (entry) { entry.Done = done; }
    var cssEsc = window.CSS && CSS.escape ? CSS.escape : function (s) { return s; };
    var row = document.querySelector('#cgTodoBody .cgTodoRow[data-id="' + cssEsc(id) + '"][data-owner="' + cssEsc(owner) + '"]');
    if (!row) { return; }
    if (done) { row.classList.add('cgTodoDone'); } else { row.classList.remove('cgTodoDone'); }
    var box = row.querySelector('.cgTodoDoneBox');
    if (box) { box.checked = done; }
    var noteEl = row.querySelector('.cgTodoNote');
    if (note) {
        if (!noteEl) {
            noteEl = document.createElement('div');
            noteEl.className = 'cgTodoNote';
            var linkCell = row.querySelector('.cgTodoLinks');
            if (linkCell && linkCell.parentNode) { linkCell.parentNode.appendChild(noteEl); }
        }
        noteEl.textContent = note;
    } else if (noteEl && !done) {
        noteEl.parentNode.removeChild(noteEl);
    }
}

// Build the TODO export: one H2 per domain (in the report's domain order), then a table per
// domain with a checkbox cell, the title and year, the source, and the links as Markdown links.
function buildTodoMarkdown(modal) {
    var items = todoVisible(modal);
    var everyone = modal._who === TODO_EVERYONE;
    var owner = ((modal._data && modal._data.Owners) || []).filter(function (o) { return o.UserId === modal._who; })[0];
    var heading = everyone ? "Everyone's TODO lists"
        : (owner && owner.UserId !== (modal._data || {}).CallerId ? owner.UserName + "'s TODO list" : 'My TODO list');
    var template = modal._template || '';
    var out = ['# Mind the Gaps: ' + mdHeading(heading), ''];
    out.push('_' + items.length + ' items, exported ' + new Date().toLocaleString() + '_', '');
    var byDomain = groupBy(items, function (it) { return it.DomainName || 'Other'; });
    byDomain.order.sort(function (a, b) {
        var ra = todoDomainRank(a), rb = todoDomainRank(b);
        return ra !== rb ? ra - rb : ci(a, b);
    });
    byDomain.order.forEach(function (domain) {
        out.push('## ' + mdHeading(domain), '');
        out.push('| Done | Title | Source | Links |');
        out.push('| --- | --- | --- | --- |');
        byDomain.map[domain].forEach(function (entry) {
            var box = entry.Done ? '[x]' : '[ ]';
            var titleMeta = (entry.Name || '') + (entry.Year ? ' (' + entry.Year + ')' : '')
                + (everyone ? ' (on ' + (entry.OwnerName || 'a user') + "'s list)" : '');
            var links = todoLinks(entry, template).map(function (l) {
                return '[' + mdEsc(l.Name || 'Link') + '](' + safeUrl(l.Url) + ')';
            });
            out.push('| ' + box + ' | ' + mdEsc(titleMeta) + ' | ' + mdEsc(todoSourceText(entry)) + ' | ' + links.join(' ') + ' |');
        });
        out.push('');
    });
    return out.join('\n');
}

