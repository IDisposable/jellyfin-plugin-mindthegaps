// Report page, part 10: wires up every control once the page shows. Every function it calls lives
// in one of the files above; this is deliberately just the addEventListener calls, so "what does
// this button do" always starts here.

document.querySelector('#MindTheGapsPage').addEventListener('pageshow', function () {
    var page = this;
    // Jellyfin keeps this page element and re-fires pageshow on every navigation, so attach
    // the listeners once. Without this they stack, and a delegated handler fires N times (an
    // even count makes a header toggle a no-op, double-mints, etc.). The data still reloads
    // on every show, below.
    if (page._cgBound) { load(page); return; }
    page._cgBound = true;
    page._domain = null;
    page._pattern = null;

    page.querySelector('#cgRefresh').addEventListener('click', function () {
        load(page);
    });
    page.querySelector('#cgStaleRescan').addEventListener('click', function () {
        startScan(page, this);
    });
    page.querySelector('#cgSettings').addEventListener('click', function () {
        Dashboard.navigate('configurationpage?name=MindTheGapsSettings');
    });
    page.querySelector('#RemovePreview').addEventListener('click', function () { runRemoval('RemoveMintedMovies?dryRun=true', null); });
    page.querySelector('#RemoveMinted').addEventListener('click', function () { runRemoval('RemoveMintedMovies', null); });
    page.querySelector('#cgAuditBtn').addEventListener('click', function () {
        var btn = this;
        var label = btn.querySelector('span');
        var orig = label ? label.textContent : '';
        btn.disabled = true;
        if (label) { label.textContent = 'Auditing…'; }
        var auditDomain = page._domain || '';
        var auditPattern = page._pattern || '';
        // Name the file by domain and the domain-aware pattern label, the same as the gap export.
        var auditLabel = auditPattern ? patternLabel(auditPattern, auditDomain) : '';
        var auditParts = [auditDomain, auditLabel].filter(Boolean).map(slugify).join('-') || 'all';
        ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('MindTheGaps/DiagnoseAudit', { domain: auditDomain, pattern: auditPattern }), dataType: 'json' })
            .then(function (audit) {
                downloadText('mind-the-gaps-identification-audit-' + auditParts + '.md', buildAuditMarkdown(audit));
                if (label) { label.textContent = orig; }
                btn.disabled = false;
            })
            .catch(function () {
                Dashboard.alert('Could not run the audit. Check the server logs.');
                if (label) { label.textContent = orig; }
                btn.disabled = false;
            });
    });
    page.querySelector('#cgFulfillBtn').addEventListener('click', function () { openFulfillment(page); });
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
    page.querySelector('#PruneStale').addEventListener('click', function () {
        if (!window.confirm('Remove gaps from a keyword, company, list, or watchlist you have since removed or turned off?')) { return; }
        Dashboard.showLoadingMsg();
        ApiClient.ajax({ type: 'POST', url: ApiClient.getUrl('MindTheGaps/PruneStaleGaps'), dataType: 'json' })
            .then(function (removed) {
                Dashboard.hideLoadingMsg();
                Dashboard.alert(removed > 0 ? 'Removed ' + removed + ' stale gap(s).' : 'Nothing to prune; every gap is still in scope.');
            }, function () {
                Dashboard.hideLoadingMsg();
                Dashboard.alert('Prune failed. Check the server logs.');
            });
    });
    // Cache the configured region once so the JustWatch and availability links match the
    // availability lookups (which use MetadataCountryCode) rather than the browser language.
    ApiClient.getPluginConfiguration(pluginId).then(function (cfg) {
        cgRegion = (cfg.MetadataCountryCode || '').trim().toLowerCase();
    });
    refreshAcqConfig(page);
    // Cache the server's display name so exported links back to it can be labelled with it.
    ApiClient.getPublicSystemInfo().then(function (info) {
        cgServerName = (info && info.ServerName) || '';
    }, function () { /* best-effort; the label falls back to "Jellyfin" */ });
    // Diagnose popup: close via the button, a backdrop click, or Escape.
    document.getElementById('cgDiagClose').addEventListener('click', closeDiagnose);
    document.getElementById('cgDiagModal').addEventListener('click', function (e) {
        if (e.target === this) { closeDiagnose(); return; }
        if (e.target.closest('.cgDeepen')) { openDiagnose(this._gapId, this._name, true); return; }
        if (e.target.closest('.cgDiagExport')) {
            var dx = this._res;
            if (dx) { downloadText(diagFilename(dx, this._name), buildDiagnosisMarkdown(dx, this._name)); }
        }
    });
    document.addEventListener('keydown', function (e) { if (e.key === 'Escape') { closeDiagnose(); closeExplore(page); closeFulfillment(); } });
    // Explore a source popup: the modal handles its own close button, backdrop click, kind
    // selector, source picker, Run, and Clear. The toolbar button opens it.
    setupExploreModal(page);
    // Fulfillment queue popup: close via the button, a backdrop click, or Escape (below). "Show
    // fulfilled" just re-renders from what is already loaded; Mark fetched and the where-to-watch
    // lookup are handled by delegation since a queue row has no live report item to key off.
    document.getElementById('cgFulfillClose').addEventListener('click', closeFulfillment);
    document.getElementById('cgFulfillModal').addEventListener('click', function (e) {
        if (e.target === this) { closeFulfillment(); }
    });
    document.getElementById('cgFulfillShowDone').addEventListener('change', function () {
        var modal = document.getElementById('cgFulfillModal');
        if (modal._data) { renderFulfillment(modal); }
    });
    document.getElementById('cgFulfillBody').addEventListener('click', function (e) {
        if (!e.target.closest) { return; }
        if (e.target.closest('a[href]')) { return; }
        var doneBtn = e.target.closest('.cgFulfillDone');
        if (doneBtn) {
            var modal = document.getElementById('cgFulfillModal');
            var rowId = doneBtn.getAttribute('data-rowid');
            var items = (modal._data && modal._data.Items) || [];
            var row = items.filter(function (r) { return r.Id === rowId; })[0];
            if (!row) { return; }
            doneBtn.disabled = true;
            ApiClient.ajax({
                type: 'POST',
                url: ApiClient.getUrl('MindTheGaps/Todo/Demand/MarkFetched'),
                contentType: 'application/json',
                data: JSON.stringify(row.Entries || []),
                dataType: 'json'
            }).then(function (res) {
                row.OpenCount = 0;
                renderFulfillment(modal);
                Dashboard.alert('Marked fetched for ' + ((res && res.Updated) || 0) + ' user(s).');
            }).catch(function () {
                doneBtn.disabled = false;
                Dashboard.alert('Could not mark that title fetched. Check the server logs.');
            });
        }
    });
    document.getElementById('cgFulfillVerifyAll').addEventListener('click', function () {
        var modal = document.getElementById('cgFulfillModal');
        Dashboard.showLoadingMsg();
        verifyAllFulfillment(modal).then(function (res) {
            Dashboard.hideLoadingMsg();
            Dashboard.alert('Checked ' + ((res && res.Checked) || 0) + ' entry(s); ' + ((res && res.Owned) || 0) + ' are already in your library.');
            return primeFulfillmentAvailability(page, modal);
        }).catch(function () {
            Dashboard.hideLoadingMsg();
            Dashboard.alert('Could not check the library. Check the server logs.');
        });
    });
    document.getElementById('cgFulfillExport').addEventListener('click', function () {
        var modal = document.getElementById('cgFulfillModal');
        downloadText('mind-the-gaps-fulfillment-queue.md', buildFulfillmentMarkdown(modal));
    });
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
    page.querySelector('#cgTodoSelected').addEventListener('click', function () {
        todoAdd(selectedGapIds(page), this);
    });
    page.querySelector('#cgRescan').addEventListener('click', function () {
        startScan(page, this);
    });
    page.querySelector('#cgTabs').addEventListener('click', function (e) {
        var tab = e.target.closest ? e.target.closest('.cgTab') : null;
        if (!tab) { return; }
        page._lettersByDomain = page._lettersByDomain || {};
        page._lettersByDomain[page._domain] = page._letter;
        page._domain = tab.getAttribute('data-domain');
        pruneOtherDomains(page, page._domain);
        // Restore this tab's own remembered letter (including an explicit "*"); a tab visited for
        // the first time has none, so applyAndRender picks its default.
        page._letter = Object.prototype.hasOwnProperty.call(page._lettersByDomain, page._domain)
            ? page._lettersByDomain[page._domain] : null;
        page._pattern = pickPattern(page, page._domain);
        ensureSlice(page, page._pattern, page._domain).then(function () { applyAndRender(page); });
    });
    page.querySelector('#cgTypeFilter').addEventListener('change', function () {
        page._pattern = this.value;
        page._patternByDomain = page._patternByDomain || {};
        page._patternByDomain[page._domain] = page._pattern;
        saveFilters(page);
        ensureSlice(page, page._pattern, page._domain).then(function () { applyAndRender(page); });
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
    page.querySelector('#cgCompact').addEventListener('change', function () {
        page._compact = this.checked;
        page.querySelector('#cgReportPanel').classList.toggle('cgCompactMode', page._compact);
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
    // Export verifies what it is about to write first, so the file is a list of things you actually
    // still need rather than a snapshot of whatever the last scan believed. That drops the rows you now
    // hold from the report as well, which is the same clear-down the refresh controls run, so it says
    // what it cleared rather than doing it silently.
    page.querySelector('#cgExport').addEventListener('click', function () {
        if (!page._report) { return; }
        var write = function () {
            // Name the file by the active domain and the domain-aware pattern label (the same words
            // shown on screen), each lowercased with all whitespace turned to hyphens, so it reads
            // consistently and each domain's export of a pattern keeps its own filename.
            var domainValue = page._domain || '';
            var label = page._pattern ? patternLabel(page._pattern, domainValue) : 'report';
            var parts = [domainValue, label].filter(Boolean).map(slugify).join('-');
            Dashboard.showLoadingMsg();
            ensureFullForExport(page).then(function () {
                Dashboard.hideLoadingMsg();
                downloadText('mind-the-gaps-' + parts + '.md', buildMarkdown(page));
            }, function () {
                Dashboard.hideLoadingMsg();
                Dashboard.alert('Could not load the full report for the export. Check the server logs.');
            });
        };

        // Verify exactly what is about to be written, not just the letter on screen.
        var toWrite = exportItems(page);
        if (!toWrite.length) { write(); return; }

        Dashboard.showLoadingMsg();
        verifyGaps(page, toWrite.map(function (it) { return it.Id; })).then(function (res) {
            Dashboard.hideLoadingMsg();
            var cleared = (res && res.Removed) || 0;
            if (cleared) {
                applyAndRender(page);
                Dashboard.alert('Cleared ' + cleared + ' item(s) you already have; the export leaves them out.');
            }

            // Write after the re-render, so the file matches what the screen now shows.
            write();
        }).catch(function () {
            // A failed check must not cost you the export; fall back to writing what is on screen.
            Dashboard.hideLoadingMsg();
            Dashboard.alert('Could not check your library first, so the export reflects the list as it stands.');
            write();
        });
    });
    page.querySelector('#cgExploreBtn').addEventListener('click', function () {
        openExplore(page);
    });
    // The rollup line sits outside the list, so its clear-down control needs its own delegation. It
    // scopes to one media domain, which on a tab with no kind headings (Creator works, Discover, or a
    // Set completion whose domain has a single kind) is the only level between a group and the tab.
    page.querySelector('#cgRollup').addEventListener('click', function (e) {
        var rEl = e.target.closest ? e.target.closest('.cgClear') : null;
        if (!rEl) { return; }
        var rKey = rEl.getAttribute('data-key') || '';
        var rRows = rowsInScope(page, 'domain', rKey);
        if (!rRows.length) { return; }
        if (!window.confirm('Check all ' + rRows.length + ' item(s) under ' + rKey + ' against your library and clear the ones you have?')) { return; }
        rEl.classList.add('cgBusy');
        clearDownScope(page, rRows, ' under ' + rKey)
            .catch(function () { Dashboard.alert('Could not check your library. Check the server logs.'); })
            .then(function () { rEl.classList.remove('cgBusy'); });
    });
    // The whole filtered tab at once, the widest scope of the same routine. The count is confirmed
    // first because it is not undoable in place (a cleared row comes back on the next scan only if it
    // is genuinely still missing).
    page.querySelector('#cgVerifyShown').addEventListener('click', function () {
        var shown = page._shown || [];
        if (!shown.length) { Dashboard.alert('Nothing shown to check.'); return; }
        if (!window.confirm('Check all ' + shown.length + ' shown item(s) against your library and clear the ones you have?')) { return; }
        Dashboard.showLoadingMsg();
        clearDownScope(page, shown, '')
            .catch(function () { Dashboard.alert('Could not check your library. Check the server logs.'); })
            .then(function () { Dashboard.hideLoadingMsg(); });
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
    // A row's popover builds its body on first open (see renderRow). 'toggle' on <details> does not
    // bubble, so this listener runs in the capture phase to see it at all via delegation, and fires
    // for the click handler's manual pop.open assignment below the same as it would for a native
    // toggle (setting .open from JS dispatches 'toggle' too). Only one popover is open at a time:
    // closing every other currently-open .cgPop here (skipping an ancestor or descendant of the one
    // just opened, since a nested Resolve popover opening must not close the Actions popover holding
    // it) covers both the top-level popovers and the nested Resolve one in a single place. The body
    // itself is a plain in-flow block (see renderRow/CSS): CSS containment on Jellyfin's own page
    // wrapper rules out position:fixed ever landing in the right place here (see mindthegaps.css's
    // .cgRow comment), so .cgPop[open] claims the icon row's full width via flex-basis instead.
    page.querySelector('#cgList').addEventListener('toggle', function (e) {
        var det = e.target;
        if (!det.matches || !det.matches('.cgPop')) { return; }
        if (det.open) {
            var openOnes = page.querySelectorAll('#cgList .cgPop[open]');
            for (var i = 0; i < openOnes.length; i++) {
                var o = openOnes[i];
                if (o !== det && !o.contains(det) && !det.contains(o)) { o.open = false; }
            }
        }
        if (!det.matches('.cgPop[data-pop]') || !det.open) { return; }
        populatePopover(page, det);
    }, true);
    // The title's detail is a plain in-flow block (see renderRow/CSS), click-only, not hover: the
    // detail renders directly below the title, so moving the pointer down into it necessarily exits
    // the title first, indistinguishable from moving away entirely. cgPinned, set by clicking
    // the title, is cleared only by clicking that title again or another row's (the click handler,
    // near the popover accordion above, keeps only one pinned at a time the same way).
    var ensureExpandedDetailBuilt = function (row) {
        var detailEl = row && row.querySelector('.cgTitleDetail');
        if (!detailEl || detailEl.dataset.built) { return detailEl; }
        detailEl.dataset.built = '1';
        var item = findRowItem(page, row.getAttribute('data-gapid'));
        if (item) {
            detailEl.innerHTML = wrap('div', { style: 'opacity:.7;' }, 'Loading');
            ensureItemDetail(item).then(function (full) { detailEl.innerHTML = buildExpandedDetailBody(full); primeSendProfilePickers(detailEl); });
        }
        return detailEl;
    };
    // The title's native tooltip is the overview, but a list row does not carry it (see ensureItemDetail),
    // so it is fetched on the first hover and set as the title attribute, which the browser reads when it
    // decides to show the tooltip, a beat after the pointer settles.
    page.querySelector('#cgList').addEventListener('mouseover', function (e) {
        var titleEl = e.target.closest ? e.target.closest('.cgTitle') : null;
        if (!titleEl || titleEl.hasAttribute('title')) { return; }
        var item = findRowItem(page, titleEl.closest('.cgRow').getAttribute('data-gapid'));
        if (!item || !item.HasOverview) { return; }
        ensureItemDetail(item).then(function (full) {
            if (full.Overview) { titleEl.setAttribute('title', full.Overview); }
        });
    });
    // A row's thumbnail is a best-effort guess for some sources (MusicBrainz's cover art comes from a
    // fixed URL formula keyed by MBID, with no check that art actually exists there), so a broken
    // image is expected occasionally, not a bug: swap it for the plain "no image" placeholder rather
    // than showing the browser's broken-image icon. 'error' does not bubble, so this runs in the
    // capture phase to see it via delegation, the same reason 'toggle' does above.
    page.querySelector('#cgList').addEventListener('error', function (e) {
        var img = e.target;
        // The server's route did not answer with an image (or a redirect to one): try the provider directly
        // before giving up on the image.
        var direct = img.getAttribute && img.getAttribute('data-direct');
        if (direct && img.getAttribute('src') !== direct) {
            img.setAttribute('src', direct);
            return;
        }
        if (img.matches && img.matches('img.cgSvc')) {
            // A service logo that will not load falls back to the service's initial, like an absent one.
            var initial = document.createElement('span');
            initial.className = 'cgSvc cgSvcText';
            initial.title = img.title;
            initial.textContent = (img.alt || '?').charAt(0).toUpperCase();
            img.replaceWith(initial);
            return;
        }
        if (!img.matches || !img.matches('img.cgThumb')) { return; }
        var placeholder = document.createElement('span');
        placeholder.className = 'cgThumb cgThumbEmpty';
        img.replaceWith(placeholder);
    }, true);
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

        var summary = e.target.closest('summary');
        var pop = summary && summary.parentElement;
        if (pop && pop.matches && pop.matches('.cgPop[data-pop]')
            && pop.closest('#cgList') === e.currentTarget) {
            e.preventDefault();
            pop.open = !pop.open;
            if (pop.open) { populatePopover(page, pop); }
            return;
        }

        // Clicking a title pins its detail open (cgPinned) until that title is clicked again or
        // another row's is: only one row is pinned at a time.
        var clickedTitle = e.target.closest('.cgTitle');
        if (clickedTitle) {
            var pinnedRow = clickedTitle.closest('.cgRow');
            var pinnedDetail = ensureExpandedDetailBuilt(pinnedRow);
            if (pinnedDetail) {
                var wasPinned = pinnedDetail.classList.contains('cgPinned');
                var otherPinned = page.querySelectorAll('#cgList .cgTitleDetail.cgPinned');
                for (var pi = 0; pi < otherPinned.length; pi++) {
                    if (otherPinned[pi] !== pinnedDetail) { otherPinned[pi].classList.remove('cgPinned'); }
                }
                pinnedDetail.classList.toggle('cgPinned', !wasPinned);
            }
            return;
        }

        // Real navigation links (open in Jellyfin, search, provider and source links, JustWatch)
        // open their new tab; do not treat the click as a header toggle or row action. Action
        // controls (Diagnose, dismiss, batch) carry no href, so they fall through to handling.
        if (e.target.closest('a[href]')) { return; }

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

        // The one clear-down control, wherever it was clicked. The scope decides which of the rows on
        // screen it covers; the behavior past that is identical at every level.
        var clearEl = e.target.closest('.cgClear');
        if (clearEl) {
            var scope = clearEl.getAttribute('data-scope') || '';
            var key = clearEl.getAttribute('data-key') || '';
            var picked = rowsInScope(page, scope, key);
            if (!picked.length) { return; }
            // Only the wide scopes confirm first: a row or a season is small enough to just do.
            if ((scope === 'domain' || scope === 'kind')
                && !window.confirm('Check all ' + picked.length + ' item(s) here against your library and clear the ones you have?')) {
                return;
            }

            clearEl.classList.add('cgBusy');
            clearDownScope(page, picked, scopeLabel(scope, key, picked))
                .catch(function () { Dashboard.alert('Could not check your library. Check the server logs.'); })
                .then(function () { clearEl.classList.remove('cgBusy'); });
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

        var sendArrBtn = e.target.closest('.cgSendArr');
        var sendSeerrBtn = e.target.closest('.cgSendSeerr');
        var sendBtn = sendArrBtn || sendSeerrBtn;
        if (sendBtn) {
            var sgid = sendBtn.getAttribute('data-gapid');
            if (!sgid) { return; }
            var sendEndpoint = sendArrBtn ? 'SendToArr' : 'SendToSeerr';
            var sendParams = { id: sgid };
            // The picker sits beside the button as a sibling, keyed by the same kind, and is only ever
            // populated once its lookup resolves; an empty/hidden select just leaves the param off, and
            // SendToArr falls back to the configured default profile.
            var sendKind = sendArrBtn && sendArrBtn.getAttribute('data-kind');
            var profileSelect = sendKind && sendBtn.parentNode && sendBtn.parentNode.querySelector('.cgSendProfile[data-kind="' + sendKind + '"]');
            if (profileSelect && profileSelect.value) { sendParams.qualityProfileId = profileSelect.value; }
            var sendHtml = sendBtn.innerHTML;
            sendBtn.textContent = 'Sending…';
            sendBtn.disabled = true;
            ApiClient.ajax({
                type: 'POST',
                url: ApiClient.getUrl('MindTheGaps/' + sendEndpoint, sendParams),
                dataType: 'json'
            }).then(function (r) {
                Dashboard.alert((r && r.Message) ? String(r.Message) : 'Sent.');
                sendBtn.innerHTML = sendHtml;
                sendBtn.disabled = false;
            }).catch(function () {
                Dashboard.alert('Send failed. Check the server logs.');
                sendBtn.innerHTML = sendHtml;
                sendBtn.disabled = false;
            });
            return;
        }

        var diagBtn = e.target.closest('.cgDiagnose');
        if (diagBtn) {
            openDiagnose(diagBtn.getAttribute('data-gapid'), diagBtn.getAttribute('data-name') || 'this title');
            return;
        }

        var todoAddBtn = e.target.closest('.cgTodoAdd');
        if (todoAddBtn) {
            var tgid = todoAddBtn.getAttribute('data-gapid');
            if (tgid) { todoAdd([tgid], todoAddBtn); }
            return;
        }

        var watchBtn = e.target.closest('.cgWatch');
        if (watchBtn) {
            handleWatchClick(page, watchBtn);
            return;
        }

        var hdr = e.target.closest('.cgHdr');
        if (hdr && hdr.parentElement) {
            var nowCollapsed = hdr.parentElement.classList.toggle('cgCollapsed');
            hdr.setAttribute('aria-expanded', nowCollapsed ? 'false' : 'true');
            if (!nowCollapsed) { ensureGroupBody(hdr.parentElement); refreshSelectBar(page); }
            return;
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
