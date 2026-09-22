// Report page, part 2: the "Diagnose" popup and its per-gap Markdown export.

// The "Diagnose" popup: ask the server why a movie/show is reported missing (usually an owned
// item carrying a different or missing provider id) and show the plain-language findings.
// rawName is the decoded title from the button's data-name attribute (getAttribute undoes the
// esc() that made the attribute safe), so it is plain text. Only assign it via textContent,
// which re-escapes; never drop it into innerHTML.
function openDiagnose(gapId, rawName, deeper) {
    if (!gapId) { return; }
    var modal = document.getElementById('cgDiagModal');
    var body = document.getElementById('cgDiagBody');
    // Remember the gap so the in-modal "Deeper analysis" button can re-run it.
    modal._gapId = gapId;
    modal._name = rawName || '';
    document.getElementById('cgDiagTitle').textContent = rawName ? ('Why is “' + rawName + '” missing?') : 'Diagnose';
    body.innerHTML = h('p', { 'class': 'fieldDescription' }, deeper ? 'Confirming…' : 'Checking your library…').outerHTML;
    modal.style.display = 'flex';
    var args = { id: gapId };
    if (deeper) { args.deeper = true; }
    ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('MindTheGaps/Diagnose', args), dataType: 'json' })
        .then(function (res) {
            // A deep-link may not carry the title, so fill it from the result.
            if (res && res.Target && res.Target.Name) {
                modal._name = res.Target.Name;
                document.getElementById('cgDiagTitle').textContent = 'Why is “' + res.Target.Name + '” missing?';
            }
            // Keep the diagnosis so the Export for AI analysis button can serialize it.
            modal._res = res;
            body.innerHTML = renderDiagnosis(res);
        })
        .catch(function () { body.innerHTML = h('p', { 'class': 'fieldDescription' }, 'Could not run the diagnosis. Check the server logs.').outerHTML; });
}

function closeDiagnose() {
    var modal = document.getElementById('cgDiagModal');
    if (modal && modal.style.display !== 'none') {
        modal.style.display = 'none';
        document.getElementById('cgDiagBody').innerHTML = '';
    }
}

// The identity providers the diagnosis compares, in column order, with display labels. This is
// presentation only: which ids are stored is provider-agnostic (DiagnosisItem.ProviderIds), and
// the URLs come from the server (DiagnosisItem.Links). An unlisted provider just is not columned.
var DIAG_PROVIDER_ORDER = ['tmdb', 'imdb', 'tvdb', 'musicbrainz', 'openlibrary'];
var DIAG_PROVIDER_LABEL = { tmdb: 'TheMovieDb', imdb: 'IMDb', tvdb: 'TheTVDB', musicbrainz: 'MusicBrainz', openlibrary: 'OpenLibrary' };

// The verdict badge the modal leads with, keyed by GapDiagnosis.ReasonName.
var DIAG_REASON = {
    NotOwned: { label: 'Genuinely missing', color: '#888' },
    OwnedUnderWrongId: { label: 'Owned under the wrong id', color: '#d39e00' },
    CarriesAnothersId: { label: 'An owned item holds this id', color: '#d39e00' },
    Stale: { label: 'Analysis looks stale', color: '#4aa3df' },
    WrongIdClass: { label: 'Wrong kind of id', color: '#d39e00' }
};

// Normalize a ProviderIds key or a server link Name to one comparable token, so an id and its
// link line up ("Tvdb"/"TheTVDB" -> tvdb, "MusicBrainzReleaseGroup"/"MusicBrainz" -> musicbrainz).
function diagToken(s) {
    var t = (s || '').toLowerCase();
    if (t === 'thetvdb') { return 'tvdb'; }
    if (t.indexOf('musicbrainz') === 0) { return 'musicbrainz'; }
    return t;
}

// A row's ids keyed by token: { id (from ProviderIds), url (from the server-built Links) }.
function diagCells(item) {
    var out = {};
    var ids = item.ProviderIds || {};
    Object.keys(ids).forEach(function (k) {
        if (!ids[k]) { return; }
        var tok = diagToken(k);
        if (!out[tok]) { out[tok] = {}; }
        if (out[tok].id == null) { out[tok].id = ids[k]; }
    });
    (item.Links || []).forEach(function (l) {
        var tok = diagToken(l.Name);
        if (out[tok] && out[tok].url == null) { out[tok].url = l.Url; out[tok].name = l.Name; }
    });
    return out;
}

// The identity columns present across the given rows, in canonical order.
function diagColumns(rows) {
    var present = {};
    rows.forEach(function (r) {
        var c = diagCells(r);
        Object.keys(c).forEach(function (t) { present[t] = true; });
    });
    return DIAG_PROVIDER_ORDER.filter(function (t) { return present[t]; });
}

// One id cell both the modal table and the audit Markdown render from: the id linked to its
// provider page, or the absent marker. asMarkdown picks the output flavour.
function diagCell(cell, asMarkdown) {
    if (!cell || cell.id == null) { return asMarkdown ? '' : h('span', { 'class': 'cgDiagMissing' }, '-').outerHTML; }
    if (asMarkdown) { return cell.url ? '[' + cell.id + '](' + cell.url + ')' : String(cell.id); }
    return cell.url
        ? newTab(false, {
            'class': providerClass(cell.name).trim(), 'data-provider': cell.name || '',
            title: 'Open ' + cell.id + ' on ' + (cell.name || ''), 'aria-label': 'Open ' + cell.id + ' on ' + (cell.name || ''),
            href: cell.url
        }, esc(cell.id))
        : esc(cell.id);
}

function renderDiagnosis(res) {
    res = res || {};
    var rows = [];
    if (res.Target) { rows.push(res.Target); }
    (res.Candidates || []).forEach(function (c) { rows.push(c); });

    var verdict = DIAG_REASON[res.ReasonName];
    // The verdict badge keeps its provider color inline (it is data-driven, one value per reason).
    var html = verdict
        ? wrap('div', { 'class': 'cgDiagVerdict' }, h('span', { 'class': 'cgDiagBadge', style: 'background:' + verdict.color + ';' }, verdict.label).outerHTML)
        : '';
    html += h('p', { 'class': 'cgDiagSummary' }, res.Summary || '').outerHTML;
    if (rows.length) {
        var cols = diagColumns(rows);
        var th = function (t) { return h('th', null, t).outerHTML; };
        var head = wrap('thead', null, wrap('tr', null,
            th('Title') + th('Year')
            + cols.map(function (t) { return th(DIAG_PROVIDER_LABEL[t] || t); }).join('')
            + th('In library')));
        var bodyRows = rows.map(function (r) {
            var isTarget = r.Relation === 'target';
            var inLib = isTarget
                ? h('span', { 'class': 'cgDiagMissingLabel' }, 'Missing').outerHTML
                : (r.JellyfinItemId ? newTab(false, { href: itemUrl(r.JellyfinItemId) }, 'Open') : 'Owned');
            var cells = diagCells(r);
            var td = function (c) { return wrap('td', null, c); };
            var nameCell = h('b', null, r.Name || '').outerHTML
                + (r.Note ? h('br').outerHTML + h('span', { 'class': 'cgDiagNote' }, r.Note).outerHTML : '');
            return wrap('tr', { 'class': isTarget ? 'cgDiagTarget' : null },
                td(nameCell)
                + td(r.Year ? esc(r.Year) : h('span', { 'class': 'cgDiagMissing' }, '-').outerHTML)
                + cols.map(function (t) { return td(diagCell(cells[t], false)); }).join('')
                + td(inLib));
        }).join('');
        html += wrap('table', { 'class': 'cgDiagTable' }, head + wrap('tbody', null, bodyRows));
    }
    // The footer offers the networked confirmation (until it has run) and always the AI-export
    // button, which downloads this diagnosis as a Markdown dossier plus a prompt.
    var footer = (res.Deepened
        ? h('span', { 'class': 'cgDiagDone' }, 'Confirmed. ').outerHTML
        : h('button', {
            is: 'emby-button', type: 'button', 'class': 'raised cgDeepen',
            title: 'Resolve ids against the source provider to confirm the verdict and catch matches your local metadata missed.'
        }, 'Deeper analysis').outerHTML)
        + h('button', {
            is: 'emby-button', type: 'button', 'class': 'raised cgDiagExport',
            title: 'Download this diagnosis as Markdown with an AI prompt, so any AI can analyze why the match failed.'
        }, 'Export for AI analysis').outerHTML;
    html += wrap('div', { 'class': 'cgDiagDeepenWrap' }, footer);
    return html;
}

// The AI prompt the diagnosis export leads with: it frames the matching rules and asks the
// reader (any AI) to judge why the title was not matched. Kept as paragraphs so the Markdown
// stays readable; Marc feeds the answers back into the matching rules.
var DIAG_AI_PROMPT = [
    'You are a media metadata identification expert helping debug a Jellyfin library tool called Mind the Gaps. The tool compares a media library against external catalogs (TheMovieDb, TheTVDB, TVmaze, MusicBrainz, OpenLibrary) to list the titles a library is missing. It treats a catalog title as already owned only when a library item carries a matching provider id. For movies and shows the primary key is the TheMovieDb id, with the IMDb and TheTVDB ids as corroborating secondary keys; for an album the primary key is the MusicBrainz release-group id and for a book the OpenLibrary work id; for an episode it first checks whether the season and episode number is among the ones the library owns for the series, then whether an episode with the same title is owned at a different number (a two-part or off-by-one mismatch), and only then falls back to comparing the air year against the owned run. For movies, shows, albums, and books a normalized title-and-year comparison is used only inside this diagnosis, never by the live ownership check; for episodes the live scan itself reconciles a candidate against the owned episodes by air date and folded title before reporting it missing, so a season a catalog numbers differently from the library (a renumber, a reorder, or a two-part episode kept as one file) is recognized as owned.',
    'The item described below was reported missing, meaning the ownership check found no library item with a matching id. Work out why, and whether that verdict is correct.',
    'Weigh these explanations and choose the most likely: (1) genuinely not owned; (2) owned under a different or absent TheMovieDb id, a metadata mismatch the id-keyed check cannot see; (3) a title localization or punctuation difference that hid a real match; (4) a same-title, different-year clash such as a remake or reboot that should not count as owned; (5) a limitation or bug in the matching rules above.',
    'Use the provider ids, the owned candidates, and the plugin verdict below. Then answer concisely: the most likely reason and your confidence (low, medium, or high); the single signal or rule that would have caught it correctly (for example, match on a shared IMDb id even when the TheMovieDb id differs, or treat a large air-year gap as a different series); any data the plugin did not collect but should have; and whether the plugin verdict is right, and if not, what it got wrong.'
];

// A plain-language recap of the matching rules, included in the export so the reader judges the
// verdict against how ownership is actually decided.
var DIAG_MATCH_EXPLAINER = [
    'Ownership is an exact provider-id match. An owned movie or show is indexed by its TheMovieDb id (primary) and by its IMDb and TheTVDB ids (secondary), an owned album by its MusicBrainz release-group id, and an owned book by its OpenLibrary work id. A gap is a catalog title whose ids match no owned item.',
    'This diagnosis additionally compares normalized title and year to surface near-misses the live check ignores: an owned item with the same title but a different or missing id is the classic metadata mismatch, while a same title more than one year apart is treated as a different release.',
    'For an episode or season, ownership is not id-matched. For an episode the diagnosis first checks whether the season and episode number is among the ones the library owns for the series: if it is, the episode is not actually missing (a stale gap, or a numbering the cross-check disagrees on). If not, it checks whether an episode with the same title (ignoring a part marker like (2) or Part 2) is owned at another number, which means the content is present but numbered differently than the catalog (a two-part episode, or the pilot counted as one episode rather than two). Otherwise it compares the missing air year against the earliest and latest years of the episodes already owned, so a late season reads as missing while a reboot decades apart reads as a different, same-named series the owning item is mis-tagged as. The live scan applies the same air-date and folded-title reconciliation, so a renumbered or merged episode is recognized as owned during the scan, not only in this diagnosis.'
];

// A library item's ids in a readable, provider-labelled list, for the export's plain text.
function describeProviderIds(ids) {
    ids = ids || {};
    var order = [['Tmdb', 'TheMovieDb'], ['Imdb', 'IMDb'], ['Tvdb', 'TheTVDB'], ['TVmaze', 'TVmaze'], ['MusicBrainzReleaseGroup', 'MusicBrainz'], ['OpenLibrary', 'OpenLibrary']];
    var parts = [];
    order.forEach(function (pair) { if (ids[pair[0]]) { parts.push(pair[1] + ' ' + ids[pair[0]]); } });
    Object.keys(ids).forEach(function (k) {
        var known = order.some(function (p) { return p[0] === k; });
        if (ids[k] && !known) { parts.push(k + ' ' + ids[k]); }
    });
    return parts.length ? parts.join(', ') : 'none';
}

// Make a value safe to drop into a Markdown table cell (escape the pipe, flatten newlines).
function mdCell(s) {
    return String(s == null ? '' : s).replace(/\|/g, '\\|').replace(/\r?\n/g, ' ');
}

// A filesystem-safe download name for one diagnosis, by title. Built with concatenation, not a
// template literal, which the page server would mangle.
function diagFilename(res, fallbackName) {
    var name = (res && res.Target && res.Target.Name) || fallbackName || 'item';
    var slug = name.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 60);
    return 'mind-the-gaps-diagnosis-' + (slug || 'item') + '.md';
}

// Serialize one diagnosis as a Markdown dossier: an AI prompt, the missing item, how the matcher
// decides, the verdict, the owned candidates, and the raw data. Marc gives this to any AI to
// analyze why the match failed, then feeds the analysis back into the matching rules.
function buildDiagnosisMarkdown(res, fallbackName) {
    res = res || {};
    var target = res.Target || {};
    var title = target.Name || fallbackName || 'this title';
    var year = target.Year;
    var reason = DIAG_REASON[res.ReasonName];
    var verdictLabel = (reason && reason.label) || res.ReasonName || 'unknown';

    var L = [];
    L.push('# Why was "' + title + '"' + (year ? ' (' + year + ')' : '') + ' not matched?');
    L.push('');
    L.push('## Task for the AI');
    L.push('');
    DIAG_AI_PROMPT.forEach(function (p) { L.push(p); L.push(''); });

    L.push('## The item reported missing');
    L.push('');
    L.push('- Title: ' + title);
    L.push('- Year: ' + (year || 'unknown'));
    L.push('- Kind: ' + (res.TargetKindName || 'unknown'));
    L.push('- Provider ids: ' + describeProviderIds(target.ProviderIds));
    L.push('');

    L.push('## How Mind the Gaps decides ownership');
    L.push('');
    DIAG_MATCH_EXPLAINER.forEach(function (p) { L.push(p); L.push(''); });

    L.push('## The plugin verdict');
    L.push('');
    L.push('- Verdict: ' + verdictLabel);
    L.push('- Pass: ' + (res.Deepened ? 'deeper (ids resolved against the source provider over the network)' : 'library-only (no network lookup)'));
    L.push('- Summary: ' + (res.Summary || ''));
    L.push('');

    L.push('## What the library holds that resembles it');
    L.push('');
    if (!res.Candidates || !res.Candidates.length) {
        L.push('No owned item matched by title or id, so the plugin treated it as genuinely missing.');
        L.push('');
    } else {
        var rows = [res.Target];
        res.Candidates.forEach(function (c) { rows.push(c); });
        var cols = diagColumns(rows);
        var head = ['Relation', 'Title', 'Year'].concat(cols.map(function (t) { return DIAG_PROVIDER_LABEL[t] || t; })).concat(['In library', 'Plugin note']);
        L.push('| ' + head.join(' | ') + ' |');
        L.push('|' + head.map(function () { return ' --- '; }).join('|') + '|');
        rows.forEach(function (r) {
            if (!r) { return; }
            var cells = diagCells(r);
            var isTarget = r.Relation === 'target';
            var idCols = cols.map(function (t) { return diagCell(cells[t], true) || '-'; });
            var line = [isTarget ? 'missing (target)' : (r.Relation || 'owned'), mdCell(r.Name), (r.Year || '-')]
                .concat(idCols)
                .concat([isTarget ? 'missing' : 'owned', mdCell(r.Note)]);
            L.push('| ' + line.join(' | ') + ' |');
        });
        L.push('');
    }

    L.push('## Raw diagnosis data');
    L.push('');
    L.push('```json');
    L.push(JSON.stringify(res, null, 2));
    L.push('```');
    return L.join('\n');
}

