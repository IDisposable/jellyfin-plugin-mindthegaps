// Report page, part 5: the report's own Markdown export (per-tab checklist) and the Diagnose
// audit's Markdown export.

// Escape the markdown control characters so a title cannot break the output.
function mdEsc(s) { return (s == null ? '' : String(s)).replace(/([\\`*_[\]()<>#|])/g, '\\$1'); }

// Like mdEsc but for heading text: parentheses are left alone (they are safe in a heading, and
// escaping them shows literal backslashes in some renderers, which also breaks the anchor).
function mdHeading(s) { return (s == null ? '' : String(s)).replace(/([\\`*_[\]<>#|])/g, '\\$1'); }

// A gap's H2 heading text (no leading "##"): the linked title and its year/kind.
function gapHeading(it) {
    var links = it.Links || [];
    var title = links.length ? '[' + mdEsc(it.Name) + '](' + safeUrl(links[0].Url) + ')' : mdEsc(it.Name);
    var meta = [];
    if (it.Year) { meta.push(it.Year); }
    if (it.TargetKindName) { meta.push(it.TargetKindName); }
    return title + (meta.length ? ' (' + meta.join(', ') + ')' : '');
}

// The detail line under a gap heading: its provider links, a linked "Watch" (to the same watch
// page the UI opens) with the providers, an open-in-Jellyfin link for held items, the owning
// source (collection, creator, or recommending title), and a dismissal note. Empty when none.
function gapDetail(it) {
    var parts = [];
    var links = it.Links || [];
    if (links.length) { parts.push(links.map(function (l) { return '[' + mdEsc(l.Name) + '](' + safeUrl(l.Url) + ')'; }).join(' ')); }
    // A search link back to this server, scoped like the report's search icon (an episode
    // searches its series). Labelled with the server name (or "Jellyfin"), led by a magnifying
    // glass (U+1F50D); the surrogate-pair escape keeps this file ASCII. The encoded query uses
    // "+" for spaces and has no whitespace or closing bracket, so no angle-bracket wrap is needed.
    var sName = (it.TargetKindName === 'Episode' && it.SourceItemName) ? it.SourceItemName : it.Name;
    var sScope = it.TargetKindName === 'Episode' ? 'tvshows' : domainScope(it.DomainName);
    parts.push('[\uD83D\uDD0D ' + mdEsc(cgServerName || 'Jellyfin') + '](' + searchUrl(sName, sScope) + ')');
    var offers = filterOffers(it.Availability);
    if (offers.length) {
        var provs = offers.map(function (o) { return o.Provider; }).filter(function (p, i, a) { return p && a.indexOf(p) === i; });
        var watchUrl = '';
        for (var i = 0; i < offers.length; i++) { if (offers[i].Url) { watchUrl = offers[i].Url; break; } }
        var watch = watchUrl ? '[Watch](' + safeUrl(watchUrl) + ')' : 'Watch';
        parts.push(watch + (provs.length ? ': ' + provs.map(mdEsc).join(', ') : ''));
    }
    if (it.LibraryItemId) { parts.push('[\uD83D\uDD17 Open in Jellyfin](' + itemUrl(it.LibraryItemId) + ')'); }
    if (it.SourceItemName) { parts.push('from ' + mdEsc(it.SourceItemName)); }
    var res = activeDismissal(it);
    if (res) {
        var lbl = res.Kind === 'notinterested' ? 'not interested' : (res.Kind === 'snoozed' ? 'snoozed' : 'resolved');
        parts.push('_(' + lbl + (res.Note ? ': ' + mdEsc(res.Note) : '') + ')_');
    }
    return parts.join(' | ');
}

// Build a markdown document for the current tab as filtered. One H1 per domain (the axis, the
// domain folded into the title "Mind the Gaps: Movies Set completion"), an H2 per source group
// (the set's kind, the discovery kind, or the creator), and each gap as an H3 with a detail
// line. A table of contents jumps to each group. The summary line links to a shareable view.
// Everything the export writes: the current tab under the current filters, across every letter. Note
// this is deliberately wider than page._shown, which the A-Z bar narrows to one letter on a big tab.
// The verify that runs before an export uses this too, so it cannot check less than it writes.
function exportItems(page) {
    var report = page._report || { Items: [] };
    var pass = buildFilter(page);
    return (report.Items || []).filter(function (it) { return it.PatternName === page._pattern && pass(it); });
}

// The export writes every link and offer, which the list rows do not carry. One request for the tab's
// full gaps, merged into the rows already held so the on-screen objects stay the ones the export reads.
function ensureFullForExport(page) {
    var items = exportItems(page).filter(function (it) { return !it._full; });
    if (!items.length) { return Promise.resolve(); }
    var query = { pattern: page._pattern, full: 'true' };
    if (page._domain) { query.domain = page._domain; }
    return ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('MindTheGaps/Gaps', query), dataType: 'json' })
        .then(function (full) {
            var byId = {};
            (full.Items || []).forEach(function (g) { byId[g.Id] = g; });
            items.forEach(function (it) {
                if (byId[it.Id]) {
                    Object.assign(it, byId[it.Id]);
                    it._full = true;
                }
            });
        });
}

function buildMarkdown(page) {
    var items = exportItems(page);
    // Angle brackets around the URL so encoded filter values cannot break the markdown link.
    var out = ['_[' + items.length + ' gaps, exported ' + new Date().toLocaleString() + '](<' + shareUrl(page) + '>)_', ''];

    // Allocate heading anchors in the order the headings appear, so the contents links resolve.
    var anchorFor = anchorAllocator();

    var byCat = groupBy(items, categoryOf);
    byCat.order.sort(domainCompare());
    var groupSort = exportGroupSort(page._pattern);

    // Model the document (domains -> source groups) and allocate anchors in render order.
    var sections = byCat.order.map(function (cat) {
        // The pattern label is per domain, so a Shows section reads "Shows Series completion",
        // music "Music Discography", and so on, regardless of the Type filter's current value.
        var catLabel = patternLabel(page._pattern, cat);
        var heading = 'Mind the Gaps: ' + cat + ' ' + catLabel;
        var byGroup = groupBy(byCat.map[cat], exportGroupLabel);
        byGroup.order.sort(groupSort);
        return {
            cat: cat,
            catLabel: catLabel,
            heading: heading,
            anchor: anchorFor(heading),
            groups: byGroup.order.map(function (g) {
                return { label: g, anchor: anchorFor(g), items: byGroup.map[g] };
            })
        };
    });

    // Table of contents, skipped when there is only one group to jump to.
    var groupCount = sections.reduce(function (n, s) { return n + s.groups.length; }, 0);
    if (groupCount > 1) {
        out.push('## Contents', '');
        sections.forEach(function (s) {
            out.push('- [' + mdHeading(s.cat + ' ' + s.catLabel) + '](#' + s.anchor + ')');
            s.groups.forEach(function (g) {
                out.push('    - [' + mdHeading(g.label) + '](#' + g.anchor + ')');
            });
        });
        out.push('');
    }

    // Sort by source then title so one set's gaps stay adjacent and read alphabetically.
    var byTitle = function (a, b) {
        var src = ci(a.SourceItemName || '', b.SourceItemName || '');
        return src !== 0 ? src : ci(a.Name || '', b.Name || '');
    };
    var emitGap = function (it) {
        out.push('### ' + gapHeading(it));
        var detail = gapDetail(it);
        if (detail) { out.push(detail); }
        out.push('');
    };
    // Collapse each set's gaps in a native <details>: <details>/<summary> with blank lines around
    // the body is the no-JS collapsible that GitHub, VS Code, and most Markdown viewers render.
    // The domain and group stay real headings so the contents links still resolve.
    var openDetails = function (summary) { out.push('<details>', '<summary>' + summary + '</summary>', ''); };
    var closeDetails = function () { out.push('</details>', ''); };
    // Set completion and Discover are both two-axis on screen (a kind heading over the individual sets or
    // lists), so the document is too; Creator works is one creator per H2 and needs no inner axis.
    var twoAxis = page._pattern === 'SetCompletion' || page._pattern === 'Recommendation';
    sections.forEach(function (s) {
        out.push('# ' + mdHeading(s.heading), '');
        s.groups.forEach(function (g) {
            out.push('## ' + mdHeading(g.label), '');
            if (twoAxis) {
                // Two axes: the kind (the H2) and the individual set or list; each collapses on its own.
                var bySource = groupBy(g.items, function (it) { return it.SourceItemName || '(no source)'; });
                bySource.order.sort(ci);
                bySource.order.forEach(function (src) {
                    var rows = bySource.map[src].slice().sort(byTitle);
                    // For a series, include the show's year so same-named reboots read apart in the
                    // export the way they do in the report header.
                    var episodic = rows.some(function (it) { return it.Season != null; });
                    var name = (episodic && rows[0].SourceItemYear) ? src + ' (' + rows[0].SourceItemYear + ')' : src;
                    openDetails(esc(name) + ' (' + rows.length + ')');
                    rows.forEach(emitGap);
                    closeDetails();
                });
            } else {
                var rows = g.items.slice().sort(byTitle);
                openDetails(rows.length === 1 ? '1 item' : rows.length + ' items');
                rows.forEach(emitGap);
                closeDetails();
            }
        });
    });
    return out.join('\n');
}

// An absolute link back into this dashboard that opens a gap's diagnosis and runs the deeper
// pass (cgdiag/cgdeep, consumed on load by consumeUrlDiag).
function diagDeepLink(gapId) {
    var base = window.location.href.split('#')[0];
    return base + '#/configurationpage?name=MindTheGaps&cgdiag=' + encodeURIComponent(gapId) + '&cgdeep=1';
}

// The mismatch reasons the audit reports, in display order, with the one-line intro each
// section leads with. The labels come from DIAG_REASON (shared with the modal badges).
var AUDIT_REASON_ORDER = ['OwnedUnderWrongId', 'CarriesAnothersId'];
var AUDIT_REASON_INTRO = {
    OwnedUnderWrongId: 'Reported missing, but you appear to own these under a different (or missing) id.',
    CarriesAnothersId: 'An owned item carries this id but sits under a different title, so it is misidentified.'
};

// Format a library identification audit (from MindTheGaps/DiagnoseAudit) as Markdown: a section
// per reason (owned under the wrong id, an owned item holds this id) plus the duplicate-id
// section, each id linked out, and a table of contents to jump between them.
function buildAuditMarkdown(audit) {
    audit = audit || {};
    // Same id links as the modal, from the shared map-driven cells, in Markdown form.
    var ids = function (it) {
        var cells = diagCells(it);
        var parts = DIAG_PROVIDER_ORDER.filter(function (t) { return cells[t] && cells[t].id != null; })
            .map(function (t) { return (DIAG_PROVIDER_LABEL[t] || t) + ' ' + diagCell(cells[t], true); });
        return parts.length ? parts.join(', ') : '(no ids)';
    };
    // Open-in-Jellyfin link with a link glyph (U+1F517, the surrogate-pair escape keeps this
    // file ASCII). No angle-bracket wrap: the item url has no spaces, and the wrap renders
    // literally in some viewers.
    var jf = function (it) { return it.JellyfinItemId ? ' - [\uD83D\uDD17 open in Jellyfin](' + itemUrl(it.JellyfinItemId) + ')' : ''; };
    var yr = function (it) { return it.Year ? ' (' + it.Year + ')' : ''; };

    // A native collapsible: <details>/<summary> with blank lines around the body, the no-JS
    // collapsible that GitHub, VS Code, and most Markdown viewers render. Section headings stay
    // real headings so the contents links still resolve.
    var detailsBlock = function (summary, bodyLines) {
        return ['<details>', '<summary>' + summary + '</summary>', ''].concat(bodyLines, ['', '</details>', '']);
    };

    // One mismatch finding as a collapsible: the missing title is the summary; the expected ids,
    // owned candidates, fix hint, and deeper link are the body.
    var mismatchDetails = function (d) {
        var t = d.Target || {};
        var summary = '<strong>' + esc(t.Name || '') + esc(yr(t)) + '</strong> - missing';
        var lines = ['Expected ' + ids(t) + '.', ''];
        (d.Candidates || []).forEach(function (c) {
            lines.push('- Owned as "' + mdEsc(c.Name || '') + '"' + yr(c) + ': ' + ids(c) + jf(c) + (c.Note ? ' - _' + mdEsc(c.Note) + '_' : ''));
        });
        var tTmdb = t.ProviderIds && t.ProviderIds.Tmdb;
        if (tTmdb) { lines.push('- Fix: set the owned item to TheMovieDb ' + tTmdb + ', then rescan.'); }
        // The label is a magnifying glass (U+1F50D) plus "Deeper"; the surrogate-pair escape
        // keeps this file ASCII while the exported Markdown still shows the glyph.
        if (d.GapId) { lines.push('- [\uD83D\uDD0D Deeper](' + diagDeepLink(d.GapId) + ')'); }
        return detailsBlock(summary, lines);
    };

    // Build the sections (heading + body lines). A reason section appears only when it has
    // findings; with no mismatches at all, a single reassuring "none found" section stands in.
    var sections = [];
    var mismatches = audit.Mismatches || [];
    if (!mismatches.length) {
        sections.push({ heading: 'Likely false "missing"', body: ['None found: every checked gap looks like a genuine miss.', ''] });
    } else {
        var byReason = {};
        mismatches.forEach(function (d) { (byReason[d.ReasonName] = byReason[d.ReasonName] || []).push(d); });
        AUDIT_REASON_ORDER.forEach(function (r) {
            var list = byReason[r];
            if (!list || !list.length) { return; }
            var label = (DIAG_REASON[r] && DIAG_REASON[r].label) || r;
            // The count goes in the body, not the heading: parentheses in a heading get escaped
            // and break the contents anchors.
            var body = ['**' + list.length + '** ' + (AUDIT_REASON_INTRO[r] || ''), ''];
            list.forEach(function (d) { body = body.concat(mismatchDetails(d)); });
            sections.push({ heading: label, body: body });
        });
    }

    var dups = audit.Duplicates || [];
    var dupBody = [];
    if (!dups.length) {
        dupBody.push('None found: no two owned items share a TheMovieDb id.', '');
    } else {
        dupBody.push('**' + dups.length + '** Each id below is on more than one owned item, so at least one is misidentified.', '');
        dups.forEach(function (g) {
            var tmdbCell = diagCells((g.Items || [])[0] || {}).tmdb;
            var dl = tmdbCell && tmdbCell.url;
            var n = (g.Items || []).length;
            var lines = [];
            if (dl) { lines.push('[Open TheMovieDb ' + g.Id + '](' + dl + ')', ''); }
            (g.Items || []).forEach(function (c) { lines.push('- "' + mdEsc(c.Name || '') + '"' + yr(c) + jf(c)); });
            dupBody = dupBody.concat(detailsBlock('TheMovieDb ' + esc(String(g.Id)) + ' on ' + n + ' items', lines));
        });
    }
    sections.push({ heading: 'Duplicate TheMovieDb ids', body: dupBody });

    // Series whose episodes are split or duplicated across two folders that map to the same season
    // number (a "Season 1" and a "Season 01"). Each folder's path and episode count is listed so the
    // reader can see which copy to keep.
    var dupSeasons = audit.DuplicateSeasons || [];
    var dsBody = [];
    if (!dupSeasons.length) {
        dsBody.push('None found: no series has the same season number in more than one folder.', '');
    } else {
        dsBody.push('**' + dupSeasons.length + '** Each series below holds one season number in more than one folder (for example a "Season 1" and a "Season 01"), so that season is duplicated or its episodes are scattered. Merge the folders into one, then rescan.', '');
        dupSeasons.forEach(function (g) {
            var lines = [];
            (g.Folders || []).forEach(function (f) {
                var where = f.Path ? ' `' + f.Path + '`' : '';
                lines.push('- "' + mdEsc(f.Name || '') + '"' + where + ': ' + (f.EpisodeCount || 0) + ' episode(s)' + jf(f));
            });
            var n = (g.Folders || []).length;
            dsBody = dsBody.concat(detailsBlock(esc(g.SeriesName || 'Series') + ' - season ' + g.SeasonNumber + ' in ' + n + ' folders', lines));
        });
    }
    sections.push({ heading: 'Duplicate season folders', body: dsBody });

    var anchorFor = anchorAllocator();
    sections.forEach(function (s) { s.anchor = anchorFor(s.heading); });

    var out = ['# Mind the Gaps: identification audit', ''];
    var scope = audit.DomainName
        ? audit.DomainName + (audit.PatternName ? ' / ' + patternLabel(audit.PatternName, audit.DomainName) : '')
        : 'all domains';
    var ownedParts = [];
    if (audit.OwnedMovies) { ownedParts.push(audit.OwnedMovies + ' movies'); }
    if (audit.OwnedShows) { ownedParts.push(audit.OwnedShows + ' shows'); }
    if (audit.OwnedAlbums) { ownedParts.push(audit.OwnedAlbums + ' albums'); }
    if (audit.OwnedBooks) { ownedParts.push(audit.OwnedBooks + ' books'); }
    var ownedStr = ownedParts.length ? ownedParts.join(', ') + ' scanned; ' : '';
    out.push('_Identification audit for ' + scope + ', from the gap scan on '
        + new Date(audit.GeneratedUtc).toLocaleString() + '. ' + ownedStr
        + (audit.GapsChecked || 0) + ' gaps checked._', '');

    if (sections.length > 1) {
        out.push('## Contents', '');
        sections.forEach(function (s) { out.push('- [' + mdEsc(s.heading) + '](#' + s.anchor + ')'); });
        out.push('');
    }

    sections.forEach(function (s) {
        out.push('## ' + mdEsc(s.heading), '');
        out = out.concat(s.body);
    });

    return out.join('\n');
}

function downloadText(filename, text) {
    var blob = new Blob([text], { type: 'text/markdown' });
    var url = URL.createObjectURL(blob);
    var a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    setTimeout(function () { URL.revokeObjectURL(url); }, 0);
}

