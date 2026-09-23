// Report page, part 9: adding a gap to your own TODO list (the per-row button and the multi-select
// bar), and the admin-only fulfillment queue that is the one place that list is read back - everyone's
// TODO list folded into one demand-sorted list for the person actually buying things. There is no
// per-user "my list" viewer on this page on purpose: the fulfillment queue already includes your own
// entries, folded in with everyone else's, so a second view of just your own would only duplicate it.

// Domain ordering for the TODO/fulfillment sections, mirroring the report's Movies/Shows/Music/Books order,
// then anything else after.
var TODO_DOMAIN_ORDER = ['Movies', 'Shows', 'Music', 'Books'];
function todoDomainRank(name) {
    var i = TODO_DOMAIN_ORDER.indexOf(name);
    return i < 0 ? TODO_DOMAIN_ORDER.length : i;
}

// "name year creator" for an Amazon / web search, trimmed so a missing field leaves no gap. Creator is
// the author or artist for a book/album entry (see TodoEntry.Creator), and without it a search for a
// title alone is often useless: many book and album titles are shared across unrelated works.
function todoSearchTerm(entry) {
    return ((entry.Name || '') + ' ' + (entry.Year || '') + ' ' + (entry.Creator || '')).trim();
}

// The web-search URL: the configured template with {0} replaced by the encoded search term. Empty when
// no template is set, so the link is dropped.
function todoWebSearchUrl(template, entry) {
    if (!template) { return ''; }
    return template.replace('{0}', encodeURIComponent(todoSearchTerm(entry)));
}

function todoAmazonUrl(entry) {
    return 'https://www.amazon.com/s?k=' + encodeURIComponent(todoSearchTerm(entry));
}

// Add ids to the caller's own TODO list, then confirm. ids is an array of gap ids.
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

// The distinct user ids represented anywhere in the loaded queue (a title can have several requesters,
// and the queue holds many titles), for Verify all.
function fulfillmentOwnersInView(modal) {
    var seen = {};
    var owners = [];
    ((modal._data && modal._data.Items) || []).forEach(function (row) {
        (row.Entries || []).forEach(function (e) {
            if (!seen[e.OwnerId]) { seen[e.OwnerId] = true; owners.push(e.OwnerId); }
        });
    });
    return owners;
}

// Checks every user represented in the queue against the library, one list at a time, then reloads the
// queue from a fresh read so a title the library now holds - however it arrived, not just through Mark
// fetched - drops off or shrinks its open count. The caller re-primes availability afterward (this stays
// page-agnostic, like the rest of this file).
function verifyAllFulfillment(modal) {
    var checked = 0;
    var owned = 0;
    return fulfillmentOwnersInView(modal).reduce(function (chain, ownerId) {
        return chain.then(function () {
            return ApiClient.ajax({ type: 'POST', url: ApiClient.getUrl('MindTheGaps/Todo/VerifyAll', { userId: ownerId }), dataType: 'json' })
                .then(function (res) {
                    checked += (res && res.Checked) || 0;
                    owned += (res && res.Owned) || 0;
                });
        });
    }, Promise.resolve()).then(function () {
        return loadFulfillment(modal).then(function () { return { Checked: checked, Owned: owned }; });
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

// Build the fulfillment queue export: one H2 per domain (in the report's domain order), then a table per
// domain with a fetched checkbox cell, the title and year, who still wants it, the source, and the links
// as Markdown links. Exports exactly what is on screen (respects "Show fulfilled"), same as the modal.
function buildFulfillmentMarkdown(modal) {
    var showDone = document.getElementById('cgFulfillShowDone').checked;
    var items = ((modal._data && modal._data.Items) || []).filter(function (r) { return showDone || r.OpenCount > 0; });
    var template = modal._template || '';
    var out = ['# Mind the Gaps: Fulfillment queue', ''];
    out.push('_' + items.length + ' items, exported ' + new Date().toLocaleString() + '_', '');
    var byDomain = groupBy(items, function (it) { return it.DomainName || 'Other'; });
    byDomain.order.sort(function (a, b) {
        var ra = todoDomainRank(a), rb = todoDomainRank(b);
        return ra !== rb ? ra - rb : ci(a, b);
    });
    byDomain.order.forEach(function (domain) {
        out.push('## ' + mdHeading(domain), '');
        out.push('| Fetched | Title | Wanted by | Source | Links |');
        out.push('| --- | --- | --- | --- | --- |');
        byDomain.map[domain].forEach(function (row) {
            var box = row.OpenCount === 0 ? '[x]' : '[ ]';
            var titleMeta = (row.Name || '') + (row.Year ? ' (' + row.Year + ')' : '');
            var wanted = (row.RequestedBy || []).join(', ') + ' (' + row.OpenCount + ' of ' + row.RequestCount + ' still open)';
            var links = todoLinks(row, template).map(function (l) {
                return '[' + mdEsc(l.Name || 'Link') + '](' + safeUrl(l.Url) + ')';
            });
            out.push('| ' + box + ' | ' + mdEsc(titleMeta) + ' | ' + mdEsc(wanted) + ' | ' + mdEsc(todoSourceText(row)) + ' | ' + links.join(' ') + ' |');
        });
        out.push('');
    });
    return out.join('\n');
}

