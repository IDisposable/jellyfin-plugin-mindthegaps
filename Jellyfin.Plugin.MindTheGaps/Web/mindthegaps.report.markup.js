// Report page, part 1: vocabulary/label lookups, generic markup helpers (icons, links, buttons),
// row sort/dismissal state, the provider/availability filter and its watch-click handling, item
// popovers, and the row renderer itself. mindthegaps.common.js holds the page-agnostic esc/safeUrl/
// h/wrap kit this and every other report file below build on.

// There is one report page, and several helpers below are reached from render paths that do not carry
// it, so they look it up rather than taking it as a parameter.
function reportPage() { return document.querySelector('#MindTheGapsPage'); }

// The report's vocabulary (patterns, domains, Set completion kinds) is served on the summary rather
// than restated here, so the dashboard cannot drift from the model: a new domain or set kind appears
// on its own, and the display order is the server's. Only the wording of each stays below.
// Empty until the summary loads, which is before anything renders.
function vocab() {
    var s = (reportPage() || {})._summary || {};
    return {
        patterns: s.Patterns || [],
        domains: s.Domains || [],
        setKinds: s.SetKinds || [],
        discoverKinds: s.DiscoverKinds || [],
        recheckPrefixes: s.RecheckPrefixes || [],
        mintableKinds: s.MintableKinds || {},
        searchUrlTemplate: s.SearchUrlTemplate || ''
    };
}

// Position of a value in an ordered vocabulary, with anything unlisted sorting last rather than first
// (which a bare indexOf would do, -1 being lowest).
function rankIn(order, value) {
    var i = order.indexOf(value);
    return i < 0 ? 9999 : i;
}
// Pattern labels worded for the domain in view (the Type filter): each pattern maps a domain to
// its label, with the '' entry the default wording for a domain that has no special label (for
// example Movies under SetCompletion) and for an inactive tab. So a movie set is "Set
// completion", a show "Series completion", music a "Discography", books a "Bibliography"; a
// creator's works are "Artist works"/"Author works" for music/books; Recommendation is "Discover".
var PATTERN_LABELS = {
    SetCompletion: { '': 'Set completion', Shows: 'Series completion', Music: 'Discography', Books: 'Bibliography' },
    CreatorWorks: { '': 'Creator works', Music: 'Artist works', Books: 'Author works' },
    Recommendation: { '': 'Discover' }
};
function patternLabel(pattern, domain) {
    var byDomain = PATTERN_LABELS[pattern] || {};
    return byDomain[domain] || byDomain[''] || pattern;
}
// Lowercase a label and turn every run of whitespace into a single hyphen, for a download
// filename, so a multi-word domain or pattern label stays one clean token.
function slugify(s) {
    return String(s == null ? '' : s).toLowerCase().replace(/\s+/g, '-');
}
var MONETIZATION_LABELS = { flatrate: 'Subscription', free: 'Free', ads: 'With ads', rent: 'Rent', buy: 'Buy' };


// A material-icons glyph (name auto-escaped via h's textContent). Sizing and alignment come from
// CSS; pass cls 'cgIconLead' for an icon that leads text, 'cgIconFollow' for one that trails it.
function icon(name, cls) {
    return h('span', { 'class': 'material-icons' + (cls ? ' ' + cls : ''), 'aria-hidden': 'true' }, name).outerHTML;
}

// A new-tab link over wrap: target=_blank always, rel flips on the external flag (an external
// link drops the referrer with noreferrer; an internal Jellyfin link keeps it).
function newTab(external, attrs, innerHtml) {
    return wrap('a', Object.assign({ target: '_blank', rel: external ? 'noopener noreferrer' : 'noopener' }, attrs), innerHtml);
}

// External provider link: a labelled new-tab emby-linkbutton; the name is the escaped label.
function providerLink(l) {
    return newTab(true, {
        is: 'emby-linkbutton',
        'class': 'cgLink' + providerClass(l.Name),
        'data-provider': l.Name,
        title: 'Open on ' + l.Name,
        'aria-label': 'Open on ' + l.Name,
        href: l.Url
    }, esc(l.Name));
}

// Internal Jellyfin link: a new-tab anchor that keeps the referrer since it stays on this server.
// innerHtml is already-built child markup (an icon). itemUrl/searchUrl return absolute http(s)
// URLs, so href clears safeUrl.
function jellyfinLink(attrs, innerHtml) {
    return newTab(false, attrs, innerHtml);
}

// A row action button: the shared "cgLink cg<Action>" button with its data-* attributes built
// safely by h(); innerHtml is the (literal) label, optionally led by an icon().
function actionBtn(cls, attrs, innerHtml) {
    return wrap('button', Object.assign({ type: 'button', 'class': 'cgLink ' + cls }, attrs), innerHtml);
}

// A click-handled cgLink anchor (no href): the dismiss, restore, and batch controls whose clicks
// are caught by delegation. cls is the action class; innerHtml is the label, an icon(), or the
// &times; glyph.
function cgAnchor(cls, attrs, innerHtml) {
    return wrap('a', Object.assign({ 'class': 'cgLink' + (cls ? ' ' + cls : '') }, attrs), innerHtml);
}

// A cgAnchor bound to a gap or source id, with its tooltip: the click delegation reads data-gapid
// to know what was clicked, and title is the other attribute these controls always carry.
function idAnchor(cls, id, title, attrs, innerHtml) {
    return cgAnchor(cls, Object.assign({ 'data-gapid': id, title: title }, attrs), innerHtml);
}

// The gap's media domain (Movies/Shows/Music/Books) straight from the model.
function categoryOf(item) { return item.DomainName || 'Other'; }

// JustWatch search needs a locale in the path; take the region from the browser, default US.
// The region for JustWatch and availability links. Prefer the configured country (the same
// MetadataCountryCode the availability lookups use), falling back to the browser's language.
function jwLocale() {
    if (cgRegion) { return cgRegion; }
    var m = (navigator.language || 'en-US').match(/-([a-z]{2})$/i);
    return m ? m[1].toLowerCase() : 'us';
}

function ci(a, b) { a = a.toLowerCase(); b = b.toLowerCase(); return a < b ? -1 : (a > b ? 1 : 0); }

// The active row sort, set from the Sort dropdown before each render.
var currentSort = 'title';

// The configured country (MetadataCountryCode), lowercased, for region-specific links. Loaded
// once on pageshow and refreshed on save; empty until then, so jwLocale falls back to the browser.
var cgRegion = '';

// This server's display name, for labeling export links that point back to it. Loaded once on
// pageshow; empty until then, so the label falls back to "Jellyfin".
var cgServerName = '';

// Which acquisition targets (Radarr/Sonarr/Jellyseerr) are configured, so a row shows a Send
// button only for a set-up target. Loaded on pageshow and refreshed on save; null until then,
// so no Send buttons appear.
var acqConfig = null;

// Radarr/Sonarr quality profiles for the per-row Send picker, fetched once per kind on first need and
// cached here: {Movie: {Profiles, DefaultId}, Series: {...}}. Undefined means not yet requested; a kind
// stays absent (and its picker hidden) when the fetch fails or the matching arr has none configured -
// Send still uses the configured default profile either way.
var sendProfiles = {};

// Monotonic counter for per-render group body ids (aria-controls targets).
var cgGroupSeq = 0;

// Deferred group bodies: a creator-works group starts collapsed with an empty body and a
// builder registered here under a token, so a tab with tens of thousands of rows only builds
// the rows for groups the user actually opens. Reset each render (tokens are per-render).
var lazyBodies = {};

// Builds a group's body the first time it is expanded (if it was registered as deferred),
// then drops the marker so it is not rebuilt. A no-op for eager (already-built) groups.
function ensureGroupBody(groupEl) {
    if (!groupEl) { return; }
    var token = groupEl.getAttribute('data-cglazy');
    if (!token) { return; }
    groupEl.removeAttribute('data-cglazy');
    var build = lazyBodies[token];
    var body = groupEl.querySelector('.cgBody');
    if (build && body) { body.innerHTML = build(); }
}

// Sort a leaf group's rows by the active mode (popularity desc, then title; or just title).
function sortRows(items) {
    var byTitle = function (a, b) { return ci(a.Name || '', b.Name || ''); };
    var cmp = byTitle;
    if (currentSort === 'popularity') {
        cmp = function (a, b) {
            var pa = a.SortScore == null ? -1 : a.SortScore;
            var pb = b.SortScore == null ? -1 : b.SortScore;
            return pb !== pa ? pb - pa : byTitle(a, b);
        };
    }
    return items.slice().sort(cmp);
}

// Report-level "where to watch" filters. Monetization types are fixed checkboxes; providers
// are discovered from offers as availability is looked up (default-on, unchecked remembered).
var disabledProviders = {};
var knownProviders = [];
// The provider list is long (one entry per streaming service), so it collapses by default.
var providersExpanded = false;
// gap id -> { Kind?, Note, ResolvedUtc, SnoozedUntil? } for dismissed gaps (resolved /
// not interested / snoozed). A missing Kind means "resolved".
var resolvedMap = {};

// The active dismissal for a gap, or null. A snooze whose date has passed is treated as
// gone (the gap resurfaces) without needing the server to clear it.
function activeDismissal(it) {
    var r = resolvedMap[it.Id];
    if (!r) { return null; }
    if (r.Kind === 'snoozed') {
        var until = r.SnoozedUntil ? new Date(r.SnoozedUntil).getTime() : 0;
        if (until && Date.now() >= until) { return null; }
    }
    return r;
}

// Whole-creator dismissals are stored in the same map under a "creator:{guid}" key.
function creatorDismissed(guid) { return !!(guid && resolvedMap['creator:' + guid]); }

// Dismissed recommendation sources are stored under a "recsource:{guid}" key.
function recSourceDismissed(guid) { return !!(guid && resolvedMap['recsource:' + guid]); }

// How many of a recommendation's sources (primary plus others) are not dismissed.
function effectiveRecSourceCount(it) {
    var n = 0;
    if (it.SourceItemName && !recSourceDismissed(it.SourceItemId)) { n++; }
    (it.OtherSources || []).forEach(function (s) { if (s && s.Name && !recSourceDismissed(s.Id)) { n++; } });
    return n;
}

// The dismiss/restore control shown on a creator (person) group header.
function creatorDismissBtn(guid, name) {
    if (!guid) { return ''; }
    if (creatorDismissed(guid)) {
        return ' ' + idAnchor('cgRestoreCreator', guid, 'Scan this creator again', null, 'Restore');
    }
    return ' ' + idAnchor('cgDismissCreator', guid, 'Never delve into this creator (stop scanning and hide their gaps)', { 'data-name': name || '' }, 'Not interested in creator');
}

// Resolve-all / Not-interested-all controls for a series or season group header: they dismiss
// every listed gap under the group in one batch. The click handler collects the ids from the
// group's rows, so the label is only for the confirm prompt.
function batchDismissBtns(label) {
    return ' ' + cgAnchor('cgBatchResolve', { 'data-label': label, title: 'Resolve all listed items here (not really missing)', 'aria-label': 'Resolve all listed items here (not really missing)' }, icon('done'))
        + ' ' + cgAnchor('cgBatchNotInterested', { 'data-label': label, title: 'Mark all listed items here as not interested', 'aria-label': 'Mark all listed items here as not interested' }, icon('close'));
}

// Whether the server can re-run the source behind this owning item on its own, which is what the
// clear-down offers once a verify leaves something still missing. The server lists the gap-id prefixes
// it can re-check (summary.RecheckPrefixes), so this asks rather than inferring it from the pattern and
// domain: the answer also depends on which sources are enabled, which the page cannot see. The owner
// must still be a library item, since a studio, keyword, label, or curated list carries a synthetic id.
function ownerRecheckable(it) {
    if (!it || !isGuidId(it.SourceItemId)) { return false; }
    return vocab().recheckPrefixes.some(function (p) { return it.Id.indexOf(p) === 0; });
}

// An N-format Jellyfin guid, which is what a library-backed source carries in SourceItemId. The
// rest (a curated list, a studio, a Discogs label) carries a synthetic key like "mdblist-123",
// which no per-item re-check can resolve back to a library item.
function isGuidId(id) { return /^[0-9a-f]{32}$/i.test(id || ''); }

// The one clear-down control, on every level of the tree and on every row. Stage one always checks
// the rows in scope against the library and drops the ones you now hold; if any survive and their
// sources can be re-run, stage two offers to ask the providers again. scope/season narrow what the
// click covers: a domain, a set kind, one group, one season, or a single row.
function clearBtn(scope, key, label) {
    var title = 'Check ' + label + ' against your library and clear what you now have, then offer a provider re-check for the rest';
    return ' ' + cgAnchor('cgClear emby-button', {
        'data-scope': scope,
        'data-key': key == null ? '' : String(key),
        title: title,
        'aria-label': title
    }, icon('refresh'));
}

// The greyed status line for a dismissed gap.
function dismissalLabel(r) {
    if (r.Kind === 'notinterested') { return 'Not interested' + (r.Note ? ': ' + esc(r.Note) : ''); }
    if (r.Kind === 'snoozed') { return 'Snoozed until ' + (r.SnoozedUntil ? new Date(r.SnoozedUntil).toLocaleDateString() : 'release'); }
    return 'Resolved' + (r.Note ? ': ' + esc(r.Note) : '');
}

function monAllowed(type) {
    if (!type) { return true; }
    var cb = document.querySelector('#MindTheGapsPage .cgMon[data-mon="' + type + '"]');
    return !cb || cb.checked;
}

// TMDB lists a service's tiers and the channels its resellers carry as separate providers ("Netflix
// Standard with Ads", "HBO Max Amazon Channel"). To the person reading the list they are one service, so a
// row shows it once and the filter has one entry for it. Only these well-known suffixes are folded: a name
// that merely looks related ("Netflix Kids", "Paramount Plus Premium") is a different offering.
var PROVIDER_VARIANT = / (?:Standard )?with Ads$| Amazon Channel$| Apple TV [Cc]hannel$| Roku Premium Channel$/;

function providerFamily(name) { return name ? name.replace(PROVIDER_VARIANT, '') : name; }

// The distinct service names behind a list of provider names, sorted.
function providerFamilies(names) {
    var seen = {};
    return names.map(providerFamily).filter(function (n) {
        if (!n || seen[n]) { return false; }
        seen[n] = true;
        return true;
    }).sort();
}

function providerAllowed(name) { return !disabledProviders[providerFamily(name)]; }

function filterOffers(offers) {
    return (offers || []).filter(function (o) {
        return monAllowed(o.MonetizationType) && providerAllowed(o.Provider);
    });
}

function renderProviderFilter(page) {
    var el = page.querySelector('#cgProviderFilter');
    if (!knownProviders.length) { el.innerHTML = ''; return; }
    var total = knownProviders.length;
    var enabledCount = 0;
    for (var i = 0; i < total; i++) { if (!disabledProviders[knownProviders[i]]) { enabledCount++; } }

    // "Enable all" only when some are off; "disable all" only when some are on.
    var toggles = '';
    if (enabledCount < total) {
        toggles += cgAnchor('cgProvAll', { title: 'Enable every provider', 'aria-label': 'Enable every provider' }, icon('done_all'));
    }
    if (enabledCount > 0) {
        toggles += cgAnchor('cgProvNone', { title: 'Disable every provider', 'aria-label': 'Disable every provider' }, icon('clear'));
    }

    // Collapsible: a header (with a caret and the enabled-of-total count) toggles the long list.
    var caret = h('span', { 'class': 'cgCaret' + (providersExpanded ? ' cgCaretOpen' : '') }).outerHTML;
    var header = wrap('span', { 'class': 'cgProvToggle', title: 'Show or hide the provider list' },
        caret + esc('Providers (' + enabledCount + ' of ' + total + ')')) + ' ' + toggles;
    var list = wrap('div', { 'class': 'cgProvList' + (providersExpanded ? ' cgProvListOpen' : '') },
        knownProviders.map(function (name) {
            var box = h('input', Object.assign({ type: 'checkbox', 'class': 'cgProv', 'data-prov': name }, disabledProviders[name] ? {} : { checked: 'checked' })).outerHTML;
            return wrap('label', { 'class': 'cgProvLabel' }, box + ' ' + esc(name));
        }).join(''));
    el.innerHTML = header + list;
}

// Add any newly-seen providers to the filter (default enabled) and persist.
function noteProviders(page, offers) {
    var added = false;
    (offers || []).forEach(function (o) {
        var service = providerFamily(o.Provider);
        if (service && knownProviders.indexOf(service) === -1) { knownProviders.push(service); added = true; }
    });
    if (added) { knownProviders.sort(); renderProviderFilter(page); saveFilters(page); }
}

// The lazy "Where to watch" lookup: fetches offers for the button's title and replaces it with the
// result. Shared by the report list's own delegated click handler and the fulfillment queue's, since
// a demand row has no live report row to key off, only a bare TMDB id and kind.
function handleWatchClick(page, watchBtn) {
    watchBtn.textContent = 'Loading…';
    watchBtn.disabled = true;
    ApiClient.ajax({
        type: 'GET',
        url: ApiClient.getUrl('MindTheGaps/Availability', { tmdbId: watchBtn.getAttribute('data-tmdb'), targetKind: watchBtn.getAttribute('data-type') }),
        dataType: 'json'
    }).then(function (offers) {
        noteProviders(page, offers);
        var note = document.createElement('div');
        note.className = 'fieldDescription cgAvail';
        note.style.marginTop = '.2em';
        note._offers = offers || [];
        renderAvail(note);
        var linksRow = watchBtn.closest('.cgLinks');
        if (linksRow) { linksRow.insertAdjacentElement('afterend', note); } else { watchBtn.parentNode.appendChild(note); }
        watchBtn.remove();
    }).catch(function () {
        watchBtn.textContent = 'Where to watch';
        watchBtn.disabled = false;
    });
}

// Render an availability note from the full offer set it stashed, applying the current filters.
function renderAvail(note) {
    var shown = filterOffers(note._offers);
    note.innerHTML = shown.length
        ? 'Where to watch: ' + availLinks(shown)
        : (note._offers && note._offers.length ? 'No offers match the selected filters.' : 'No streaming offers found in your region.');
}

// Every offer shares one "where to watch" link (the TMDB/JustWatch page), so lead with a
// single "Options" button and list the providers as text rather than one identical link each.
function availLinks(offers) {
    var names = offers.map(function (o) {
        var mt = o.MonetizationType ? (MONETIZATION_LABELS[o.MonetizationType] || o.MonetizationType) : '';
        return esc(o.Provider) + (mt ? ' (' + esc(mt) + ')' : '');
    }).join(', ');
    var url = '';
    for (var i = 0; i < offers.length; i++) { if (offers[i].Url) { url = offers[i].Url; break; } }
    var button = url
        ? newTab(true, {
            is: 'emby-linkbutton', 'class': 'cgLink', href: url,
            title: 'Open the watch page', 'aria-label': 'Open the watch page'
        }, 'Watch ' + icon('open_in_new')) + ' '
        : '';
    return button + names;
}

// One recommending source: name, its year/type meta, an open-in-Jellyfin icon, and a small
// control to dismiss it (stop recommendations from this title).
function recSource(name, year, type, id) {
    var meta = [];
    if (year) { meta.push(year); }
    if (type) { meta.push(esc(type)); }
    var suffix = meta.length ? ' (' + meta.join(' &middot; ') + ')' : '';
    var dismiss = id ? ' ' + idAnchor('cgDismissRecSource', id, 'Stop recommendations from this title', { 'data-name': name || '', 'aria-label': 'Stop recommendations from this title' }, '&times;') : '';
    return esc(name) + suffix + openIcon(id) + dismiss;
}

// The dismiss control for a recommendation source, shown on its group header. Restoring a muted
// source is done from the "Muted sources" picker, so a dismissed source shows no button here.
function recSourceDismissBtn(guid, name) {
    if (!guid || recSourceDismissed(guid)) { return ''; }
    return ' ' + idAnchor('cgDismissRecSource', guid, 'Stop recommendations from this source', { 'data-name': name || '', 'aria-label': 'Stop recommendations from this source' }, '&times;');
}

// A gap is mintable when its kind is one the server can mint and it carries the provider id that kind
// is minted under. Both come from the minter (summary.MintableKinds), so the button appears exactly
// where a mint would be accepted. Episodes are native in core and so are not a mintable kind.
function isMintable(item) {
    var provider = vocab().mintableKinds[item.TargetKindName];
    return !!provider && !!(item.ProviderIds || {})[provider];
}

// Finds a currently-loaded gap by id, to rehydrate a handler that only has a data-gapid to work from.
function findRowItem(page, gapId) {
    var items = (page && page._report && page._report.Items) || [];
    for (var i = 0; i < items.length; i++) { if (items[i].Id === gapId) { return items[i]; } }
    return null;
}

// The Watch popover's body: resolved offers when known, else the on-demand lookup; always a
// JustWatch search underneath, not only as a fallback when nothing else resolved.
function buildWatchPopoverBody(item) {
    var tmdb = item.ProviderIds && item.ProviderIds.Tmdb;
    var watchTmdb = item.WatchTmdbId || tmdb;
    var watchKind = item.TargetKindName === 'Episode' ? 'Series' : item.TargetKindName;
    var watchable = !!watchTmdb && (item.TargetKindName === 'Movie' || item.TargetKindName === 'Series' || item.TargetKindName === 'Episode');
    var shownOffers = filterOffers(item.Availability);

    var body = '';
    if (shownOffers.length) {
        body = wrap('div', { class: "cgOffers" }, availLinks(shownOffers) + ' ');
    } else if (watchable && item.AvailabilityChecked) {
        body = wrap('div', { class: "cgOffers cgDimmed" }, 'No streaming sources found.');
    } else if (watchable) {
        body = wrap('div', { class: "cgOffers" },
            actionBtn('cgWatch', { 'data-tmdb': watchTmdb, 'data-type': watchKind }, 'Look up where to watch'));
    }

    if (item.TargetKindName === 'Movie' || item.TargetKindName === 'Series') {
        body += newTab(false, {
            'class': 'cgLink cgPopLink emby-button', href: 'https://www.justwatch.com/' + jwLocale() + '/search?q=' + encodeURIComponent(item.Name),
            title: 'Search JustWatch for where to watch'
        }, 'Search JustWatch');
    }

    return body || wrap('div', { style: 'opacity:.7;' }, 'Not applicable.');
}

// A generic Amazon and configured web search for a title, for a book or album gap: those have no TMDB
// id to browse a "where to watch" page for, and the title alone is often ambiguous without the author
// or artist (many are shared across unrelated works), so the term also carries the gap's source item
// (the same convention TodoEntry.Creator/todoSearchTerm use for the fulfillment queue's own links).
function externalSearchLinks(name, year, creator) {
    if (!name) { return ''; }
    var term = (name + ' ' + (year || '') + ' ' + (creator || '')).trim();
    var encoded = encodeURIComponent(term);
    var out = newTab(true, { 'class': 'cgLink cgPopLink emby-button', href: 'https://www.amazon.com/s?k=' + encoded, title: 'Search Amazon' }, 'Search Amazon');
    var template = vocab().searchUrlTemplate;
    if (template) {
        out += newTab(true, { 'class': 'cgLink cgPopLink emby-button', href: template.replace('{0}', encoded), title: 'Web search' }, 'Web search');
    }

    return out;
}

// The Information popover's body: the external id links this gap already carries, plus (for a book or
// album) the Amazon/web search fallback, then the search/open/dismiss icons a bare title has no room for.
function buildInfoPopoverBody(item) {
    var providerLinks = (item.Links || []).map(providerLink).join('');
    var bookOrMusic = item.DomainName === 'Books' || item.DomainName === 'Music';
    var externalSearch = bookOrMusic ? externalSearchLinks(item.Name, item.Year, item.SourceItemName) : '';
    return (providerLinks || wrap('div', { style: 'opacity:.7;margin-bottom:.3em;' }, 'No linked ids yet.'))
        + externalSearch
        + searchIcon(item.Name, domainScope(item.DomainName))
        + openIcon(item.LibraryItemId)
        + clearBtn('row', item.Id, 'this title');
}

// A quality-profile picker for a Send button, next to it: left hidden and empty until
// primeSendProfilePickers fills it in (Send still works with the configured default profile if that
// never happens), so a popover that is never opened never pays for the lookup.
function sendProfileSelect(kind) {
    return h('select', { 'is': 'emby-select', 'class': 'selectSmall cgSendProfile', 'data-kind': kind, style: 'display:none' }).outerHTML;
}

// Lazily fetches and caches a kind's quality profiles (shared by every row that asks for it), then fills
// whichever pickers of that kind are on the page right now.
function primeSendProfiles(kind) {
    if (sendProfiles[kind] !== undefined) {
        if (sendProfiles[kind]) { fillSendProfileSelects(kind, sendProfiles[kind]); }
        return;
    }

    sendProfiles[kind] = null;
    ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('MindTheGaps/QualityProfiles', { kind: kind }), dataType: 'json' })
        .then(function (result) {
            if (!result || !result.Profiles || !result.Profiles.length) { return; }
            sendProfiles[kind] = result;
            fillSendProfileSelects(kind, result);
        }, function () { /* leave it unset; Send still uses the configured default profile */ });
}

function fillSendProfileSelects(kind, result) {
    var selects = document.querySelectorAll('.cgSendProfile[data-kind="' + kind + '"]');
    Array.prototype.forEach.call(selects, function (select) {
        if (select.options.length) { return; }
        result.Profiles.forEach(function (p) {
            var opt = h('option', { value: p.Id }, p.Name);
            if (p.Id === result.DefaultId) { opt.selected = true; }
            select.appendChild(opt);
        });
        select.style.display = '';
    });
}

// After an Actions popover or expanded detail body renders, prime whichever kind(s) of Send picker it
// just built (a no-op wherever there is none, which is every other popover).
function primeSendProfilePickers(body) {
    var kinds = {};
    Array.prototype.forEach.call(body.querySelectorAll('.cgSendProfile'), function (select) { kinds[select.getAttribute('data-kind')] = true; });
    Object.keys(kinds).forEach(primeSendProfiles);
}

// The Actions popover's body: mint/acquisition/diagnose/todo as their own items, and a nested
// Resolve popover for the dismissal family (Resolve/Not interested/Snooze are the same Resolve
// call with a different canned note, so one popover, not three peers).
function buildActionsPopoverBody(item) {
    var tmdb = item.ProviderIds && item.ProviderIds.Tmdb;
    var watchTmdb = item.WatchTmdbId || tmdb;
    var res = activeDismissal(item);
    var actionItems = [];

    if (isMintable(item)) {
        actionItems.push(actionBtn('cgMint', { 'data-gapid': item.Id, title: 'Mint a virtual placeholder for this item' }, icon('eco', 'cgIconLead') + 'Mint'));
    }

    if (acqConfig) {
        if (acqConfig.RadarrConfigured && item.TargetKindName === 'Movie' && tmdb) {
            actionItems.push(sendProfileSelect('Movie'));
            actionItems.push(actionBtn('cgSendArr', { 'data-gapid': item.Id, 'data-kind': 'Movie', title: 'Send this movie to Radarr' }, icon('movie', 'cgIconLead') + 'Radarr'));
        }

        if (acqConfig.SonarrConfigured && (item.TargetKindName === 'Series' || item.TargetKindName === 'Episode')) {
            actionItems.push(sendProfileSelect('Series'));
            actionItems.push(actionBtn('cgSendArr', { 'data-gapid': item.Id, 'data-kind': 'Series', title: 'Send the owning series to Sonarr' }, icon('live_tv', 'cgIconLead') + 'Sonarr'));
        }

        if (acqConfig.SeerrConfigured && watchTmdb) {
            actionItems.push(actionBtn('cgSendSeerr', { 'data-gapid': item.Id, title: 'Request this title in Jellyseerr' }, icon('playlist_add', 'cgIconLead') + 'Request'));
        }
    }

    actionItems.push(actionBtn('cgDiagnose', { 'data-gapid': item.Id, 'data-name': item.Name, title: 'Why is this listed as missing?' }, icon('troubleshoot', 'cgIconLead') + 'Diagnose'));
    actionItems.push(actionBtn('cgTodoAdd', { 'data-gapid': item.Id, title: 'Add to my TODO list' }, icon('playlist_add_check', 'cgIconLead') + 'TODO'));

    var resolveBody;
    if (res) {
        resolveBody = wrap('div', { style: 'opacity:.8;margin-bottom:.4em;' }, dismissalLabel(res))
            + actionBtn('cgClearResolve', { 'data-gapid': item.Id, title: 'Clear the dismissal (show as missing again)' }, 'Clear');
    } else {
        resolveBody = actionBtn('cgResolve', { 'data-gapid': item.Id, title: 'Mark resolved (not really missing)' }, icon('done', 'cgIconLead') + 'Mark resolved')
            + actionBtn('cgNotInterested', { 'data-gapid': item.Id, title: 'Not interested (a real gap you do not want)' }, icon('not_interested', 'cgIconLead') + 'Not interested');
        if (item.IsUpcoming && item.ReleaseDate) {
            resolveBody += actionBtn('cgSnooze', { 'data-gapid': item.Id, 'data-until': item.ReleaseDate, title: 'Hide until it is released' }, 'Snooze until release');
        }
    }

    actionItems.push(wrap('details', { 'class': 'cgPop cgPopNested' },
        wrap('summary', null, 'Resolve' + icon('more_horiz', 'cgIconFollow')) + wrap('div', { 'class': 'cgPopBody' }, resolveBody)));

    return actionItems.join('');
}

// The title's click-revealed detail body: the overview, plus (for a Recommendation) the other
// titles that suggested it, whose primary source is the group header rather than a repeated peer
// here, then where to watch. No title line: the row's own <h3> is right above it. Non-compact
// (spacious) view has the room to fold in the Information and Actions popovers' content too, as
// flat buttons, instead of making those separate clicks; compact view keeps them behind their own
// icons, where space is tighter, so this stays overview-and-watch-only there.
function buildExpandedDetailBody(item) {
    var detailParts = [];
    if (item.PatternName === 'Recommendation' && (item.OtherSources || []).length) {
        var srcs = [];
        (item.OtherSources || []).forEach(function (s) { if (s && s.Name && !recSourceDismissed(s.Id)) { srcs.push(recSource(s.Name, s.Year, s.Type, s.Id)); } });
        if (srcs.length) { detailParts.push(wrap('p', { style: 'margin:0 0 .4em;opacity:.85;' }, 'Also recommended by: ' + srcs.join(', '))); }
    }
    if (item.Overview) { detailParts.push(wrap('p', { style: 'margin:0 0 .4em;opacity:.85;' }, esc(item.Overview))); }
    detailParts.push(buildWatchPopoverBody(item));
    if (!reportPage()._compact) {
        detailParts.push(buildInfoPopoverBody(item));
        detailParts.push(buildActionsPopoverBody(item));
    }
    return detailParts.join('');
}

function directChild(parent, selector) {
    for (var child = parent ? parent.firstElementChild : null; child; child = child.nextElementSibling) {
        if (child.matches && child.matches(selector)) { return child; }
    }
    return null;
}

// The list loads rows, not whole gaps: what only an opened row shows (its overview, external links and
// each offer's deeplink) stays on the server until the row is first opened, then is merged into the row's
// own object so every builder below reads one shape. A failed fetch leaves the row as it was, so its
// popover still renders (without the links) and the next open tries again.
function ensureItemDetail(item) {
    if (item._full) { return Promise.resolve(item); }
    if (!item._detailPending) {
        item._detailPending = ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('MindTheGaps/GapDetail', { id: item.Id }), dataType: 'json' })
            .then(function (full) {
                if (full) { Object.assign(item, full); }
                item._full = true;
                return item;
            }, function () {
                item._detailPending = null;
                return item;
            });
    }
    return item._detailPending;
}

// Only the Information popover and the expanded detail always need the detail; the Watch popover needs
// it only to show deeplinks, and a row with no offers has none to show.
function popoverNeedsDetail(kind, item) {
    if (kind === 'info') { return true; }
    return kind === 'watch' && (item.Availability || []).length > 0;
}

function populatePopover(page, det) {
    if (!det || !det.classList.contains('cgPop') || !det.hasAttribute('data-pop')) { return; }
    var body = directChild(det, '.cgPopBody');
    if (!body || det.dataset.built) { return; }
    det.dataset.built = '1';
    var row = det.closest('.cgRow');
    var item = row && findRowItem(page, row.getAttribute('data-gapid'));
    if (!item) { return; }
    var kind = det.getAttribute('data-pop');
    var build = kind === 'watch' ? buildWatchPopoverBody
        : kind === 'info' ? buildInfoPopoverBody
            : buildActionsPopoverBody;
    if (!popoverNeedsDetail(kind, item)) {
        body.innerHTML = build(item);
        primeSendProfilePickers(body);
        return;
    }
    body.innerHTML = wrap('div', { style: 'opacity:.7;' }, 'Loading');
    ensureItemDetail(item).then(function (full) { body.innerHTML = build(full); primeSendProfilePickers(body); });
}

// One item's row: checkbox, a thumbnail, a title (an <h3>, since a row is effectively a heading
// for its own content within the list), meta (year/kind/upcoming), and three icon popovers holding
// everything the row can do. #cgReportPanel's cgCompactMode class (the Compact view toggle) resizes
// the thumbnail and tightens the title/meta by CSS alone; the markup and behavior are identical
// either way, so there is exactly one renderer. The icons behave the same in both views until the
// title's detail is expanded: non-compact then hides them by CSS
// (#cgReportPanel:not(.cgCompactMode) .cgRow:has(.cgTitleDetail.cgPinned) .cgIcons), since their
// content is folded into that detail once it is open (see buildExpandedDetailBody) and there is nothing
// left for them to do; compact view keeps them regardless, since it never folds that content in.
//
// A popover's body, and the detail's body, start empty and build on first open/click (see the
// delegated 'toggle'/click handlers near the rest of #cgList's delegation): a report can hold
// thousands of rows, and most popovers are never opened, so building every body up front would add
// DOM weight for little benefit.
// A couple of tiny service icons on the collapsed line, so where a title streams is visible without
// opening it. They follow the same monetization and provider filters as the rest of the report. Streamable
// offers (subscription, free, ad-supported) lead, since a rent or buy price is not where it is, and a
// service offered several ways shows once. The logo comes with the offer when the lookup stored one; an
// offer without one, or whose logo fails to load, shows the service's initial letter.
var SERVICE_ICON_LIMIT = 2;
var OFFER_ORDER = { flatrate: 0, free: 1, ads: 2, rent: 3, buy: 4 };

// An <img> for a provider's image, loaded through the server's own image cache. The server redirects a browser
// to the provider itself whenever it cannot serve an image, so the provider's address rides along in
// data-direct only for what a redirect cannot cover (a host the route does not allow, or a server that cannot
// be reached): the error handler on #cgList loads that instead, so the cache can only ever make an image arrive
// sooner and never make one go missing.
function cachedImage(attrs, url) {
    var viaServer = /^https:\/\//i.test(url) ? ApiClient.getUrl('MindTheGaps/Image', { u: url }) : url;
    return h('img', Object.assign({ src: viaServer, 'data-direct': url, loading: 'lazy' }, attrs)).outerHTML;
}

function serviceIcons(item) {
    var offers = filterOffers(item.Availability);
    if (!offers.length) { return ''; }
    var ranked = offers.map(function (o, i) { return { o: o, i: i, r: OFFER_ORDER[o.MonetizationType] === undefined ? 5 : OFFER_ORDER[o.MonetizationType] }; })
        .sort(function (a, b) { return a.r - b.r || a.i - b.i; });
    // One icon per service (see providerFamily), wearing the base service's own logo when the title has it,
    // else the first variant's.
    var indexOf = {};
    var services = [];
    ranked.forEach(function (x) {
        var service = providerFamily(x.o.Provider);
        if (!service) { return; }
        if (indexOf[service] === undefined) {
            indexOf[service] = services.length;
            services.push({ name: service, offer: x.o });
        } else if (x.o.Provider === service && services[indexOf[service]].offer.Provider !== service) {
            services[indexOf[service]].offer = x.o;
        }
    });
    var shown = services.slice(0, SERVICE_ICON_LIMIT).map(function (s) {
        return s.offer.LogoUrl
            ? cachedImage({ alt: s.name, title: s.name, 'class': 'cgSvc' }, s.offer.LogoUrl)
            : h('span', { 'class': 'cgSvc cgSvcText', title: s.name }, s.name.charAt(0).toUpperCase()).outerHTML;
    });
    var rest = services.slice(SERVICE_ICON_LIMIT);
    if (rest.length) {
        shown.push(h('span', { 'class': 'cgSvcMore', title: rest.map(function (s) { return s.name; }).join(', ') }, '+' + rest.length).outerHTML);
    }
    // The icons are one link to the title's "where to watch" page on TMDB, which lists every service and how
    // it is offered, so a click goes straight there. A row that carries no such page shows them as plain icons.
    if (item.WatchUrl) {
        var names = services.map(function (s) { return s.name; }).join(', ');
        return wrap('a', {
            'class': 'cgSvcs', href: item.WatchUrl, target: '_blank', rel: 'noopener noreferrer',
            title: 'Where to watch on TMDB: ' + names, 'aria-label': 'Where to watch on TMDB: ' + names
        }, shown.join(''));
    }

    return wrap('span', { 'class': 'cgSvcs' }, shown.join(''));
}

function renderRow(item) {
    var res = activeDismissal(item);

    var selBox = isMintable(item)
        ? h('input', { type: 'checkbox', 'class': 'cgSel', 'data-gapid': item.Id, title: 'Select to mint' }).outerHTML
        : h('span', { 'class': 'cgSelSpacer' }).outerHTML;

    var thumb = item.ImageUrl
        ? cachedImage({ 'class': 'cgThumb' }, item.ImageUrl)
        : h('span', { 'class': 'cgThumb cgThumbEmpty' }).outerHTML;

    // Meta: the year as a <time> (a real point in time), the target kind, and an upcoming/announced
    // badge when the release has not happened yet.
    var metaParts = [];
    if (item.Year) { metaParts.push(h('time', { datetime: String(item.Year) }, item.Year).outerHTML); }
    metaParts.push(esc(item.TargetKindName));
    if (item.IsUpcoming) {
        metaParts.push(item.ReleaseDate
            ? h('span', { style: 'color:#f0ad4e;', title: 'Not released yet.' }, 'Upcoming').outerHTML
            : h('span', { style: 'color:#f0ad4e;', title: 'Announced, with no release date yet.' }, 'Announced').outerHTML);
    }

    // Non-compact view always has a detail to show (Information and Actions fold into it there, so
    // even a row with no overview/watch/recommendation content still has its Diagnose/TODO/etc.);
    // compact view keeps those behind their own icons, so it only needs one when there is real
    // overview/watch/recommendation content to show.
    var watchableKind = item.TargetKindName === 'Movie' || item.TargetKindName === 'Series' || item.TargetKindName === 'Episode';
    var compact = !!reportPage()._compact;
    var hasDetail = !compact || !!item.HasOverview || !!item.Overview || watchableKind || (item.PatternName === 'Recommendation' && (item.OtherSources || []).length > 0);
    var overview = hasDetail ? wrap('div', { 'class': 'cgTitleDetail' }, '') : '';

    var iconsHtml = wrap('span', { 'class': 'cgIcons' },
        wrap('details', { 'class': 'cgPop', 'data-pop': 'watch' },
            wrap('summary', { title: 'Where to watch', 'aria-label': 'Where to watch' }, icon('play_arrow'))
            + wrap('div', { 'class': 'cgPopBody' }, ''))
        + wrap('details', { 'class': 'cgPop', 'data-pop': 'info' },
            wrap('summary', { title: 'Information', 'aria-label': 'Information' }, icon('info'))
            + wrap('div', { 'class': 'cgPopBody' }, ''))
        + wrap('details', { 'class': 'cgPop', 'data-pop': 'actions' },
            wrap('summary', { title: 'Actions', 'aria-label': 'Actions' }, icon('more_vert'))
            + wrap('div', { 'class': 'cgPopBody' }, '')));

    return wrap('div', {
        'class': 'listItem cgRow', 'data-gapid': item.Id,
        title: res ? 'Resolved: ' + dismissalLabel(res) : null,
        style: res ? 'opacity:.55;' : ''
    },
        selBox
        + thumb
        + wrap('h3', { 'class': 'cgTitle', tabindex: '0', title: item.Overview || null }, esc(item.Name))
        + wrap('span', { 'class': 'cgMeta' }, metaParts.join(' &middot; '))
        + serviceIcons(item)
        + iconsHtml
        + overview);
}

function groupBy(items, keyFn) {
    var map = {}, order = [];
    items.forEach(function (it) {
        var k = keyFn(it);
        if (!map[k]) { map[k] = []; order.push(k); }
        map[k].push(it);
    });
    return { map: map, order: order };
}

// Link to an item in this Jellyfin instance, opened in a new tab. Built from the current
// page URL so it works whatever the web root is.
function itemUrl(id) {
    return window.location.href.split('#')[0] + '#/details?id=' + encodeURIComponent(id) + '&serverId=' + encodeURIComponent(ApiClient.serverId());
}

// A small "open in Jellyfin" icon for items we already hold (a series, season, or virtual
// episode). Empty string when there is nothing to link to. The cgOpen class lets the group
// header click ignore it so it does not toggle the collapse.
function openIcon(id) {
    if (!id) { return ''; }
    return ' ' + jellyfinLink(
        { 'class': 'cgLink cgOpen emby-button', href: itemUrl(id), title: 'Open in Jellyfin', 'aria-label': 'Open in Jellyfin' },
        icon('open_in_new'));
}

