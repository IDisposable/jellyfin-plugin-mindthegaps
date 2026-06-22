
            (function () {
                var pluginId = '8c2a93cc-6cc5-493a-880a-2e67ae50e454';
                var PATTERNS = ['SetCompletion', 'CreatorWorks', 'Recommendation'];
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
                var CATEGORY_ORDER = { Movies: 0, Shows: 1, Music: 2, Books: 3 };
                var MONETIZATION_LABELS = { flatrate: 'Subscription', free: 'Free', ads: 'With ads', rent: 'Rent', buy: 'Buy' };

                function esc(s) {
                    return (s == null ? '' : String(s)).replace(/[&<>"']/g, function (c) {
                        return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
                    });
                }

                // Only allow http(s) link targets, so a javascript:/data: URL that slipped in from a third-party
                // API (TMDB watch page, a host link provider) cannot execute when rendered as an href. Anything
                // else collapses to '#'. The result is still passed through esc() at the call site.
                function safeUrl(u) {
                    var s = (u == null ? '' : String(u)).trim();
                    return /^https?:\/\//i.test(s) ? s : '#';
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

                // This server's display name, for labelling export links that point back to it. Loaded once on
                // pageshow; empty until then, so the label falls back to "Jellyfin".
                var cgServerName = '';

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
                        return ' <a class="cgLink cgRestoreCreator" data-gapid="' + esc(guid) + '" title="Scan this creator again">Restore</a>';
                    }
                    return ' <a class="cgLink cgDismissCreator" data-gapid="' + esc(guid) + '" data-name="' + esc(name || '') + '" title="Never delve into this creator (stop scanning and hide their gaps)">Not interested in creator</a>';
                }

                // Resolve-all / Not-interested-all controls for a series or season group header: they dismiss
                // every listed gap under the group in one batch. The click handler collects the ids from the
                // group's rows, so the label is only for the confirm prompt.
                function batchDismissBtns(label) {
                    return ' <a class="cgLink cgBatchResolve" data-label="' + esc(label) + '" title="Resolve all listed items here (not really missing)" aria-label="Resolve all listed items here (not really missing)"><span class="material-icons" aria-hidden="true" style="font-size:1.05em;vertical-align:middle;">done</span></a>'
                        + ' <a class="cgLink cgBatchNotInterested" data-label="' + esc(label) + '" title="Mark all listed items here as not interested" aria-label="Mark all listed items here as not interested"><span class="material-icons" aria-hidden="true" style="font-size:1.05em;vertical-align:middle;">close</span></a>';
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

                function providerAllowed(name) { return !disabledProviders[name]; }

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
                        toggles += '<a class="cgLink cgProvAll" title="Enable every provider" aria-label="Enable every provider"><span class="material-icons" aria-hidden="true" style="font-size:1.05em;vertical-align:middle;">done_all</span></a>';
                    }
                    if (enabledCount > 0) {
                        toggles += '<a class="cgLink cgProvNone" title="Disable every provider" aria-label="Disable every provider"><span class="material-icons" aria-hidden="true" style="font-size:1.05em;vertical-align:middle;">clear</span></a>';
                    }

                    // Collapsible: a header (with a caret and the enabled-of-total count) toggles the long list.
                    var header = '<span class="cgProvToggle" title="Show or hide the provider list">'
                        + '<span class="cgCaret" style="' + (providersExpanded ? 'transform:rotate(90deg);' : '') + '"></span>'
                        + 'Providers (' + enabledCount + ' of ' + total + ')</span> ' + toggles;
                    var list = '<div class="cgProvList" style="display:' + (providersExpanded ? 'flex' : 'none') + ';flex-wrap:wrap;margin-top:.25em;">'
                        + knownProviders.map(function (name) {
                            return '<label class="cgProvLabel"><input type="checkbox" class="cgProv" data-prov="' + esc(name) + '"'
                                + (disabledProviders[name] ? '' : ' checked') + ' /> ' + esc(name) + '</label>';
                        }).join('') + '</div>';
                    el.innerHTML = header + list;
                }

                // Add any newly-seen providers to the filter (default enabled) and persist.
                function noteProviders(page, offers) {
                    var added = false;
                    (offers || []).forEach(function (o) {
                        if (o.Provider && knownProviders.indexOf(o.Provider) === -1) { knownProviders.push(o.Provider); added = true; }
                    });
                    if (added) { knownProviders.sort(); renderProviderFilter(page); saveFilters(page); }
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
                        ? '<a is="emby-linkbutton" class="cgLink" href="' + esc(safeUrl(url)) + '" target="_blank" rel="noopener noreferrer" title="Open the watch page" aria-label="Open the watch page">Watch <span class="material-icons" aria-hidden="true" style="font-size:1.05em;vertical-align:middle;">open_in_new</span></a> '
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
                    var dismiss = id ? ' <a class="cgLink cgDismissRecSource" data-gapid="' + esc(id) + '" data-name="' + esc(name || '') + '" title="Stop recommendations from this title" aria-label="Stop recommendations from this title">&times;</a>' : '';
                    return esc(name) + suffix + openIcon(id) + dismiss;
                }

                // The dismiss control for a recommendation source, shown on its group header. Restoring a muted
                // source is done from the "Muted sources" picker, so a dismissed source shows no button here.
                function recSourceDismissBtn(guid, name) {
                    if (!guid || recSourceDismissed(guid)) { return ''; }
                    return ' <a class="cgLink cgDismissRecSource" data-gapid="' + esc(guid) + '" data-name="' + esc(name || '') + '" title="Stop recommendations from this source" aria-label="Stop recommendations from this source">&times;</a>';
                }

                function renderRow(item) {
                    var meta = [];
                    if (item.Year) { meta.push(item.Year); }
                    meta.push(esc(item.TargetKindName));
                    if (item.IsUpcoming) { meta.push('<span style="color:#f0ad4e;">Upcoming</span>'); }

                    var providerLinks = (item.Links || []).map(function (l) {
                        return '<a is="emby-linkbutton" class="cgLink' + providerClass(l.Name) + '" data-provider="' + esc(l.Name) + '" title="Open on ' + esc(l.Name) + '" aria-label="Open on ' + esc(l.Name) + '" href="' + esc(safeUrl(l.Url)) + '" target="_blank" rel="noopener noreferrer">' + esc(l.Name) + '</a>';
                    }).join('');

                    var tmdb = item.ProviderIds && item.ProviderIds.Tmdb;
                    // "Where to watch" looks up the title itself for a movie/series, or the owning series
                    // for an episode (episodes have no streaming page of their own).
                    var watchTmdb = item.WatchTmdbId || tmdb;
                    var watchKind = item.TargetKindName === 'Episode' ? 'Series' : item.TargetKindName;
                    var watchable = !!watchTmdb && (item.TargetKindName === 'Movie' || item.TargetKindName === 'Series' || item.TargetKindName === 'Episode');
                    var hasAvail = item.Availability && item.Availability.length;
                    var res = activeDismissal(item);
                    var actions = [];
                    // Offer the on-demand look-up only when we have not looked yet. Once checked, an empty
                    // result is shown as "no sources" instead (below), not a button that comes back empty.
                    if (watchable && !hasAvail && !item.AvailabilityChecked) {
                        actions.push('<button type="button" class="cgLink cgWatch" data-tmdb="' + esc(watchTmdb) + '" data-type="' + esc(watchKind) + '">Where to watch</button>');
                    }
                    // A JustWatch search for movies/shows that have no JustWatch link of their own (the JustWatch
                    // plugin only links owned items), so there is always a quick "where can I watch this" path.
                    if (item.TargetKindName === 'Movie' || item.TargetKindName === 'Series') {
                        var hasJw = (item.Links || []).some(function (l) { return /justwatch/i.test((l.Name || '') + ' ' + (l.Url || '')); });
                        if (!hasJw) {
                            actions.push('<a class="cgLink" href="' + esc('https://www.justwatch.com/' + jwLocale() + '/search?q=' + encodeURIComponent(item.Name)) + '" target="_blank" rel="noopener" title="Search JustWatch for where to watch">JustWatch search</a>');
                        }
                    }
                    // Experimental: mint this single gap as a virtual movie (Movie gaps only; episodes are native in core).
                    if (tmdb && item.TargetKindName === 'Movie') {
                        actions.push('<button type="button" class="cgLink cgMint" data-gapid="' + esc(item.Id) + '" title="Mint a virtual placeholder for this item"><span class="material-icons" aria-hidden="true">eco</span>Mint</button>');
                    }
                    // Diagnose why this is reported missing (a provider-id mismatch on an owned item, or a
                    // same-named reboot for an episode). Every gap kind today is diagnosable; an unsupported
                    // kind still gets a graceful "not available for this kind" verdict, so always offer it.
                    actions.push('<button type="button" class="cgLink cgDiagnose" data-gapid="' + esc(item.Id) + '" data-name="' + esc(item.Name) + '" title="Why is this listed as missing?"><span class="material-icons" aria-hidden="true" style="font-size:1em;vertical-align:text-bottom;">troubleshoot</span> Diagnose</button>');
                    // Dismiss this gap (resolve / not interested / snooze until release), or clear that.
                    if (res) {
                        actions.push('<button type="button" class="cgLink cgClearResolve" data-gapid="' + esc(item.Id) + '" title="Clear the dismissal (show as missing again)">Clear</button>');
                    } else {
                        actions.push('<button type="button" class="cgLink cgResolve" data-gapid="' + esc(item.Id) + '" title="Mark resolved (not really missing)"><span class="material-icons" aria-hidden="true" style="font-size:1em;vertical-align:text-bottom;">done</span> Resolve</button>');
                        actions.push('<button type="button" class="cgLink cgNotInterested" data-gapid="' + esc(item.Id) + '" title="Not interested (a real gap you do not want)">Not interested</button>');
                        if (item.IsUpcoming && item.ReleaseDate) {
                            actions.push('<button type="button" class="cgLink cgSnooze" data-gapid="' + esc(item.Id) + '" data-until="' + esc(item.ReleaseDate) + '" title="Hide until it is released">Snooze</button>');
                        }
                    }

                    // Provider links can run long because the host's own providers contribute too, so let them
                    // wrap on the left and keep the action buttons (Where to watch, Mint) flush right.
                    var linksRow = (providerLinks || actions.length)
                        ? '<div class="cgLinks" style="display:flex;flex-wrap:wrap;align-items:center;gap:.25em;margin-top:.3em;">'
                            + providerLinks
                            + (actions.length ? '<span class="cgActions" style="margin-left:auto;display:inline-flex;flex-wrap:wrap;justify-content:flex-end;gap:.25em;align-items:center;">' + actions.join('') + '</span>' : '')
                            + '</div>'
                        : '';

                    var avail = '';
                    var shownOffers = filterOffers(item.Availability);
                    if (shownOffers.length) {
                        avail = '<div class="fieldDescription" style="margin-top:.2em;">Where to watch: ' + availLinks(shownOffers) + '</div>';
                    } else if (watchable && item.AvailabilityChecked && !hasAvail) {
                        avail = '<div class="fieldDescription" style="margin-top:.2em;opacity:.6;">No streaming sources found.</div>';
                    }

                    var resolvedLine = res
                        ? '<div class="fieldDescription" style="margin-top:.2em;color:#3ddc97;">' + dismissalLabel(res) + '</div>'
                        : '';

                    var detailParts = [];
                    if (item.PatternName === 'Recommendation' && (item.OtherSources || []).length) {
                        // The primary source is the group header now, so the row lists only the other sources.
                        var srcs = [];
                        (item.OtherSources || []).forEach(function (s) { if (s && s.Name && !recSourceDismissed(s.Id)) { srcs.push(recSource(s.Name, s.Year, s.Type, s.Id)); } });
                        if (srcs.length) { detailParts.push('<div style="opacity:.85;">Also recommended by: ' + srcs.join(', ') + '</div>'); }
                    }
                    if (item.Overview) { detailParts.push(esc(item.Overview)); }
                    var details = detailParts.length
                        ? '<div class="cgDetails" style="display:none;">' + detailParts.join('') + '</div>'
                        : '';

                    var poster = item.ImageUrl
                        ? '<img src="' + esc(item.ImageUrl) + '" loading="lazy" style="width:40px;height:60px;object-fit:cover;margin-right:.8em;border-radius:4px;" />'
                        : '<div style="width:40px;height:60px;margin-right:.8em;background:#222;border-radius:4px;"></div>';

                    // Only Movie gaps are mintable, so only they get a multi-select checkbox.
                    var selBox = (tmdb && item.TargetKindName === 'Movie')
                        ? '<input type="checkbox" class="cgSel" data-gapid="' + esc(item.Id) + '" title="Select to mint" style="margin-right:.5em;flex:none;" />'
                        : '<span style="display:inline-block;width:1.4em;flex:none;"></span>';

                    return '<div class="listItem cgRow" data-gapid="' + esc(item.Id) + '" style="display:flex;align-items:center;padding:.35em .25em;' + (res ? 'opacity:.55;' : '') + '">'
                        + selBox
                        + poster
                        + '<div style="flex:1;min-width:0;">'
                        + '<div class="listItemBodyText">' + esc(item.Name)
                        + searchIcon(item.Name, domainScope(item.DomainName))
                        + openIcon(item.LibraryItemId) + '</div>'
                        + '<div class="listItemBodyText secondary">' + meta.join(' &middot; ') + '</div>'
                        + details
                        + avail
                        + resolvedLine
                        + linksRow
                        + '</div>'
                        + '</div>';
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
                    return ' <a class="cgLink cgOpen" href="' + itemUrl(id) + '" target="_blank" rel="noopener" title="Open in Jellyfin" aria-label="Open in Jellyfin"><span class="material-icons" aria-hidden="true" style="font-size:1.05em;vertical-align:middle;">open_in_new</span></a>';
                }

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
                    body.innerHTML = '<p class="fieldDescription">' + (deeper ? 'Confirming&hellip;' : 'Checking your library&hellip;') + '</p>';
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
                            body.innerHTML = renderDiagnosis(res);
                        })
                        .catch(function () { body.innerHTML = '<p class="fieldDescription">Could not run the diagnosis. Check the server logs.</p>'; });
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
                    if (!cell || cell.id == null) { return asMarkdown ? '' : '<span style="opacity:.4;">-</span>'; }
                    if (asMarkdown) { return cell.url ? '[' + cell.id + '](' + cell.url + ')' : String(cell.id); }
                    return cell.url ? '<a class="' + providerClass(cell.name).trim() + '" data-provider="' + esc(cell.name || '') + '" title="Open ' + esc(cell.id) + ' on ' + esc(cell.name || '') + '" aria-label="Open ' + esc(cell.id) + ' on ' + esc(cell.name || '') + '" href="' + esc(cell.url) + '" target="_blank" rel="noopener">' + esc(cell.id) + '</a>' : esc(cell.id);
                }

                function renderDiagnosis(res) {
                    res = res || {};
                    var rows = [];
                    if (res.Target) { rows.push(res.Target); }
                    (res.Candidates || []).forEach(function (c) { rows.push(c); });

                    var verdict = DIAG_REASON[res.ReasonName];
                    var html = verdict
                        ? '<div style="margin:0 0 .5em 0;"><span style="display:inline-block;padding:.15em .6em;border-radius:.8em;background:' + verdict.color + ';color:#111;font-weight:600;font-size:.85em;">' + esc(verdict.label) + '</span></div>'
                        : '';
                    html += '<p style="margin:.2em 0 .8em 0;">' + esc(res.Summary || '') + '</p>';
                    if (rows.length) {
                        var cols = diagColumns(rows);
                        var th = function (t) { return '<th style="padding:.3em .45em;font-weight:600;">' + esc(t) + '</th>'; };
                        html += '<table style="width:100%;border-collapse:collapse;font-size:.92em;">'
                            + '<thead><tr style="text-align:left;border-bottom:1px solid #444;">'
                            + th('Title') + th('Year')
                            + cols.map(function (t) { return th(DIAG_PROVIDER_LABEL[t] || t); }).join('')
                            + th('In library')
                            + '</tr></thead><tbody>';
                        rows.forEach(function (r) {
                            var isTarget = r.Relation === 'target';
                            var inLib = isTarget
                                ? '<span style="opacity:.7;">Missing</span>'
                                : (r.JellyfinItemId ? '<a href="' + esc(itemUrl(r.JellyfinItemId)) + '" target="_blank" rel="noopener">Open</a>' : 'Owned');
                            var cells = diagCells(r);
                            var td = function (c) { return '<td style="padding:.3em .45em;vertical-align:top;">' + c + '</td>'; };
                            html += '<tr style="border-bottom:1px solid #2a2a2a;' + (isTarget ? 'background:rgba(74,163,223,.1);' : '') + '">'
                                + td('<b>' + esc(r.Name || '') + '</b>' + (r.Note ? '<br><span style="opacity:.65;font-size:.85em;">' + esc(r.Note) + '</span>' : ''))
                                + td(r.Year ? esc(r.Year) : '<span style="opacity:.4;">-</span>')
                                + cols.map(function (t) { return td(diagCell(cells[t], false)); }).join('')
                                + td(inLib)
                                + '</tr>';
                        });
                        html += '</tbody></table>';
                    }
                    // Offer the networked confirmation, or note it already ran.
                    html += res.Deepened
                        ? '<div style="margin-top:.8em;opacity:.7;font-size:.85em;">Confirmed.</div>'
                        : '<div style="margin-top:.8em;"><button is="emby-button" type="button" class="raised cgDeepen" title="Resolve ids against the source provider to confirm the verdict and catch matches your local metadata missed.">Deeper analysis</button></div>';
                    return html;
                }

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
                    return ' <a class="cgLink cgSearch" href="' + esc(searchUrl(name, collectionType)) + '" target="_blank" rel="noopener" title="Search this Jellyfin for &ldquo;' + esc(name) + '&rdquo;" aria-label="Search Jellyfin for ' + esc(name) + '"><span class="material-icons" aria-hidden="true" style="font-size:1.05em;vertical-align:middle;">search</span></a>';
                }

                function groupHtml(level, label, count, collapsed, inner, itemId, extra, lazyToken) {
                    // A per-render id ties the header to its body for assistive tech (aria-controls), and
                    // aria-expanded mirrors the collapse state (kept in sync on toggle and re-render).
                    var bodyId = 'cgBody' + (++cgGroupSeq);
                    // A deferred group ships an empty body plus a token; ensureGroupBody fills it on expand.
                    var lazyAttr = lazyToken ? ' data-cglazy="' + esc(lazyToken) + '"' : '';
                    return '<div class="cgGroup cgL' + level + (collapsed ? ' cgCollapsed' : '') + '" data-cglabel="' + esc(label) + '"' + lazyAttr + '>'
                        + '<div class="cgHdr cgHdr' + level + '" role="button" tabindex="0" aria-expanded="' + (collapsed ? 'false' : 'true') + '" aria-controls="' + bodyId + '">'
                        + '<span class="cgCaret"></span><span class="cgLabel">' + esc(label) + '</span>'
                        + ' <span class="cgCount">(' + count + ')</span>' + (extra || '') + openIcon(itemId) + '</div>'
                        + '<div class="cgBody" id="' + bodyId + '">' + inner + '</div></div>';
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
                        return ' <a is="emby-linkbutton" class="cgLink' + providerClass(l.Name) + '" data-provider="' + esc(l.Name) + '" title="Open on ' + esc(l.Name) + '" aria-label="Open on ' + esc(l.Name) + '" href="' + esc(safeUrl(l.Url)) + '" target="_blank" rel="noopener noreferrer">' + esc(l.Name) + '</a>';
                    }).join('');
                }

                function coverageBadge(item) {
                    if (!item || !item.SetTotalCount || item.SetOwnedCount == null) { return ''; }
                    var pct = Math.round(item.SetOwnedCount / item.SetTotalCount * 100);
                    var full = item.SetOwnedCount + ' of ' + item.SetTotalCount + ' owned, ' + pct + '%';
                    return ' <span class="cgCoverage" title="' + esc(full) + '"><span class="cgCovFull">' + full + '</span><span class="cgCovPct">' + pct + '%</span></span>';
                }

                // A compact Diagnose control for a season header, run against one of the season's episodes, so
                // the popup can say whether the season belongs to the series you own or a same-named reboot.
                function seasonDiagnoseBtn(gapId, name) {
                    return ' <a class="cgLink cgDiagnose" data-gapid="' + esc(gapId) + '" data-name="' + esc(name)
                        + '" title="Is this season really part of the series you own?" aria-label="Diagnose this season">'
                        + '<span class="material-icons" aria-hidden="true" style="font-size:1.05em;vertical-align:middle;">troubleshoot</span></a>';
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
                        var seasonExtra = searchIcon(seriesName, 'tvshows') + openIcon(openId) + seasonDiag + batchDismissBtns(label);
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

                // The "kind of set" a SetCompletion gap completes, from its owning item's type, so the tab can
                // group collections, studios, keywords, series, and discographies into separate sections.
                var SET_KIND_ORDER = { 'Collections & franchises': 0, 'Series': 1, 'Discography': 2, 'Labels': 3, 'Studios': 4, 'Keywords': 5, 'Other': 9 };
                var SET_KIND_LABELS = {
                    BoxSet: 'Collections & franchises',
                    Series: 'Series',
                    MusicArtist: 'Discography',
                    MusicLabel: 'Record labels',
                    Studio: 'Studios',
                    Keyword: 'Keywords'
                };
                function setKindLabel(sourceItemType) {
                    return SET_KIND_LABELS[sourceItemType] || 'Other';
                }

                // The H2 source group for the Markdown export, mirroring the on-screen tree: a set's kind for
                // Set completion (Collections & franchises, Studios, Keywords, Series, Discography), otherwise
                // the owning source (the creator, or the title a recommendation came from).
                function exportGroupLabel(it) {
                    return it.PatternName === 'SetCompletion' ? setKindLabel(it.SourceItemType) : (it.SourceItemName || '(no source)');
                }

                // Order the export's source groups: set kinds in their canonical order, sources alphabetically.
                function exportGroupSort(pattern) {
                    if (pattern === 'SetCompletion') {
                        return function (a, b) { return (SET_KIND_ORDER[a] != null ? SET_KIND_ORDER[a] : 9) - (SET_KIND_ORDER[b] != null ? SET_KIND_ORDER[b] : 9); };
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
                        + (isEpisodic ? batchDismissBtns(src) : '')
                        + sourceLinks(srcItems[0]);
                    return groupHtml(2, src, srcItems.length, true, sourceBody(srcItems), '', extra);
                }

                // Render the current pattern's entities for the items passed (already scoped to one domain and,
                // via the A-Z selector, usually one letter): recommended titles as rows, creators as groups, or
                // the set grid. The A-Z bar handles letters, so there is no in-tree letter grouping.
                function buildTree(items) {
                    if (!items.length) { return ''; }
                    var pattern = items[0].PatternName;
                    lazyBodies = {}; // tokens are per-render; drop the previous render's builders

                    if (pattern === 'Recommendation') {
                        // Group discovery gaps under their source (a recommending owned title, or a curated
                        // list) the way Creator works groups by creator and the Markdown export already does, so
                        // a list's missing movies collapse under the list's name. A multi-source gap files under
                        // its primary source; its other sources stay on the row ("Also recommended by").
                        var bySource = groupBy(items, function (it) { return it.SourceItemName || '(no source)'; });
                        bySource.order.sort(ci);
                        return bySource.order.map(function (src) {
                            var sItems = bySource.map[src];
                            var token = 'lz' + (++cgGroupSeq);
                            lazyBodies[token] = function () { return sortRows(sItems).map(renderRow).join(''); };
                            return groupHtml(2, src, sItems.length, true, '', sItems[0].SourceItemId, streamDot(sItems) + searchIcon(src, '') + recSourceDismissBtn(sItems[0].SourceItemId, src) + sourceLinks(sItems[0]), token);
                        }).join('');
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
                            return groupHtml(2, src, cItems.length, true, '', cItems[0].SourceItemId, streamDot(cItems) + searchIcon(src, '') + creatorDismissBtn(cItems[0].SourceItemId, src) + sourceLinks(cItems[0]), token);
                        }).join('');
                    }

                    // SetCompletion: split by the kind of set, then lay each kind's collapsed sources out in a
                    // responsive grid. One domain can hold several kinds (Movies has collections, studios, and
                    // keywords), so each kind gets a heading; with a single kind the heading is dropped.
                    var byKind = groupBy(items, function (it) { return setKindLabel(it.SourceItemType); });
                    byKind.order.sort(function (a, b) {
                        return (SET_KIND_ORDER[a] != null ? SET_KIND_ORDER[a] : 9) - (SET_KIND_ORDER[b] != null ? SET_KIND_ORDER[b] : 9);
                    });
                    var multiKind = byKind.order.length > 1;
                    return byKind.order.map(function (kind) {
                        var bySrc = groupBy(byKind.map[kind], function (it) { return it.SourceItemName || '(no source)'; });
                        bySrc.order.sort(ci);
                        var srcHtml = bySrc.order.map(function (src) { return setSourceCell(src, bySrc.map[src]); }).join('');
                        var heading = multiKind ? '<h3 class="cgSetKind">' + esc(kind) + '</h3>' : '';
                        return heading + '<div class="cgGridWrap">' + srcHtml + '</div>';
                    }).join('');
                }

                // The filters shared by the tab counts and the list, all of them except the pattern itself,
                // so a tab's badge shows how many gaps would appear if you opened it under the current filters.
                function buildFilter(page) {
                    var type = page.querySelector('#cgTypeFilter').value;
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
                    // The per-pattern totals come from the summary, so the inactive tabs show a count without
                    // their items being loaded. Each tab shows the raw gap total for its pattern.
                    var counts = (page._summary && page._summary.PatternCounts) || {};
                    // Stay on a pattern that has any gaps at all, so toggling a filter down to zero does not
                    // yank you to another tab; only fall back when the current pattern is truly empty.
                    if (!page._pattern || !counts[page._pattern]) {
                        page._pattern = PATTERNS.filter(function (p) { return counts[p]; })[0] || PATTERNS[0];
                    }
                    // The active tab is worded for the domain in view (Series completion, Discography, ...);
                    // the others keep their generic wording, since each tab carries its own Type selection.
                    var domain = page.querySelector('#cgTypeFilter').value;
                    page.querySelector('#cgTabs').innerHTML = PATTERNS.map(function (p) {
                        var active = p === page._pattern ? ' cgActive' : '';
                        var lbl = patternLabel(p, p === page._pattern ? domain : '');
                        return '<button type="button" is="emby-button" class="raised cgTab' + active + '" data-pattern="' + p + '" style="margin-right:.4em;">'
                            + esc(lbl) + ' (' + (counts[p] || 0) + ')</button>';
                    }).join('');
                }

                // The media domains the plugin covers, always offered in the "Type:" selector so a domain with
                // zero entries this scan (for example Music when nothing music is owned) still shows and does
                // not look unsupported.
                var ALL_DOMAINS = ['Movies', 'Shows', 'Music', 'Books'];

                // Build the "Type:" (media domain) selector. It always offers every covered domain (plus any
                // unexpected one actually present), in display order, so the chooser is never hidden and a
                // domain is never missing just because this scan found nothing for it. It defaults to the first
                // domain that does have entries, so the report opens on content; a remembered domain
                // (page._wantType, from a saved view or the per-browser filters) is applied once.
                function renderTypeFilter(page) {
                    var wrap = page.querySelector('#cgTypeFilterWrap');
                    var sel = page.querySelector('#cgTypeFilter');
                    var present = {};
                    ((page._report && page._report.Items) || []).forEach(function (it) {
                        if (it.PatternName === page._pattern) { present[categoryOf(it)] = true; }
                    });
                    var domains = ALL_DOMAINS.slice();
                    Object.keys(present).forEach(function (d) { if (domains.indexOf(d) === -1) { domains.push(d); } });
                    domains.sort(function (a, b) {
                        return (CATEGORY_ORDER[a] != null ? CATEGORY_ORDER[a] : 9) - (CATEGORY_ORDER[b] != null ? CATEGORY_ORDER[b] : 9);
                    });
                    if (wrap) { wrap.style.display = 'inline-flex'; }
                    // Decide before rebuilding the options (which clears sel.value): a remembered domain wins,
                    // else keep the current one, else the first domain that actually has entries.
                    var firstWithEntries = domains.filter(function (d) { return present[d]; })[0] || domains[0];
                    var desired = (page._wantType && domains.indexOf(page._wantType) !== -1) ? page._wantType
                        : ((sel.value && domains.indexOf(sel.value) !== -1) ? sel.value : firstWithEntries);
                    sel.innerHTML = domains.map(function (d) { return '<option value="' + esc(d) + '">' + esc(d) + '</option>'; }).join('');
                    sel.value = desired;
                    // Consume the remembered domain once applied, so a later manual pick is not overridden.
                    if (page._wantType && desired === page._wantType) { page._wantType = ''; }
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
                // (the set's kind, the creator, or the recommending title), and each gap as an H3 with a detail
                // line. A table of contents jumps to each group. The summary line links to a shareable view.
                function buildMarkdown(page) {
                    var report = page._report || { Items: [] };
                    var pass = buildFilter(page);
                    var items = (report.Items || []).filter(function (it) { return it.PatternName === page._pattern && pass(it); });
                    // Angle brackets around the URL so encoded filter values cannot break the markdown link.
                    var out = ['_[' + items.length + ' gaps, exported ' + new Date().toLocaleString() + '](<' + shareUrl(page) + '>)_', ''];

                    // Allocate heading anchors in the order the headings appear, so the contents links resolve.
                    var anchorFor = anchorAllocator();

                    var byCat = groupBy(items, categoryOf);
                    byCat.order.sort(function (a, b) { return (CATEGORY_ORDER[a] != null ? CATEGORY_ORDER[a] : 9) - (CATEGORY_ORDER[b] != null ? CATEGORY_ORDER[b] : 9); });
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
                    var isSet = page._pattern === 'SetCompletion';
                    sections.forEach(function (s) {
                        out.push('# ' + mdHeading(s.heading), '');
                        s.groups.forEach(function (g) {
                            out.push('## ' + mdHeading(g.label), '');
                            if (isSet) {
                                // Two axes: the kind (the H2) and the individual set; each set collapses on its own.
                                var bySource = groupBy(g.items, function (it) { return it.SourceItemName || '(no source)'; });
                                bySource.order.sort(ci);
                                bySource.order.forEach(function (src) {
                                    var rows = bySource.map[src].slice().sort(byTitle);
                                    openDetails(esc(src) + ' (' + rows.length + ')');
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

                    var anchorFor = anchorAllocator();
                    sections.forEach(function (s) { s.anchor = anchorFor(s.heading); });

                    var out = ['# Mind the Gaps: identification audit', ''];
                    out.push('_Based on the gap scan from ' + new Date(audit.GeneratedUtc).toLocaleString()
                        + '. Library: ' + (audit.OwnedMovies || 0) + ' movies, ' + (audit.OwnedShows || 0)
                        + ' shows; ' + (audit.GapsChecked || 0) + ' gaps checked._', '');

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

                // The A-Z selector: one entry per letter present, plus a leading "*" for all. Clicking a letter
                // renders only that letter's entities (so a huge tab does not render at once); "*" renders the
                // lot. Hidden when there is only one letter (nothing to choose).
                function renderLetterBar(page, letters, sel) {
                    var bar = page.querySelector('#cgJump');
                    if (letters.length < 2) { bar.innerHTML = ''; bar.style.display = 'none'; return; }
                    var html = '<a class="cgJumpL cgJumpAll' + (sel === '*' ? ' cgJumpSel' : '') + '" data-l="*" title="Show all letters">*</a>';
                    html += letters.map(function (L) {
                        return '<a class="cgJumpL' + (sel === L ? ' cgJumpSel' : '') + '" data-l="' + esc(L) + '">' + esc(L) + '</a>';
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
                    byCat.order.sort(function (a, b) { return (CATEGORY_ORDER[a] != null ? CATEGORY_ORDER[a] : 9) - (CATEGORY_ORDER[b] != null ? CATEGORY_ORDER[b] : 9); });
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
                        var cov = totalSum ? ' <span class="cgRollupCov">(' + ownedSum + ' of ' + totalSum + ' owned, ' + Math.round(ownedSum / totalSum * 100) + '%)</span>' : '';
                        return '<b>' + esc(cat) + '</b>: ' + catItems.length + ' gaps across ' + nGroups + ' ' + noun + (nGroups === 1 ? '' : 's') + cov;
                    });
                    return parts.join(' &nbsp;&middot;&nbsp; ');
                }

                function page_pattern_noun() {
                    var p = document.querySelector('#MindTheGapsPage') && document.querySelector('#MindTheGapsPage')._pattern;
                    if (p === 'CreatorWorks') { return 'creator'; }
                    if (p === 'Recommendation') { return 'source'; }
                    return 'set';
                }

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

                    var streamable = page.querySelector('#cgStreamable').checked;
                    var empty;
                    if (streamable && !(report.Items || []).some(function (it) { return it.AvailabilityChecked; })) {
                        empty = '<p class="fieldDescription">No "where to watch" data yet, so this filter has nothing to act on. Look it up in the background, then it fills in here.</p>'
                            + '<button is="emby-button" type="button" id="cgEnableAvail" class="raised button-submit">'
                            + '<span>Look up where to watch</span></button>';
                    } else {
                        // The pattern has gaps overall (summary count) but none pass the filters: name the
                        // filters that are on so the user knows what to relax, rather than a dead-end blank.
                        // The Type (domain) selector is not a "hide" filter here, so it is handled separately:
                        // if the chosen domain is empty but the tab has gaps in another domain, say so.
                        var rawForPattern = (page._summary && page._summary.PatternCounts && page._summary.PatternCounts[page._pattern]) || 0;
                        var selDomain = page.querySelector('#cgTypeFilter').value;
                        var rawItems = (report.Items || []).filter(function (it) { return it.PatternName === page._pattern; });
                        var selDomainHasRaw = !selDomain || rawItems.some(function (it) { return categoryOf(it) === selDomain; });
                        var active = [];
                        if ((page.querySelector('#cgSearch').value || '').trim()) { active.push('the search box'); }
                        if (page.querySelector('#cgHideSpecials').checked) { active.push('"Hide specials"'); }
                        if (page.querySelector('#cgHideUpcoming').checked) { active.push('"Hide upcoming"'); }
                        if (streamable) { active.push('"Hide items with no sources"'); }
                        if (selDomain && !selDomainHasRaw && rawItems.length) {
                            empty = '<p class="fieldDescription">No ' + esc(selDomain) + ' gaps on this tab. Pick another type from the menu above.</p>';
                        } else if (rawForPattern > 0 && active.length) {
                            var list = active.length === 1 ? active[0]
                                : active.slice(0, -1).join(', ') + ' or ' + active[active.length - 1];
                            empty = '<p class="fieldDescription">No gaps match the current filters. Try clearing ' + list + '.</p>';
                        } else if (rawForPattern > 0) {
                            // No filters on, yet nothing shows: the rows are all dismissed.
                            empty = '<p class="fieldDescription">Every gap on this tab is dismissed. Turn on "Show dismissed" to see them.</p>';
                        } else {
                            empty = '<p class="fieldDescription">No gaps on this tab. Pick another tab, or rescan to refresh.</p>';
                        }
                    }

                    var listEl = page.querySelector('#cgList');

                    // Snapshot what the user has expanded/collapsed/selected and where they are scrolled, so a
                    // re-render (resolving a row, toggling a filter) does not throw it all away.
                    var collapsed = {}, openRows = {}, checkedSel = {};
                    var pg = listEl.querySelectorAll('.cgGroup');
                    for (var gi = 0; gi < pg.length; gi++) { collapsed[groupKey(pg[gi])] = pg[gi].classList.contains('cgCollapsed'); }
                    var pr = listEl.querySelectorAll('.cgRow');
                    for (var rj = 0; rj < pr.length; rj++) {
                        var dd = pr[rj].querySelector('.cgDetails');
                        if (dd && dd.style.display !== 'none') { openRows[pr[rj].getAttribute('data-gapid')] = true; }
                    }
                    var psel = listEl.querySelectorAll('.cgSel:checked');
                    for (var sk = 0; sk < psel.length; sk++) { checkedSel[psel[sk].getAttribute('data-gapid')] = true; }
                    var scroller = scrollerFor(page);
                    var scrollY = scroller.scrollTop;

                    listEl.innerHTML = displayItems.length ? buildTree(displayItems) : empty;

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
                    var nr = listEl.querySelectorAll('.cgRow');
                    for (var nri = 0; nri < nr.length; nri++) {
                        if (openRows[nr[nri].getAttribute('data-gapid')]) { var nd = nr[nri].querySelector('.cgDetails'); if (nd) { nd.style.display = 'block'; } }
                    }
                    var nsel = listEl.querySelectorAll('.cgSel');
                    for (var nsi = 0; nsi < nsel.length; nsi++) { if (checkedSel[nsel[nsi].getAttribute('data-gapid')]) { nsel[nsi].checked = true; } }
                    scroller.scrollTop = scrollY;

                    renderLetterBar(page, letters, letter);
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
                            type: page.querySelector('#cgTypeFilter').value,
                            sort: page.querySelector('#cgSort').value,
                            hideSpecials: page.querySelector('#cgHideSpecials').checked,
                            hideUpcoming: page.querySelector('#cgHideUpcoming').checked,
                            showResolved: page.querySelector('#cgShowResolved').checked,
                            streamable: page.querySelector('#cgStreamable').checked,
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
                    // The Type options are built per tab from the data, so remember the wanted domain and let
                    // renderTypeFilter apply it once that tab's domains are known.
                    page._wantType = state.type || '';
                    if (state.sort != null) { page.querySelector('#cgSort').value = state.sort; }
                    if (state.hideSpecials != null) { page.querySelector('#cgHideSpecials').checked = !!state.hideSpecials; }
                    if (state.hideUpcoming != null) { page.querySelector('#cgHideUpcoming').checked = !!state.hideUpcoming; }
                    if (state.showResolved != null) { page.querySelector('#cgShowResolved').checked = !!state.showResolved; }
                    if (state.streamable != null) { page.querySelector('#cgStreamable').checked = !!state.streamable; }
                    if (state.letter != null) { page._letter = state.letter; }
                    if (state.mon) {
                        var cbs = page.querySelectorAll('.cgMon');
                        for (var i = 0; i < cbs.length; i++) {
                            var k = cbs[i].getAttribute('data-mon');
                            if (state.mon[k] != null) { cbs[i].checked = !!state.mon[k]; }
                        }
                    }
                    knownProviders = Array.isArray(state.knownProviders) ? state.knownProviders : [];
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
                        type: page.querySelector('#cgTypeFilter').value,
                        sort: page.querySelector('#cgSort').value,
                        search: page.querySelector('#cgSearch').value || '',
                        hideSpecials: page.querySelector('#cgHideSpecials').checked,
                        hideUpcoming: page.querySelector('#cgHideUpcoming').checked,
                        showResolved: page.querySelector('#cgShowResolved').checked,
                        streamable: page.querySelector('#cgStreamable').checked,
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
                    if (v.pattern) { page._pattern = v.pattern; }
                    page._letter = v.letter != null ? v.letter : null;
                    // The Type options are rebuilt per tab, so route the wanted domain through _wantType.
                    page._wantType = v.type || '';
                    if (v.sort != null) { page.querySelector('#cgSort').value = v.sort; }
                    page.querySelector('#cgSearch').value = v.search || '';
                    page.querySelector('#cgHideSpecials').checked = !!v.hideSpecials;
                    page.querySelector('#cgHideUpcoming').checked = !!v.hideUpcoming;
                    page.querySelector('#cgShowResolved').checked = !!v.showResolved;
                    page.querySelector('#cgStreamable').checked = !!v.streamable;
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
                    // A saved view can switch the pattern, so make sure that tab's items are loaded first.
                    return ensureSlice(page, page._pattern).then(function () { applyAndRender(page); });
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
                    el.innerHTML = '<span style="opacity:.7;margin-left:1em;" title="' + esc(help) + '">' + esc(label) + '</span> '
                        + '<select is="emby-select" id="cgHiddenCreatorSel" class="emby-select" style="width:auto;" title="' + esc(help) + '">'
                        + entries.map(function (en) { return '<option value="' + esc(en.key) + '">' + esc(en.name) + '</option>'; }).join('')
                        + '</select> <button is="emby-button" type="button" id="cgRestoreCreatorBtn" class="raised" style="margin:0;" title="' + esc(help) + '"><span>Bring back</span></button>';
                }

                function renderViews(page) {
                    var views = loadViews();
                    var names = Object.keys(views).sort(function (a, b) { return ci(a, b); });
                    page.querySelector('#cgViews').innerHTML = '<option value="">(choose a saved view)</option>'
                        + names.map(function (n) { return '<option value="' + esc(n) + '">' + esc(n) + '</option>'; }).join('');
                }

                // Fetch one pattern's items on demand (cached per pattern), so a large report is not shipped
                // whole; the browser only loads the tab being viewed. Sets page._report to that slice.
                function ensureSlice(page, pattern) {
                    page._slices = page._slices || {};
                    if (page._slices[pattern]) {
                        page._report = page._slices[pattern];
                        return Promise.resolve(page._report);
                    }
                    Dashboard.showLoadingMsg();
                    return ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('MindTheGaps/Gaps', { pattern: pattern }), dataType: 'json' })
                        .then(function (report) {
                            page._slices[pattern] = report;
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
                        }, function () { Dashboard.hideLoadingMsg(); return { Items: [] }; });
                }

                // Whether the settings panel is the one on screen (it starts hidden; the gear toggles it).
                function settingsOpen(page) {
                    return page.querySelector('#cgSettingsPanel').style.display !== 'none';
                }

                // Reload the report after a background scan or look-up finishes, but defer it while the settings
                // panel is open so the report under it is not rebuilt out from under the user; it reloads when
                // settings closes.
                function reloadReport(page) {
                    if (settingsOpen(page)) { page._reloadAfterSettings = true; return; }
                    load(page);
                }

                function load(page) {
                    Dashboard.showLoadingMsg();
                    // Drop any cached slices so a reload (after a scan, mint, or availability pass) re-fetches.
                    page._slices = {};
                    ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('MindTheGaps/Summary'), dataType: 'json' })
                        .then(function (summary) {
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
                            // Fresh install with nothing scanned yet: land on settings once so the admin can
                            // configure and run the first scan. Once a scan exists, the report leads. The flag
                            // keeps this a one-time nudge, not a view it forces back to on every reload.
                            if (when === 'never' && !page._autoSettingsDone) {
                                page._autoSettingsDone = true;
                                toggleSettings(page, true);
                            }
                            // Pick a pattern that has gaps before loading its slice.
                            var counts = summary.PatternCounts || {};
                            if (!page._pattern || !counts[page._pattern]) {
                                page._pattern = PATTERNS.filter(function (p) { return counts[p]; })[0] || PATTERNS[0];
                            }
                            fetchResolved().then(function () {
                                // A shared link (cgview in the URL) overrides the default tab and the
                                // per-browser filters, once: it is stripped from the address bar on read.
                                var shared = consumeUrlView();
                                var render = shared
                                    ? applyView(page, shared)
                                    : ensureSlice(page, page._pattern).then(function () { applyAndRender(page); });
                                render.then(function () {
                                    checkStale(page, summary);
                                    Dashboard.hideLoadingMsg();
                                    // A deep-link (cgdiag in the URL, e.g. from an exported audit) opens that
                                    // gap's diagnosis straight away, optionally running the deeper pass.
                                    var diag = consumeUrlDiag();
                                    if (diag) { openDiagnose(diag.id, '', diag.deep); }
                                });
                            });
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
                function updateAvailButton(page) {
                    var btn = page.querySelector('#cgLookupAvail');
                    if (!btn) { return; }
                    var span = btn.querySelector('span');
                    var s = page._summary || {};
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
                                    reloadReport(page);
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
                                    reloadReport(page);
                                }
                            })
                            .catch(function () { finish('Lost contact while scanning. Refresh to see results.'); });
                    }

                    ApiClient.ajax({ type: 'POST', url: ApiClient.getUrl('MindTheGaps/Scan'), dataType: 'json' })
                        .then(function () { setTimeout(poll, 1000); })
                        .catch(function () { finish('Could not start the scan. Check the server logs.'); });
                }

                // ---- Settings panel (accordion) ----
                // The gear toggles between the report and an inline settings form, so the plugin keeps a
                // single sidebar entry. The form mirrors the standalone config page and saves through the
                // same plugin-configuration API. Config is fetched lazily the first time the panel opens.

                // Enable/disable and dim a secret field based on its toggle checkbox.
                function bindSettingsToggle(page, toggleId, inputId) {
                    var on = page.querySelector('#' + toggleId).checked;
                    var input = page.querySelector('#' + inputId);
                    input.disabled = !on;
                    input.required = on;
                    var container = input.closest('.inputContainer');
                    if (container) { container.style.opacity = on ? '' : '0.5'; }
                }

                function loadConfig(page, config) {
                    page.querySelector('#ScanCollections').checked = config.ScanCollections;
                    page.querySelector('#ScanSeries').checked = config.ScanSeries;
                    page.querySelector('#ScanPeople').checked = config.ScanPeople;
                    page.querySelector('#ScanRecommendations').checked = config.ScanRecommendations;
                    page.querySelector('#ScanCuratedSets').checked = config.ScanCuratedSets;
                    page.querySelector('#AutoSeedStudios').checked = config.AutoSeedStudios;
                    page.querySelector('#CuratedTmdbListIds').value = config.CuratedTmdbListIds || '';
                    loadChips(page, config);
                    page.querySelector('#ScanMusic').checked = config.ScanMusic;
                    page.querySelector('#ScanBooks').checked = config.ScanBooks;
                    page.querySelector('#ScanDiscogs').checked = config.ScanDiscogs;
                    page.querySelector('#DiscogsToken').value = config.DiscogsToken || '';
                    page.querySelector('#ScanMdbList').checked = config.ScanMdbList;
                    page.querySelector('#MdbListApiKey').value = config.MdbListApiKey || '';
                    page.querySelector('#IncludeAvailability').checked = config.IncludeAvailability;
                    page.querySelector('#AvailabilityCacheHours').value = config.AvailabilityCacheHours;
                    page.querySelector('#TraktEnabled').checked = config.TraktEnabled;
                    page.querySelector('#TraktClientId').value = config.TraktClientId || '';
                    page.querySelector('#TvMazeEnabled').checked = config.TvMazeEnabled;
                    page.querySelector('#TvdbEnabled').checked = config.TvdbEnabled;
                    page.querySelector('#TvdbApiKey').value = config.TvdbApiKey || '';
                    page.querySelector('#MetadataCountryCode').value = config.MetadataCountryCode || '';
                    page.querySelector('#MetadataLanguage').value = config.MetadataLanguage || '';
                    page.querySelector('#TmdbApiKey').value = config.TmdbApiKey || '';
                    page.querySelector('#WebhookUrl').value = config.WebhookUrl || '';
                    page.querySelector('#MaxRelatedPerItem').value = config.MaxRelatedPerItem;
                    page.querySelector('#MinRecommendationVotes').value = config.MinRecommendationVotes;
                    page.querySelector('#MaxMissingEpisodesPerShow').value = config.MaxMissingEpisodesPerShow;
                    page.querySelector('#MaxFilmographyPeople').value = config.MaxFilmographyPeople;
                    page.querySelector('#MinFilmographyVotes').value = config.MinFilmographyVotes;
                    page.querySelector('#MaxCastBillingOrder').value = config.MaxCastBillingOrder;
                    bindSettingsToggle(page, 'TraktEnabled', 'TraktClientId');
                    bindSettingsToggle(page, 'TvdbEnabled', 'TvdbApiKey');
                    // Freshly loaded values are not unsaved edits (assigning .value/.checked fires no events).
                    page._settingsDirty = false;
                }

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

                function saveConfig(page, e) {
                    if (e) { e.preventDefault(); }
                    var form = page.querySelector('#MindTheGapsConfigForm');

                    // Validate a secret only when its cross-check is on; bypass entirely when off.
                    if (form.querySelector('#TraktEnabled').checked && !form.querySelector('#TraktClientId').value.trim()) {
                        Dashboard.alert('Enter a Trakt client id, or turn off the Trakt cross-check.');
                        return false;
                    }
                    if (form.querySelector('#TvdbEnabled').checked && !form.querySelector('#TvdbApiKey').value.trim()) {
                        Dashboard.alert('Enter a TheTVDB API key, or turn off the TheTVDB cross-check.');
                        return false;
                    }

                    Dashboard.showLoadingMsg();
                    ApiClient.getPluginConfiguration(pluginId).then(function (config) {
                        config.ScanCollections = form.querySelector('#ScanCollections').checked;
                        config.ScanSeries = form.querySelector('#ScanSeries').checked;
                        config.ScanPeople = form.querySelector('#ScanPeople').checked;
                        config.ScanRecommendations = form.querySelector('#ScanRecommendations').checked;
                        config.ScanCuratedSets = form.querySelector('#ScanCuratedSets').checked;
                        config.AutoSeedStudios = form.querySelector('#AutoSeedStudios').checked;
                        config.CuratedTmdbListIds = form.querySelector('#CuratedTmdbListIds').value.trim();
                        // The chips hold the ids; nothing else to persist for curated sets.
                        var chips = page._chipState || {};
                        config.CuratedCompanyIds = chips.studio ? chips.studio.ids() : (config.CuratedCompanyIds || '');
                        config.CuratedKeywordIds = chips.keyword ? chips.keyword.ids() : (config.CuratedKeywordIds || '');
                        config.ScanMusic = form.querySelector('#ScanMusic').checked;
                        config.ScanBooks = form.querySelector('#ScanBooks').checked;
                        config.ScanDiscogs = form.querySelector('#ScanDiscogs').checked;
                        config.DiscogsToken = form.querySelector('#DiscogsToken').value;
                        config.DiscogsLabelIds = chips.label ? chips.label.ids() : (config.DiscogsLabelIds || '');
                        config.ScanMdbList = form.querySelector('#ScanMdbList').checked;
                        config.MdbListApiKey = form.querySelector('#MdbListApiKey').value.trim();
                        config.MdbListListIds = chips.mdblist ? chips.mdblist.ids() : (config.MdbListListIds || '');
                        config.IncludeAvailability = form.querySelector('#IncludeAvailability').checked;
                        config.AvailabilityCacheHours = parseInt(form.querySelector('#AvailabilityCacheHours').value || '24', 10);
                        config.TraktEnabled = form.querySelector('#TraktEnabled').checked;
                        config.TraktClientId = form.querySelector('#TraktClientId').value;
                        config.TvMazeEnabled = form.querySelector('#TvMazeEnabled').checked;
                        config.TvdbEnabled = form.querySelector('#TvdbEnabled').checked;
                        config.TvdbApiKey = form.querySelector('#TvdbApiKey').value;
                        config.MetadataCountryCode = form.querySelector('#MetadataCountryCode').value;
                        config.MetadataLanguage = form.querySelector('#MetadataLanguage').value;
                        config.TmdbApiKey = form.querySelector('#TmdbApiKey').value;
                        config.WebhookUrl = form.querySelector('#WebhookUrl').value;
                        config.MaxRelatedPerItem = parseInt(form.querySelector('#MaxRelatedPerItem').value || '0', 10);
                        config.MinRecommendationVotes = parseInt(form.querySelector('#MinRecommendationVotes').value || '0', 10);
                        config.MaxMissingEpisodesPerShow = parseInt(form.querySelector('#MaxMissingEpisodesPerShow').value || '0', 10);
                        config.MaxFilmographyPeople = parseInt(form.querySelector('#MaxFilmographyPeople').value || '1000', 10);
                        config.MinFilmographyVotes = parseInt(form.querySelector('#MinFilmographyVotes').value || '0', 10);
                        config.MaxCastBillingOrder = parseInt(form.querySelector('#MaxCastBillingOrder').value || '0', 10);
                        ApiClient.updatePluginConfiguration(pluginId, config).then(function (result) {
                            page._settingsDirty = false;
                            cgRegion = (config.MetadataCountryCode || '').trim().toLowerCase();
                            Dashboard.processPluginConfigurationUpdateResult(result);
                        });
                    });
                    return false;
                }

                // Swap the page between the report and the settings form, keeping the gear (and title) in sync.
                function toggleSettings(page, show) {
                    page.querySelector('#cgSettingsPanel').style.display = show ? '' : 'none';
                    page.querySelector('#cgReportPanel').style.display = show ? 'none' : '';
                    var gear = page.querySelector('#cgSettings');
                    gear.setAttribute('aria-expanded', show ? 'true' : 'false');
                    gear.title = show ? 'Close settings' : 'Plugin settings';
                    var icon = gear.querySelector('.material-icons');
                    if (icon) { icon.textContent = show ? 'close' : 'settings'; }
                    page.querySelector('#cgPageTitle').textContent = show ? 'Mind the Gaps settings' : 'Mind the Gaps ToDo List';
                    if (show) {
                        // Re-read the config every time settings opens (opening is a discrete action now), so it
                        // always reflects what is saved; any prior unsaved edits were already discarded on close.
                        Dashboard.showLoadingMsg();
                        ApiClient.getPluginConfiguration(pluginId).then(function (config) {
                            loadConfig(page, config);
                            Dashboard.hideLoadingMsg();
                        });
                    } else if (page._reloadAfterSettings) {
                        // A scan or look-up finished while settings was open; pick up its results now.
                        page._reloadAfterSettings = false;
                        load(page);
                    }
                    var sc = scrollerFor(page);
                    if (sc) { sc.scrollTop = 0; }
                }

                // A type-ahead chip control over a curated-set kind ('studio' or 'keyword'). Each chip holds
                // {Id, Name}: the name shows, the id is what gets saved, so the numeric id is never exposed.
                // Suggestions come from the server's TheMovieDb search; wired once, populated by loadConfig.
                function setupChips(page, kind, boxId, listId, inputId, suggestId) {
                    var box = page.querySelector('#' + boxId);
                    var list = page.querySelector('#' + listId);
                    var input = page.querySelector('#' + inputId);
                    var suggest = page.querySelector('#' + suggestId);
                    var state = { chips: [], items: [], sel: -1, seq: 0, timer: 0 };
                    page._chipState = page._chipState || {};
                    page._chipState[kind] = state;

                    function announce(msg) { var live = page.querySelector('#cgChipLive'); if (live) { live.textContent = msg; } }
                    function render() {
                        list.innerHTML = state.chips.map(function (c, i) {
                            return '<span class="cgChip" role="listitem">' + esc(c.Name)
                                + '<button type="button" class="cgChipX" data-i="' + i + '" aria-label="Remove ' + esc(c.Name) + '" title="Remove ' + esc(c.Name) + '">&times;</button></span>';
                        }).join('');
                    }
                    function closeSuggest() {
                        suggest.classList.remove('cgShown'); suggest.innerHTML = ''; state.items = []; state.sel = -1;
                        input.setAttribute('aria-expanded', 'false'); input.removeAttribute('aria-activedescendant');
                    }
                    function has(id) { return state.chips.some(function (c) { return c.Id === id; }); }
                    function addChip(c) {
                        if (c && c.Id && !has(c.Id)) { state.chips.push({ Id: c.Id, Name: c.Name || String(c.Id) }); render(); page._settingsDirty = true; announce('Added ' + (c.Name || c.Id)); }
                        input.value = ''; closeSuggest();
                    }
                    function removeAt(i) {
                        var removed = state.chips[i];
                        state.chips.splice(i, 1); render(); page._settingsDirty = true;
                        if (removed) { announce('Removed ' + removed.Name); }
                        input.focus();
                    }
                    function renderSuggest() {
                        suggest.innerHTML = state.items.length
                            ? state.items.map(function (it, i) { return '<div class="cgSuggestItem' + (i === state.sel ? ' cgSuggestSel' : '') + '" role="option" id="' + suggestId + '-opt-' + i + '" aria-selected="' + (i === state.sel ? 'true' : 'false') + '" data-i="' + i + '">' + esc(it.Name) + '</div>'; }).join('')
                            : '<div class="cgSuggestEmpty">No matches</div>';
                        suggest.classList.add('cgShown');
                        input.setAttribute('aria-expanded', 'true');
                        input.setAttribute('aria-activedescendant', state.sel >= 0 ? (suggestId + '-opt-' + state.sel) : '');
                    }
                    function search() {
                        var q = input.value.trim();
                        if (q.length < 2) { closeSuggest(); return; }
                        var mySeq = ++state.seq;
                        ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('MindTheGaps/CuratedSearch', { kind: kind, query: q }), dataType: 'json' }).then(function (res) {
                            if (mySeq !== state.seq) { return; } // a newer keystroke superseded this response
                            state.items = (res || []).filter(function (it) { return !has(it.Id); });
                            state.sel = state.items.length ? 0 : -1;
                            renderSuggest();
                        }, function () { closeSuggest(); });
                    }

                    input.addEventListener('input', function () { clearTimeout(state.timer); state.timer = setTimeout(search, 220); });
                    input.addEventListener('keydown', function (e) {
                        if (e.key === 'ArrowDown' && state.items.length) { state.sel = (state.sel + 1) % state.items.length; renderSuggest(); e.preventDefault(); }
                        else if (e.key === 'ArrowUp' && state.items.length) { state.sel = (state.sel - 1 + state.items.length) % state.items.length; renderSuggest(); e.preventDefault(); }
                        else if (e.key === 'Enter') { e.preventDefault(); if (state.sel >= 0 && state.items[state.sel]) { addChip(state.items[state.sel]); } }
                        else if (e.key === 'Escape') { closeSuggest(); }
                        else if (e.key === 'Backspace' && !input.value && state.chips.length) { removeAt(state.chips.length - 1); }
                    });
                    // A blur closes the dropdown, but after a beat so a click on a suggestion lands first.
                    input.addEventListener('blur', function () { setTimeout(closeSuggest, 150); });
                    suggest.addEventListener('mousedown', function (e) {
                        var el = e.target.closest('.cgSuggestItem');
                        if (el) { e.preventDefault(); addChip(state.items[parseInt(el.getAttribute('data-i'), 10)]); }
                    });
                    list.addEventListener('click', function (e) {
                        var x = e.target.closest('.cgChipX');
                        if (x) { removeAt(parseInt(x.getAttribute('data-i'), 10)); }
                    });
                    box.addEventListener('click', function (e) { if (e.target === box || e.target === list) { input.focus(); } });

                    state.set = function (chips) {
                        var seen = {};
                        state.chips = [];
                        (chips || []).forEach(function (c) {
                            var k = String(c.Id);
                            if (c.Id && !seen[k]) { seen[k] = 1; state.chips.push({ Id: c.Id, Name: c.Name || k }); }
                        });
                        render();
                    };
                    state.ids = function () { return state.chips.map(function (c) { return c.Id; }).join(','); };
                    return state;
                }

                // Populate the studio/keyword chips from the saved config: resolve the stored ids to display
                // names server-side, then hand them to the chip controls.
                function loadChips(page, config) {
                    var resolve = function (kind, ids) {
                        var st = page._chipState && page._chipState[kind];
                        if (!st) { return; }
                        st.set([]);
                        if (!ids) { return; }
                        ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('MindTheGaps/CuratedResolve', { kind: kind, ids: ids }), dataType: 'json' })
                            .then(function (res) { st.set(res || []); }, function () { });
                    };
                    resolve('studio', config.CuratedCompanyIds || '');
                    resolve('keyword', config.CuratedKeywordIds || '');
                    resolve('label', config.DiscogsLabelIds || '');
                    resolve('mdblist', config.MdbListListIds || '');
                }

                function bindSettings(page) {
                    // Any edit to a settings field marks the form dirty, so closing it can warn about unsaved
                    // changes. loadConfig/save reset the flag; programmatic value assignment fires no events.
                    var markDirty = function () { page._settingsDirty = true; };
                    page.querySelector('#MindTheGapsConfigForm').addEventListener('input', markDirty);
                    page.querySelector('#MindTheGapsConfigForm').addEventListener('change', markDirty);
                    page.querySelector('#TraktEnabled').addEventListener('change', function () { bindSettingsToggle(page, 'TraktEnabled', 'TraktClientId'); });
                    page.querySelector('#TvdbEnabled').addEventListener('change', function () { bindSettingsToggle(page, 'TvdbEnabled', 'TvdbApiKey'); });
                    setupChips(page, 'studio', 'cgStudioBox', 'cgStudioChips', 'cgStudioInput', 'cgStudioSuggest');
                    setupChips(page, 'keyword', 'cgKeywordBox', 'cgKeywordChips', 'cgKeywordInput', 'cgKeywordSuggest');
                    setupChips(page, 'label', 'cgLabelBox', 'cgLabelChips', 'cgLabelInput', 'cgLabelSuggest');
                    setupChips(page, 'mdblist', 'cgMdbListBox', 'cgMdbListChips', 'cgMdbListInput', 'cgMdbListSuggest');
                    // Reveal/hide a secret field; inputs default to type=password so a key is not shoulder-read.
                    var revealBtns = page.querySelectorAll('.cgReveal');
                    for (var rb = 0; rb < revealBtns.length; rb++) {
                        revealBtns[rb].addEventListener('click', function () {
                            var input = page.querySelector('#' + this.getAttribute('data-target'));
                            if (!input) { return; }
                            var sh = input.type === 'password';
                            input.type = sh ? 'text' : 'password';
                            var span = this.querySelector('span');
                            if (span) { span.textContent = sh ? 'Hide' : 'Show'; }
                        });
                    }
                    page.querySelector('#RemovePreview').addEventListener('click', function () { runRemoval('RemoveMintedMovies?dryRun=true', null); });
                    page.querySelector('#RemoveMinted').addEventListener('click', function () { runRemoval('RemoveMintedMovies', null); });
                    page.querySelector('#cgAuditBtn').addEventListener('click', function () {
                        var btn = this;
                        var label = btn.querySelector('span');
                        var orig = label ? label.textContent : '';
                        btn.disabled = true;
                        if (label) { label.textContent = 'Auditing…'; }
                        ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('MindTheGaps/DiagnoseAudit'), dataType: 'json' })
                            .then(function (audit) {
                                downloadText('mind-the-gaps-identification-audit.md', buildAuditMarkdown(audit));
                                if (label) { label.textContent = orig; }
                                btn.disabled = false;
                            })
                            .catch(function () {
                                Dashboard.alert('Could not run the audit. Check the server logs.');
                                if (label) { label.textContent = orig; }
                                btn.disabled = false;
                            });
                    });
                    page.querySelector('#ResetRotation').addEventListener('click', function () {
                        if (!window.confirm('Forget which items were scanned recently and start a fresh coverage cycle on the next scan?')) { return; }
                        Dashboard.showLoadingMsg();
                        ApiClient.ajax({ type: 'POST', url: ApiClient.getUrl('MindTheGaps/ResetScanRotation') })
                            .then(function () {
                                Dashboard.hideLoadingMsg();
                                Dashboard.alert('Scan rotation reset. The next scan starts a fresh coverage cycle.');
                            }, function () {
                                Dashboard.hideLoadingMsg();
                                Dashboard.alert('Reset failed. Check the server logs.');
                            });
                    });
                    // Bind the save to the live form (per page show), so the native form never GET-submits
                    // (which would leak API keys into the URL).
                    page.querySelector('#MindTheGapsConfigForm').addEventListener('submit', function (e) { saveConfig(page, e); });
                }

                document.querySelector('#MindTheGapsPage').addEventListener('pageshow', function () {
                    var page = this;
                    // Jellyfin keeps this page element and re-fires pageshow on every navigation, so attach
                    // the listeners once. Without this they stack, and a delegated handler fires N times (an
                    // even count makes a header toggle a no-op, double-mints, etc.). The data still reloads
                    // on every show, below.
                    if (page._cgBound) { load(page); return; }
                    page._cgBound = true;
                    page._pattern = null;

                    page.querySelector('#cgRefresh').addEventListener('click', function () {
                        load(page);
                    });
                    page.querySelector('#cgStaleRescan').addEventListener('click', function () {
                        startScan(page, this);
                    });
                    page.querySelector('#cgSettings').addEventListener('click', function () {
                        var opening = page.querySelector('#cgSettingsPanel').style.display === 'none';
                        // Closing with unsaved edits: warn before discarding. Cancel returns to the form to Save;
                        // OK discards. No reload needed here: reopening re-reads the saved config, so the
                        // abandoned edits never show; just drop the dirty flag.
                        if (!opening && page._settingsDirty) {
                            if (!window.confirm('You have unsaved settings changes that will be lost. Click Cancel to go back and Save, or OK to discard them.')) {
                                return;
                            }
                            page._settingsDirty = false;
                        }
                        toggleSettings(page, opening);
                    });
                    bindSettings(page);
                    // Cache the configured region once so the JustWatch and availability links match the
                    // availability lookups (which use MetadataCountryCode) rather than the browser language.
                    ApiClient.getPluginConfiguration(pluginId).then(function (cfg) {
                        cgRegion = (cfg.MetadataCountryCode || '').trim().toLowerCase();
                    });
                    // Cache the server's display name so exported links back to it can be labelled with it.
                    ApiClient.getPublicSystemInfo().then(function (info) {
                        cgServerName = (info && info.ServerName) || '';
                    }, function () { /* best-effort; the label falls back to "Jellyfin" */ });
                    // Diagnose popup: close via the button, a backdrop click, or Escape.
                    document.getElementById('cgDiagClose').addEventListener('click', closeDiagnose);
                    document.getElementById('cgDiagModal').addEventListener('click', function (e) {
                        if (e.target === this) { closeDiagnose(); return; }
                        if (e.target.closest('.cgDeepen')) { openDiagnose(this._gapId, this._name, true); }
                    });
                    document.addEventListener('keydown', function (e) { if (e.key === 'Escape') { closeDiagnose(); } });
                    function setAllSelected(checked) {
                        // "Select all" should reach every row, including those in still-deferred creator-works
                        // groups, so build any unbuilt bodies first (a no-op on tabs with no deferred groups).
                        if (checked) {
                            var deferred = page.querySelectorAll('#cgList .cgGroup[data-cglazy]');
                            for (var d = 0; d < deferred.length; d++) { ensureGroupBody(deferred[d]); }
                        }
                        var cbs = page.querySelectorAll('#cgList .cgSel');
                        for (var i = 0; i < cbs.length; i++) { cbs[i].checked = checked; }
                        refreshSelectBar(page);
                        updateSelection(page);
                    }
                    page.querySelector('#cgSelectAll').addEventListener('click', function () { setAllSelected(true); });
                    page.querySelector('#cgSelectNone').addEventListener('click', function () { setAllSelected(false); });
                    page.querySelector('#cgList').addEventListener('change', function (e) {
                        if (e.target && e.target.classList && e.target.classList.contains('cgSel')) { updateSelection(page); }
                    });
                    page.querySelector('#cgMintSelected').addEventListener('click', function () {
                        var ids = selectedGapIds(page);
                        if (!ids.length) { return; }
                        if (!window.confirm('Mint ' + ids.length + ' selected item(s) as virtual placeholders?')) { return; }
                        var btn = this;
                        var label = btn.querySelectorAll('span')[1];
                        var labelHtml = label ? label.innerHTML : '';
                        btn.disabled = true;

                        // Runs in the background so a big selection cannot time out the request.
                        function done(msg) {
                            if (label) { label.innerHTML = labelHtml; }
                            btn.disabled = false;
                            if (msg) { Dashboard.alert(msg); }
                        }
                        function pollMint() {
                            if (!pageActive(page)) { return; }
                            ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('MindTheGaps/MintStatus'), dataType: 'json' })
                                .then(function (s) {
                                    if (s && s.Running) {
                                        if (label) { label.textContent = 'Minting… ' + Math.round(s.Progress || 0) + '%'; }
                                        setTimeout(pollMint, 1500);
                                    } else {
                                        done();
                                        Dashboard.alert((s && s.Message) || 'Done.');
                                        load(page);
                                    }
                                })
                                .catch(function () { done('Lost contact while minting. Check the server logs.'); });
                        }

                        if (label) { label.textContent = 'Minting…'; }
                        ApiClient.ajax({
                            type: 'POST',
                            url: ApiClient.getUrl('MindTheGaps/MintGaps'),
                            contentType: 'application/json',
                            data: JSON.stringify(ids),
                            dataType: 'json'
                        }).then(function () { setTimeout(pollMint, 800); })
                          .catch(function () {
                            done('Bulk mint failed. Check the server logs.');
                        });
                    });
                    page.querySelector('#cgRescan').addEventListener('click', function () {
                        startScan(page, this);
                    });
                    page.querySelector('#cgTabs').addEventListener('click', function (e) {
                        var tab = e.target.closest ? e.target.closest('.cgTab') : null;
                        if (!tab) { return; }
                        page._pattern = tab.getAttribute('data-pattern');
                        page._letter = null; // a new tab has its own letters; let applyAndRender default it
                        ensureSlice(page, page._pattern).then(function () { applyAndRender(page); });
                    });
                    page.querySelector('#cgTypeFilter').addEventListener('change', function () {
                        saveFilters(page);
                        if (page._report) { applyAndRender(page); }
                    });
                    page.querySelector('#cgSort').addEventListener('change', function () {
                        saveFilters(page);
                        if (page._report) { applyAndRender(page); }
                    });
                    page.querySelector('#cgSearch').addEventListener('input', function () {
                        if (page._report) { applyAndRender(page); }
                    });
                    page.querySelector('#cgHideSpecials').addEventListener('change', function () {
                        saveFilters(page);
                        if (page._report) { applyAndRender(page); }
                    });
                    page.querySelector('#cgHideUpcoming').addEventListener('change', function () {
                        saveFilters(page);
                        if (page._report) { applyAndRender(page); }
                    });
                    page.querySelector('#cgShowResolved').addEventListener('change', function () {
                        saveFilters(page);
                        if (page._report) { applyAndRender(page); }
                    });
                    page.querySelector('#cgStreamable').addEventListener('change', function () {
                        saveFilters(page);
                        if (page._report) { applyAndRender(page); }
                    });
                    page.querySelector('#cgLookupAvail').addEventListener('click', function () {
                        startAvailability(page, this);
                    });
                    page.querySelector('#cgSaveView').addEventListener('click', function () {
                        var name = (window.prompt('Save current filters as a view named:', '') || '').trim().slice(0, 60);
                        if (!name) { return; }
                        var views = loadViews();
                        views[name] = captureView(page);
                        storeViews(views);
                        renderViews(page);
                        page.querySelector('#cgViews').value = name;
                    });
                    page.querySelector('#cgViews').addEventListener('change', function () {
                        var views = loadViews();
                        if (this.value && views[this.value]) { applyView(page, views[this.value]); }
                    });
                    page.querySelector('#cgDeleteView').addEventListener('click', function () {
                        var sel = page.querySelector('#cgViews').value;
                        if (!sel) { return; }
                        var views = loadViews();
                        delete views[sel];
                        storeViews(views);
                        renderViews(page);
                    });
                    page.querySelector('#cgShareLink').addEventListener('click', function () {
                        var url = shareUrl(page);
                        var ok = function () { Dashboard.alert('Link copied. It opens this view (tab and filters) when pasted.'); };
                        // Clipboard API needs a secure context; fall back to a prompt the user can copy from.
                        if (navigator.clipboard && navigator.clipboard.writeText) {
                            navigator.clipboard.writeText(url).then(ok, function () { window.prompt('Copy this link:', url); });
                        } else {
                            window.prompt('Copy this link:', url);
                        }
                    });
                    page.querySelector('#cgHiddenCreators').addEventListener('click', function (e) {
                        if (!e.target.closest || !e.target.closest('#cgRestoreCreatorBtn')) { return; }
                        var sel = page.querySelector('#cgHiddenCreatorSel');
                        if (!sel || !sel.value) { return; }
                        ApiClient.ajax({ type: 'POST', url: ApiClient.getUrl('MindTheGaps/Unresolve', { id: sel.value }) })
                            .then(function () { fetchResolved().then(function () { applyAndRender(page); }); })
                            .catch(function () { Dashboard.alert('Could not restore the creator. Check the server logs.'); });
                    });
                    page.querySelector('#cgExport').addEventListener('click', function () {
                        if (!page._report) { return; }
                        // Name the file by the active domain and the domain-aware pattern label (the same words
                        // shown on screen), each lowercased with all whitespace turned to hyphens, so it reads
                        // consistently and each domain's export of a pattern keeps its own filename.
                        var typeSel = page.querySelector('#cgTypeFilter');
                        var domainValue = (typeSel && typeSel.value) || '';
                        var label = page._pattern ? patternLabel(page._pattern, domainValue) : 'report';
                        var parts = [domainValue, label].filter(Boolean).map(slugify).join('-');
                        downloadText(`mind-the-gaps-${parts}.md`, buildMarkdown(page));
                    });
                    page.querySelector('#cgJump').addEventListener('click', function (e) {
                        var a = e.target.closest ? e.target.closest('.cgJumpL') : null;
                        if (!a) { return; }
                        // Select the letter (or "*" for all): re-render that letter's items and scroll to top.
                        page._letter = a.getAttribute('data-l');
                        saveFilters(page);
                        applyAndRender(page);
                        var sc = scrollerFor(page);
                        if (sc) { sc.scrollTop = 0; }
                    });
                    page.querySelector('#cgMonFilter').addEventListener('change', function () {
                        saveFilters(page);
                        // Monetization changes affect "where to watch" matching, so re-filter the list.
                        if (page._report) { applyAndRender(page); }
                    });
                    page.querySelector('#cgProviderFilter').addEventListener('change', function (e) {
                        var cb = e.target.closest ? e.target.closest('.cgProv') : null;
                        if (!cb) { return; }
                        var name = cb.getAttribute('data-prov');
                        if (cb.checked) { delete disabledProviders[name]; } else { disabledProviders[name] = true; }
                        renderProviderFilter(page);
                        saveFilters(page);
                        if (page._report) { applyAndRender(page); }
                    });
                    page.querySelector('#cgProviderFilter').addEventListener('click', function (e) {
                        // The header toggles the (long) provider list open and closed.
                        if (e.target.closest && e.target.closest('.cgProvToggle')) {
                            providersExpanded = !providersExpanded;
                            renderProviderFilter(page);
                            saveFilters(page);
                            return;
                        }
                        var all = e.target.closest ? e.target.closest('.cgProvAll') : null;
                        var none = e.target.closest ? e.target.closest('.cgProvNone') : null;
                        if (!all && !none) { return; }
                        disabledProviders = {};
                        if (none) { knownProviders.forEach(function (n) { disabledProviders[n] = true; }); }
                        renderProviderFilter(page);
                        saveFilters(page);
                        // Actually re-filter the list with the new provider set (this is what makes
                        // "enable all" / "disable all" visibly change the results, not just the checkboxes).
                        if (page._report) { applyAndRender(page); }
                    });
                    // Group headers are focusable (role=button); Enter/Space toggles them like a click, so the
                    // tree is operable from the keyboard.
                    page.querySelector('#cgList').addEventListener('keydown', function (e) {
                        if (e.key !== 'Enter' && e.key !== ' ' && e.key !== 'Spacebar') { return; }
                        var hdr = e.target.closest ? e.target.closest('.cgHdr') : null;
                        if (hdr && hdr.parentElement) {
                            e.preventDefault();
                            var nowCollapsed = hdr.parentElement.classList.toggle('cgCollapsed');
                            hdr.setAttribute('aria-expanded', nowCollapsed ? 'false' : 'true');
                            if (!nowCollapsed) { ensureGroupBody(hdr.parentElement); refreshSelectBar(page); }
                        }
                    });
                    page.querySelector('#cgList').addEventListener('click', function (e) {
                        if (!e.target.closest) { return; }

                        // The "open in Jellyfin" and search icons are real links; let them open the new tab
                        // and do not treat the click as a header toggle or row action.
                        if (e.target.closest('.cgOpen') || e.target.closest('.cgSearch')) { return; }

                        // The "Hide items with no sources" nudge: look the data up in the background.
                        var enableAvail = e.target.closest('#cgEnableAvail');
                        if (enableAvail) {
                            startAvailability(page, enableAvail);
                            return;
                        }

                        var resolveBtn = e.target.closest('.cgResolve');
                        if (resolveBtn) {
                            var rid = resolveBtn.getAttribute('data-gapid');
                            var rItems = (page._report && page._report.Items) || [];
                            var rName = 'this item';
                            for (var ri = 0; ri < rItems.length; ri++) { if (rItems[ri].Id === rid) { rName = rItems[ri].Name || 'this item'; break; } }
                            var note = window.prompt('Resolve "' + rName + '" (not really missing).\nOptional note (e.g. why):', '');
                            if (note === null) { return; }
                            note = note.trim().slice(0, 100);
                            ApiClient.ajax({
                                type: 'POST',
                                url: ApiClient.getUrl('MindTheGaps/Resolve'),
                                contentType: 'application/json',
                                data: JSON.stringify({ Id: rid, Note: note })
                            }).then(function () { fetchResolved().then(function () { applyAndRender(page); }); })
                                .catch(function () { Dashboard.alert('Could not save the resolution. Check the server logs.'); });
                            return;
                        }

                        var niBtn = e.target.closest('.cgNotInterested');
                        if (niBtn) {
                            ApiClient.ajax({
                                type: 'POST',
                                url: ApiClient.getUrl('MindTheGaps/Resolve'),
                                contentType: 'application/json',
                                data: JSON.stringify({ Id: niBtn.getAttribute('data-gapid'), Kind: 'notinterested', Note: '' })
                            }).then(function () { fetchResolved().then(function () { applyAndRender(page); }); })
                                .catch(function () { Dashboard.alert('Could not save. Check the server logs.'); });
                            return;
                        }

                        var snoozeBtn = e.target.closest('.cgSnooze');
                        if (snoozeBtn) {
                            ApiClient.ajax({
                                type: 'POST',
                                url: ApiClient.getUrl('MindTheGaps/Resolve'),
                                contentType: 'application/json',
                                data: JSON.stringify({ Id: snoozeBtn.getAttribute('data-gapid'), Kind: 'snoozed', SnoozedUntil: snoozeBtn.getAttribute('data-until') })
                            }).then(function () { fetchResolved().then(function () { applyAndRender(page); }); })
                                .catch(function () { Dashboard.alert('Could not snooze. Check the server logs.'); });
                            return;
                        }

                        var clearResBtn = e.target.closest('.cgClearResolve');
                        if (clearResBtn) {
                            ApiClient.ajax({
                                type: 'POST',
                                url: ApiClient.getUrl('MindTheGaps/Unresolve', { id: clearResBtn.getAttribute('data-gapid') })
                            }).then(function () { fetchResolved().then(function () { applyAndRender(page); }); })
                                .catch(function () { Dashboard.alert('Could not clear the resolution. Check the server logs.'); });
                            return;
                        }

                        // Batch resolve / not-interested for every listed gap under a series or season group.
                        var batchBtn = e.target.closest('.cgBatchResolve') || e.target.closest('.cgBatchNotInterested');
                        if (batchBtn) {
                            var notInterested = !!e.target.closest('.cgBatchNotInterested');
                            var grp = batchBtn.closest('.cgGroup');
                            if (!grp) { return; }
                            var rows = grp.querySelectorAll('.cgRow');
                            var ids = [];
                            for (var bi = 0; bi < rows.length; bi++) {
                                var bid = rows[bi].getAttribute('data-gapid');
                                if (bid) { ids.push(bid); }
                            }
                            if (!ids.length) { return; }
                            var blabel = batchBtn.getAttribute('data-label') || 'this group';
                            var verb = notInterested ? 'mark as not interested' : 'resolve';
                            if (!window.confirm('This will ' + verb + ' all ' + ids.length + ' listed item(s) under ' + blabel + '. Continue?')) { return; }
                            ApiClient.ajax({
                                type: 'POST',
                                url: ApiClient.getUrl('MindTheGaps/ResolveBatch'),
                                contentType: 'application/json',
                                data: JSON.stringify({ Ids: ids, Kind: notInterested ? 'notinterested' : null, Note: '' })
                            }).then(function () { fetchResolved().then(function () { applyAndRender(page); }); })
                                .catch(function () { Dashboard.alert('Could not update those items. Check the server logs.'); });
                            return;
                        }

                        var dismissCreatorBtn = e.target.closest('.cgDismissCreator');
                        if (dismissCreatorBtn) {
                            var dcGuid = dismissCreatorBtn.getAttribute('data-gapid');
                            var dcName = dismissCreatorBtn.getAttribute('data-name') || 'this creator';
                            if (!window.confirm('Stop scanning "' + dcName + '" and hide all their gaps?')) { return; }
                            ApiClient.ajax({
                                type: 'POST',
                                url: ApiClient.getUrl('MindTheGaps/Resolve'),
                                contentType: 'application/json',
                                data: JSON.stringify({ Id: 'creator:' + dcGuid, Kind: 'notinterested', Note: dcName })
                            }).then(function () { fetchResolved().then(function () { applyAndRender(page); }); })
                                .catch(function () { Dashboard.alert('Could not dismiss the creator. Check the server logs.'); });
                            return;
                        }

                        var dismissRecSrcBtn = e.target.closest('.cgDismissRecSource');
                        if (dismissRecSrcBtn) {
                            var rsGuid = dismissRecSrcBtn.getAttribute('data-gapid');
                            var rsName = dismissRecSrcBtn.getAttribute('data-name') || 'this title';
                            if (!window.confirm('Stop recommendations from "' + rsName + '"?')) { return; }
                            ApiClient.ajax({
                                type: 'POST',
                                url: ApiClient.getUrl('MindTheGaps/Resolve'),
                                contentType: 'application/json',
                                data: JSON.stringify({ Id: 'recsource:' + rsGuid, Kind: 'notinterested', Note: rsName })
                            }).then(function () { fetchResolved().then(function () { applyAndRender(page); }); })
                                .catch(function () { Dashboard.alert('Could not dismiss the source. Check the server logs.'); });
                            return;
                        }

                        var restoreCreatorBtn = e.target.closest('.cgRestoreCreator');
                        if (restoreCreatorBtn) {
                            ApiClient.ajax({
                                type: 'POST',
                                url: ApiClient.getUrl('MindTheGaps/Unresolve', { id: 'creator:' + restoreCreatorBtn.getAttribute('data-gapid') })
                            }).then(function () { fetchResolved().then(function () { applyAndRender(page); }); })
                                .catch(function () { Dashboard.alert('Could not restore the creator. Check the server logs.'); });
                            return;
                        }

                        var mintBtn = e.target.closest('.cgMint');
                        if (mintBtn) {
                            var gid = mintBtn.getAttribute('data-gapid');
                            if (!gid) { return; }
                            var mintHtml = mintBtn.innerHTML;
                            mintBtn.textContent = 'Minting…';
                            mintBtn.disabled = true;
                            ApiClient.ajax({
                                type: 'POST',
                                url: ApiClient.getUrl('MindTheGaps/MintGap', { id: gid }),
                                dataType: 'json'
                            }).then(function (msg) {
                                Dashboard.alert(String(msg));
                                mintBtn.innerHTML = mintHtml;
                                mintBtn.disabled = false;
                            }).catch(function () {
                                Dashboard.alert('Mint failed. Check the server logs.');
                                mintBtn.innerHTML = mintHtml;
                                mintBtn.disabled = false;
                            });
                            return;
                        }

                        var diagBtn = e.target.closest('.cgDiagnose');
                        if (diagBtn) {
                            openDiagnose(diagBtn.getAttribute('data-gapid'), diagBtn.getAttribute('data-name') || 'this title');
                            return;
                        }

                        var watchBtn = e.target.closest('.cgWatch');
                        if (watchBtn) {
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
                            return;
                        }

                        var hdr = e.target.closest('.cgHdr');
                        if (hdr && hdr.parentElement) {
                            var nowCollapsed = hdr.parentElement.classList.toggle('cgCollapsed');
                            hdr.setAttribute('aria-expanded', nowCollapsed ? 'false' : 'true');
                            if (!nowCollapsed) { ensureGroupBody(hdr.parentElement); refreshSelectBar(page); }
                            return;
                        }

                        // Click a row (but not one of its links or the select checkbox) to reveal its overview.
                        var row = e.target.closest('.cgRow');
                        if (row && !e.target.closest('a') && !e.target.closest('input')) {
                            var d = row.querySelector('.cgDetails');
                            if (d) { d.style.display = d.style.display === 'none' ? 'block' : 'none'; }
                        }
                    });
                    // Floating "back to top" button: show it once scrolled down, and scroll the report's
                    // own scroll container (or the window, whichever actually scrolls) back to the top.
                    (function () {
                        var topBtn = page.querySelector('#cgScrollTop');
                        var scroller = scrollerFor(page);
                        var listenOn = (scroller === document.scrollingElement || scroller === document.documentElement) ? window : scroller;
                        function onScroll() {
                            var y = scroller.scrollTop || window.pageYOffset || 0;
                            topBtn.style.display = y > 300 ? 'inline-flex' : 'none';
                        }
                        listenOn.addEventListener('scroll', onScroll, { passive: true });
                        topBtn.addEventListener('click', function () { scroller.scrollTo({ top: 0, behavior: 'smooth' }); });
                        onScroll();
                    })();

                    restoreFilters(page);
                    renderViews(page);
                    showVersion(page);
                    load(page);
                });
            })();
        