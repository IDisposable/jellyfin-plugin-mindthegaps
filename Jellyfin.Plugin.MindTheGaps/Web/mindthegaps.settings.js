// ---- The settings page ----
// The settings form. Saves through the plugin-configuration API; touches nothing but its own DOM.

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
    page.querySelector('#ScanTmdbLists').checked = config.ScanTmdbLists;
    page.querySelector('#AutoSeedStudios').checked = config.AutoSeedStudios;
    page.querySelector('#CuratedTmdbListIds').value = config.CuratedTmdbListIds || '';
    loadChips(page, config);
    page.querySelector('#ScanMusic').checked = config.ScanMusic;
    page.querySelector('#ScanBooks').checked = config.ScanBooks;
    page.querySelector('#ScanCuratedBooks').checked = config.ScanCuratedBooks;
    page.querySelector('#CuratedOpenLibrarySubjects').value = config.CuratedOpenLibrarySubjects || '';
    page.querySelector('#ScanDiscogs').checked = config.ScanDiscogs;
    page.querySelector('#DiscogsToken').value = config.DiscogsToken || '';
    page.querySelector('#ScanMdbList').checked = config.ScanMdbList;
    page.querySelector('#MdbListApiKey').value = config.MdbListApiKey || '';
    page.querySelector('#ScanTraktLists').checked = config.ScanTraktLists;
    page.querySelector('#CuratedTraktListIds').value = config.CuratedTraktListIds || '';
    page.querySelector('#IncludeAvailability').checked = config.IncludeAvailability;
    page.querySelector('#AvailabilityCacheHours').value = config.AvailabilityCacheHours;
    page.querySelector('#TraktEnabled').checked = config.TraktEnabled;
    page.querySelector('#TraktClientId').value = config.TraktClientId || '';
    page.querySelector('#TvdbApiKey').value = config.TvdbApiKey || '';
    page.querySelector('#SearchUrlTemplate').value = config.SearchUrlTemplate || 'https://www.google.com/search?q={0}';
    page.querySelector('#MetadataCountryCode').value = config.MetadataCountryCode || '';
    page.querySelector('#MetadataLanguage').value = config.MetadataLanguage || '';
    page.querySelector('#TmdbApiKey').value = config.TmdbApiKey || '';
    page.querySelector('#WebhookUrl').value = config.WebhookUrl || '';
    page.querySelector('#DetailedApiLogging').checked = config.DetailedApiLogging;
    page.querySelector('#SeerrUrl').value = config.SeerrUrl || '';
    page.querySelector('#SeerrApiKey').value = config.SeerrApiKey || '';
    page.querySelector('#RadarrUrl').value = config.RadarrUrl || '';
    page.querySelector('#RadarrApiKey').value = config.RadarrApiKey || '';
    page.querySelector('#RadarrQualityProfileId').value = config.RadarrQualityProfileId || 0;
    page.querySelector('#RadarrRootFolderPath').value = config.RadarrRootFolderPath || '';
    page.querySelector('#SonarrUrl').value = config.SonarrUrl || '';
    page.querySelector('#SonarrApiKey').value = config.SonarrApiKey || '';
    page.querySelector('#SonarrQualityProfileId').value = config.SonarrQualityProfileId || 0;
    page.querySelector('#SonarrRootFolderPath').value = config.SonarrRootFolderPath || '';
    page.querySelector('#SonarrMonitor').value = config.SonarrMonitor || 'all';
    page.querySelector('#MaxRelatedPerItem').value = config.MaxRelatedPerItem;
    page.querySelector('#MinRecommendationVotes').value = config.MinRecommendationVotes;
    page.querySelector('#MaxMissingEpisodesPerShow').value = config.MaxMissingEpisodesPerShow;
    page.querySelector('#MaxFilmographyPeople').value = config.MaxFilmographyPeople;
    page.querySelector('#MinFilmographyVotes').value = config.MinFilmographyVotes;
    page.querySelector('#MaxCastBillingOrder').value = config.MaxCastBillingOrder;
    bindSettingsToggle(page, 'TraktEnabled', 'TraktClientId');
    // Freshly loaded values are not unsaved edits (assigning .value/.checked fires no events).
    page._settingsDirty = false;
}

function saveConfig(page, e) {
    if (e) { e.preventDefault(); }
    var form = page.querySelector('#MindTheGapsConfigForm');

    // Validate a secret only when its cross-check is on; bypass entirely when off.
    if (form.querySelector('#TraktEnabled').checked && !form.querySelector('#TraktClientId').value.trim()) {
        Dashboard.alert('Enter a Trakt client id, or turn off the Trakt cross-check.');
        return false;
    }
    Dashboard.showLoadingMsg();
    ApiClient.getPluginConfiguration(pluginId).then(function (config) {
        config.ScanCollections = form.querySelector('#ScanCollections').checked;
        config.ScanSeries = form.querySelector('#ScanSeries').checked;
        config.ScanPeople = form.querySelector('#ScanPeople').checked;
        config.ScanRecommendations = form.querySelector('#ScanRecommendations').checked;
        config.ScanCuratedSets = form.querySelector('#ScanCuratedSets').checked;
        config.ScanTmdbLists = form.querySelector('#ScanTmdbLists').checked;
        config.AutoSeedStudios = form.querySelector('#AutoSeedStudios').checked;
        config.CuratedTmdbListIds = form.querySelector('#CuratedTmdbListIds').value.trim();
        // The chips hold the ids; nothing else to persist for curated sets.
        var chips = page._chipState || {};
        config.CuratedCompanyIds = chips.studio ? chips.studio.ids() : (config.CuratedCompanyIds || '');
        config.CuratedKeywordIds = chips.keyword ? chips.keyword.ids() : (config.CuratedKeywordIds || '');
        config.ScanMusic = form.querySelector('#ScanMusic').checked;
        config.ScanBooks = form.querySelector('#ScanBooks').checked;
        config.ScanCuratedBooks = form.querySelector('#ScanCuratedBooks').checked;
        config.CuratedOpenLibrarySubjects = form.querySelector('#CuratedOpenLibrarySubjects').value.trim();
        config.ScanDiscogs = form.querySelector('#ScanDiscogs').checked;
        config.DiscogsToken = form.querySelector('#DiscogsToken').value;
        config.DiscogsLabelIds = chips.label ? chips.label.ids() : (config.DiscogsLabelIds || '');
        config.ScanMdbList = form.querySelector('#ScanMdbList').checked;
        config.MdbListApiKey = form.querySelector('#MdbListApiKey').value.trim();
        config.MdbListListIds = chips.mdblist ? chips.mdblist.ids() : (config.MdbListListIds || '');
        config.ScanTraktLists = form.querySelector('#ScanTraktLists').checked;
        config.CuratedTraktListIds = form.querySelector('#CuratedTraktListIds').value.trim();
        config.IncludeAvailability = form.querySelector('#IncludeAvailability').checked;
        config.AvailabilityCacheHours = parseInt(form.querySelector('#AvailabilityCacheHours').value || '24', 10);
        config.TraktEnabled = form.querySelector('#TraktEnabled').checked;
        config.TraktClientId = form.querySelector('#TraktClientId').value;
        config.TvdbApiKey = form.querySelector('#TvdbApiKey').value;
        config.SearchUrlTemplate = form.querySelector('#SearchUrlTemplate').value.trim();
        config.MetadataCountryCode = form.querySelector('#MetadataCountryCode').value;
        config.MetadataLanguage = form.querySelector('#MetadataLanguage').value;
        config.TmdbApiKey = form.querySelector('#TmdbApiKey').value;
        config.WebhookUrl = form.querySelector('#WebhookUrl').value;
        config.DetailedApiLogging = form.querySelector('#DetailedApiLogging').checked;
        config.SeerrUrl = form.querySelector('#SeerrUrl').value.trim();
        config.SeerrApiKey = form.querySelector('#SeerrApiKey').value.trim();
        config.RadarrUrl = form.querySelector('#RadarrUrl').value.trim();
        config.RadarrApiKey = form.querySelector('#RadarrApiKey').value.trim();
        config.RadarrQualityProfileId = parseInt(form.querySelector('#RadarrQualityProfileId').value || '0', 10);
        config.RadarrRootFolderPath = form.querySelector('#RadarrRootFolderPath').value.trim();
        config.SonarrUrl = form.querySelector('#SonarrUrl').value.trim();
        config.SonarrApiKey = form.querySelector('#SonarrApiKey').value.trim();
        config.SonarrQualityProfileId = parseInt(form.querySelector('#SonarrQualityProfileId').value || '0', 10);
        config.SonarrRootFolderPath = form.querySelector('#SonarrRootFolderPath').value.trim();
        config.SonarrMonitor = form.querySelector('#SonarrMonitor').value.trim() || 'all';
        config.MaxRelatedPerItem = parseInt(form.querySelector('#MaxRelatedPerItem').value || '0', 10);
        config.MinRecommendationVotes = parseInt(form.querySelector('#MinRecommendationVotes').value || '0', 10);
        config.MaxMissingEpisodesPerShow = parseInt(form.querySelector('#MaxMissingEpisodesPerShow').value || '0', 10);
        config.MaxFilmographyPeople = parseInt(form.querySelector('#MaxFilmographyPeople').value || '1000', 10);
        config.MinFilmographyVotes = parseInt(form.querySelector('#MinFilmographyVotes').value || '0', 10);
        config.MaxCastBillingOrder = parseInt(form.querySelector('#MaxCastBillingOrder').value || '0', 10);
        ApiClient.updatePluginConfiguration(pluginId, config).then(function (result) {
            page._settingsDirty = false;
            Dashboard.processPluginConfigurationUpdateResult(result);
        });
    });
    return false;
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
            var x = wrap('button', {
                type: 'button', 'class': 'cgChipX', 'data-i': i,
                'aria-label': 'Remove ' + (c.Name || ''), title: 'Remove ' + (c.Name || '')
            }, '&times;');
            return wrap('span', { 'class': 'cgChip', role: 'listitem' }, esc(c.Name) + x);
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
            ? state.items.map(function (it, i) {
                return h('div', {
                    'class': 'cgSuggestItem' + (i === state.sel ? ' cgSuggestSel' : ''), role: 'option',
                    id: suggestId + '-opt-' + i, 'aria-selected': i === state.sel ? 'true' : 'false', 'data-i': i
                }, it.Name).outerHTML;
            }).join('')
            : h('div', { 'class': 'cgSuggestEmpty' }, 'No matches').outerHTML;
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
    setupChips(page, 'studio', 'cgStudioBox', 'cgStudioChips', 'cgStudioInput', 'cgStudioSuggest');
    setupChips(page, 'keyword', 'cgKeywordBox', 'cgKeywordChips', 'cgKeywordInput', 'cgKeywordSuggest');
    setupChips(page, 'label', 'cgLabelBox', 'cgLabelChips', 'cgLabelInput', 'cgLabelSuggest');
    setupChips(page, 'mdblist', 'cgMdbListBox', 'cgMdbListChips', 'cgMdbListInput', 'cgMdbListSuggest');
    // Reveal/hide a secret field. The inputs are type=text masked by the cgSecret CSS class, not
    // type=password, so the browser never treats the settings form as a login and never offers to
    // save the keys. Reveal toggles the mask rather than the input type.
    var revealBtns = page.querySelectorAll('.cgReveal');
    for (var rb = 0; rb < revealBtns.length; rb++) {
        revealBtns[rb].addEventListener('click', function () {
            var input = page.querySelector('#' + this.getAttribute('data-target'));
            if (!input) { return; }
            var shown = input.classList.toggle('cgSecretShown');
            var span = this.querySelector('span');
            if (span) { span.textContent = shown ? 'Hide' : 'Show'; }
        });
    }
    // Bind the save to the live form (per page show), so the native form never GET-submits
    // (which would leak API keys into the URL).
    page.querySelector('#MindTheGapsConfigForm').addEventListener('submit', function (e) { saveConfig(page, e); });
}
document.querySelector('#MindTheGapsSettingsPage').addEventListener('pageshow', function () {
    var page = this;
    // Jellyfin keeps the page element and re-fires pageshow on every navigation, so bind once or the
    // listeners stack and a delegated handler fires N times. The config is re-read on every show.
    if (!page._cgBound) {
        page._cgBound = true;
        bindSettings(page);
        page.querySelector('#cgBackToReport').addEventListener('click', function () {
            // Leaving with unsaved edits: warn before discarding.
            if (page._settingsDirty && !window.confirm('You have unsaved settings changes that will be lost. Click Cancel to go back and Save, or OK to discard them.')) {
                return;
            }
            page._settingsDirty = false;
            Dashboard.navigate('configurationpage?name=MindTheGaps');
        });
    }
    Dashboard.showLoadingMsg();
    ApiClient.getPluginConfiguration(pluginId).then(function (config) {
        loadConfig(page, config);
        Dashboard.hideLoadingMsg();
    });
});
