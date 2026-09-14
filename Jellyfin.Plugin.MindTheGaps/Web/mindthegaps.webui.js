// Mind the Gaps: the web UI surfaces.
//
// Loaded by jellyfin-web's index.html (the plugin adds the script tag as the page is served). Renders, where
// the matching surface is switched on in the plugin settings:
//   - on a Person page, a "Missing from your library" section of the person's unowned movies and shows;
//   - on a Movie or Series page, a "More like this you don't have" row of unowned similar titles;
//   - on the home screen, a "Discover" row of the recommendations the scan has accumulated, and a "Want to
//     watch" row of the titles bookmarked into the plugin's todo list.
// A card opens a detail dialog (TMDB's summary, rating, links, and for an administrator a quality-profile
// choice and a Download button); the button under a card downloads with the configured default profile.
// Talks to the server only through the web client's own ApiClient, so it inherits the signed-in user's
// session and base URL. Touches nothing but the elements it owns, and removes them again before rendering
// a different view.
(function () {
    'use strict';

    var PERSON_ID = 'mtgPersonMissing';
    var RELATED_ID = 'mtgRelatedMissing';
    var HOME_ID = 'mtgHomeDiscover';
    var WANT_ID = 'mtgHomeWant';
    var surfaceFlags = {};
    var DIALOG_ID = 'mtgDialog';
    var pending = 0;
    var profilesPromise = null;
    var surfacesPromise = null;

    function h(tag, attrs, text) {
        var el = document.createElement(tag);
        if (attrs) {
            Object.keys(attrs).forEach(function (k) {
                if (attrs[k] != null) { el.setAttribute(k, attrs[k]); }
            });
        }
        if (text != null) { el.textContent = text; }
        return el;
    }

    function isTv() {
        return document.documentElement.classList.contains('layout-tv');
    }

    function safeImage(url) {
        return url && /^https:\/\//i.test(url) ? 'url("' + url.replace(/["\\]/g, '') + '")' : null;
    }

    function safeHref(url) {
        return url && /^https:\/\//i.test(url) ? url : null;
    }

    function alertUser(message) {
        if (window.Dashboard && Dashboard.alert) { Dashboard.alert(message); } else { window.alert(message); }
    }

    function api(type, path, params) {
        return ApiClient.ajax({ type: type, url: ApiClient.getUrl(path, params || {}), dataType: 'json' });
    }

    function surfaces() {
        if (!surfacesPromise) {
            surfacesPromise = api('GET', 'MindTheGaps/WebUi/Surfaces').then(function (s) { surfaceFlags = s || {}; return surfaceFlags; }, function () { surfacesPromise = null; return {}; });
        }
        return surfacesPromise;
    }

    // The details page is `#/details?id=…` on a hash router; read the id from the hash, not the search.
    function itemIdFromLocation() {
        var hash = window.location.hash || '';
        var q = hash.indexOf('?');
        if (hash.indexOf('/details') < 0 || q < 0) { return null; }
        return new URLSearchParams(hash.slice(q + 1)).get('id');
    }

    function remove(page, id) {
        var old = page.querySelector('#' + id);
        if (old) { old.parentNode.removeChild(old); }
    }

    // ---- Send ----

    // ctx: { source: 'person'|'item'|'home', sourceId: guid|null, canSend: function(kind) }
    function send(ctx, item, btn, profileId, onDone) {
        btn.disabled = true;
        var was = btn.textContent;
        btn.textContent = 'Sending\u2026';
        var params = { source: ctx.source, gapId: item.GapId };
        if (ctx.sourceId) { params.sourceId = ctx.sourceId; }
        if (profileId) { params.qualityProfileId = profileId; }
        api('POST', 'MindTheGaps/WebUi/Send', params).then(function (result) {
            if (result && result.Success) {
                btn.textContent = 'Sent';
                btn.classList.add('mtgSent');
                if (onDone) { onDone(); }
            } else {
                btn.textContent = was;
                btn.disabled = false;
                alertUser((result && result.Message) || 'Send failed.');
            }
        }, function () {
            btn.textContent = was;
            btn.disabled = false;
            alertUser('Could not reach the server.');
        });
    }

    function markSent(item) {
        var buttons = document.querySelectorAll('[data-gapid="' + item.GapId + '"] .mtgSendButton');
        Array.prototype.forEach.call(buttons, function (b) {
            b.textContent = 'Sent';
            b.disabled = true;
            b.classList.add('mtgSent');
        });
    }

    // ---- Want to watch ----

    function canEditWant() {
        return !!(surfaceFlags.WantToWatch && surfaceFlags.CanEditWantToWatch);
    }

    function wantLabel(wanted) {
        return wanted ? 'On your want-to-watch list' : 'Add to want-to-watch';
    }

    // Toggles the title on the list and reflects the new state on every card and dialog showing it.
    function toggleWant(ctx, item, btn) {
        btn.disabled = true;
        var params = { source: ctx.source, gapId: item.GapId, wanted: !item.Wanted };
        if (ctx.sourceId) { params.sourceId = ctx.sourceId; }
        api('POST', 'MindTheGaps/WebUi/WantToWatch', params).then(function (state) {
            item.Wanted = !!(state && state.Wanted);
            markWanted(item);
            btn.disabled = false;
            // The home row lists the bookmarks; a change made elsewhere on the same page refreshes it.
            var page = document.querySelector('#indexPage');
            if (page) { loadWantRow(page.querySelector('#homeTab .sections')); }
        }, function () {
            btn.disabled = false;
            alertUser('Could not update the want-to-watch list.');
        });
    }

    function markWanted(item) {
        var buttons = document.querySelectorAll('[data-gapid="' + item.GapId + '"] .mtgWantButton, .mtgDialog[data-gapid="' + item.GapId + '"] .mtgWantButton');
        Array.prototype.forEach.call(buttons, function (b) {
            b.classList.toggle('mtgWanted', item.Wanted);
            b.setAttribute('title', wantLabel(item.Wanted));
            b.setAttribute('aria-label', wantLabel(item.Wanted));
            b.setAttribute('aria-pressed', item.Wanted ? 'true' : 'false');
            var icon = b.querySelector('.material-icons');
            if (icon) { icon.className = 'material-icons ' + (item.Wanted ? 'bookmark' : 'bookmark_border'); }
            var text = b.querySelector('.mtgWantText');
            if (text) { text.textContent = item.Wanted ? 'Want to watch \u2713' : 'Want to watch'; }
        });
    }

    function wantButton(ctx, item, withText) {
        var btn = h('button', {
            'is': withText ? 'emby-button' : 'paper-icon-button-light',
            'type': 'button',
            'tabindex': !withText && isTv() ? '-1' : null,
            'class': (withText ? 'raised mtgWantButton mtgWantButtonText' : 'mtgWantButton') + (item.Wanted ? ' mtgWanted' : ''),
            'title': wantLabel(item.Wanted),
            'aria-label': wantLabel(item.Wanted),
            'aria-pressed': item.Wanted ? 'true' : 'false'
        });
        btn.appendChild(h('span', { 'class': 'material-icons ' + (item.Wanted ? 'bookmark' : 'bookmark_border'), 'aria-hidden': 'true' }));
        if (withText) { btn.appendChild(h('span', { 'class': 'mtgWantText' }, item.Wanted ? 'Want to watch \u2713' : 'Want to watch')); }
        btn.addEventListener('click', function (e) {
            e.preventDefault();
            e.stopPropagation();
            toggleWant(ctx, item, btn);
        });
        return btn;
    }

    // A playlist bookmark for a card whose title is in the library (a search result).
    function ownedWantButton(itemId) {
        var btn = h('button', { 'is': 'paper-icon-button-light', 'type': 'button', 'class': 'mtgWantButton mtgHoverWant', 'data-itemid': itemId, 'tabindex': isTv() ? '-1' : null });
        btn.appendChild(h('span', { 'class': 'material-icons bookmark_border', 'aria-hidden': 'true' }));
        btn.setAttribute('title', WATCHLIST_ADD);
        btn.mtgWanted = false;
        btn.addEventListener('click', function (e) {
            e.preventDefault();
            e.stopPropagation();
            var next = !btn.mtgWanted;
            btn.disabled = true;
            setOwnedWanted(itemId, next).then(function () { btn.disabled = false; syncOwnedWanted(itemId, next); }, function () { btn.disabled = false; alertUser('Could not update your watchlist.'); });
        });
        ownedWanted(itemId).then(function (state) {
            btn.mtgWanted = !!state;
            btn.classList.toggle('mtgWanted', !!state);
            btn.setAttribute('title', state ? WATCHLIST_REMOVE : WATCHLIST_ADD);
            btn.querySelector('.material-icons').className = 'material-icons ' + (state ? 'bookmark' : 'bookmark_border');
        }, function () { /* leave unmarked */ });
        return btn;
    }

    // ---- Want to watch for owned titles: the user's own "Want to watch" playlist ----
    //
    // An owned title cannot go on the plugin's todo list (that list is of titles to acquire, and the
    // library check would mark it done at once), so the bookmark on a movie or series page toggles the
    // item in a Jellyfin playlist named "Want to watch" belonging to the signed-in user. Being a real
    // playlist, it shows under Playlists on every client, TV apps included. Created on first use.

    var PLAYLIST_NAME = 'Want to watch';
    var playlistIdPromise = null;

    function findPlaylistId(create) {
        if (!playlistIdPromise) {
            var userId = ApiClient.getCurrentUserId();
            playlistIdPromise = ApiClient.getItems(userId, { IncludeItemTypes: 'Playlist', Recursive: true, SearchTerm: PLAYLIST_NAME, Fields: 'Name' })
                .then(function (result) {
                    var hit = (result.Items || []).filter(function (p) { return (p.Name || '').toLowerCase() === PLAYLIST_NAME.toLowerCase(); })[0];
                    if (hit) { return hit.Id; }
                    if (!create) { return null; }
                    return ApiClient.ajax({
                        type: 'POST',
                        url: ApiClient.getUrl('Playlists'),
                        data: JSON.stringify({ Name: PLAYLIST_NAME, UserId: userId, MediaType: 'Video', Ids: [] }),
                        contentType: 'application/json',
                        dataType: 'json'
                    }).then(function (created) { return created.Id; });
                })
                .then(function (id) {
                    if (!id) { playlistIdPromise = null; }
                    return id;
                }, function () { playlistIdPromise = null; return null; });
        }
        return playlistIdPromise;
    }

    // The playlist's entries: each carries the library item id and the entry id a removal needs.
    function playlistEntries(playlistId) {
        return api('GET', 'Playlists/' + playlistId + '/Items', { UserId: ApiClient.getCurrentUserId(), Fields: 'ProductionYear' })
            .then(function (r) { return r.Items || []; });
    }

    // The playlist's membership, item id -> entry id, kept briefly so hovering a row of cards is one call.
    var membershipPromise = null;
    var membershipAt = 0;

    function ownedMembership() {
        if (!membershipPromise || Date.now() - membershipAt > 20000) {
            membershipAt = Date.now();
            membershipPromise = findPlaylistId(false).then(function (pid) {
                if (!pid) { return { playlistId: null, entries: {} }; }
                return playlistEntries(pid).then(function (items) {
                    var entries = {};
                    items.forEach(function (i) { entries[i.Id] = i.PlaylistItemId; });
                    return { playlistId: pid, entries: entries };
                });
            }).catch(function () { membershipPromise = null; return { playlistId: null, entries: {} }; });
        }
        return membershipPromise;
    }

    function forgetMembership() {
        membershipPromise = null;
    }

    function ownedWanted(itemId) {
        return ownedMembership().then(function (m) {
            return m.entries[itemId] ? { playlistId: m.playlistId, entryId: m.entries[itemId] } : null;
        });
    }

    function setOwnedWanted(itemId, wanted) {
        var done = function (r) { forgetMembership(); return r; };
        if (wanted) {
            return findPlaylistId(true).then(function (pid) {
                return ApiClient.ajax({ type: 'POST', url: ApiClient.getUrl('Playlists/' + pid + '/Items', { Ids: itemId, UserId: ApiClient.getCurrentUserId() }) });
            }).then(done);
        }
        return ownedWanted(itemId).then(function (state) {
            if (!state) { return; }
            return ApiClient.ajax({ type: 'DELETE', url: ApiClient.getUrl('Playlists/' + state.playlistId + '/Items', { EntryIds: state.entryId }) });
        }).then(done);
    }

    // Reflects a change on every element showing this owned title: the detail header button, hover
    // bookmarks on cards, and the home row on its next show.
    function syncOwnedWanted(itemId, wanted) {
        Array.prototype.forEach.call(document.querySelectorAll('.mtgWantDetail[data-itemid="' + itemId + '"], .mtgHoverWant[data-itemid="' + itemId + '"]'), function (b) {
            if (b.classList.contains('mtgWantDetail')) { setDetailButtonState(b, wanted); } else { setHoverButtonState(b, wanted); }
        });
    }

    var WATCHLIST_ADD = 'Add to watchlist';
    var WATCHLIST_REMOVE = 'Remove from watchlist';

    // ---- Hover bookmark on library cards (desktop) ----
    //
    // jellyfin-web's cards carry a hover overlay with play, played, favourite and more buttons as plain
    // markup. On the first hover of a movie or series card a bookmark is added beside the favourite, in
    // the overlay's own button markup, toggling the title in the want-to-watch playlist.

    function setHoverButtonState(btn, wanted) {
        btn.setAttribute('title', wanted ? WATCHLIST_REMOVE : WATCHLIST_ADD);
        btn.setAttribute('aria-label', wanted ? WATCHLIST_REMOVE : WATCHLIST_ADD);
        btn.mtgWanted = wanted;
        btn.classList.toggle('mtgWanted', wanted);
        var icon = btn.querySelector('.material-icons');
        var overlay = icon.classList.contains('cardOverlayButtonIcon');
        icon.className = 'material-icons ' + (overlay ? 'cardOverlayButtonIcon cardOverlayButtonIcon-hover ' : '') + (wanted ? 'bookmark' : 'bookmark_border');
    }

    function onCardHover(e) {
        if (!surfaceFlags.WantToWatch || !e.target || !e.target.closest) { return; }
        var card = e.target.closest('.card[data-id][data-type]');
        if (!card || card.mtgHoverDone) { return; }
        var type = card.getAttribute('data-type');
        if (type !== 'Movie' && type !== 'Series') { return; }
        var bar = card.querySelector('.cardOverlayContainer .cardOverlayButton-br');
        if (!bar) { return; }
        card.mtgHoverDone = true;
        var itemId = card.getAttribute('data-id');
        var btn = h('button', { 'is': 'paper-icon-button-light', 'type': 'button', 'data-action': 'none', 'data-itemid': itemId, 'class': 'cardOverlayButton cardOverlayButton-hover itemAction paper-icon-button-light mtgHoverWant' });
        btn.appendChild(h('span', { 'class': 'material-icons cardOverlayButtonIcon cardOverlayButtonIcon-hover bookmark_border', 'aria-hidden': 'true' }));
        setHoverButtonState(btn, false);
        btn.addEventListener('click', function (ev) {
            ev.preventDefault();
            ev.stopPropagation();
            var next = !btn.mtgWanted;
            btn.disabled = true;
            setOwnedWanted(itemId, next).then(function () { btn.disabled = false; syncOwnedWanted(itemId, next); }, function () { btn.disabled = false; alertUser('Could not update your watchlist.'); });
        });
        var more = bar.querySelector('[data-action="menu"]');
        bar.insertBefore(btn, more || null);
        ownedWanted(itemId).then(function (state) { setHoverButtonState(btn, !!state); }, function () { /* leave unmarked */ });
    }

    document.addEventListener('mouseover', onCardHover, true);

    // ---- "Add to watchlist" in the more menu ----
    //
    // The kebab opens an action sheet that is appended to the page after the click. The click records
    // which movie or series it was for (the card, or the page's own item), and when a sheet appears
    // shortly after, an entry is added at its end in the sheet's own item markup. The sheet closes itself
    // on any item click, so the entry only has to do the toggle.

    var pendingMenu = null;
    var currentPageItem = null;

    function onMenuClick(e) {
        if (!surfaceFlags.WantToWatch || !e.target || !e.target.closest) { return; }
        var btn = e.target.closest('[data-action="menu"], .btnMoreCommands, .btnCardOptions');
        if (!btn) { return; }
        var card = btn.closest('.card[data-id][data-type]');
        var id = null;
        var type = null;
        if (card) {
            id = card.getAttribute('data-id');
            type = card.getAttribute('data-type');
        } else if (btn.classList.contains('btnMoreCommands') && currentPageItem) {
            id = currentPageItem.Id;
            type = currentPageItem.Type;
        }
        pendingMenu = id && (type === 'Movie' || type === 'Series') ? { id: id, at: Date.now() } : null;
    }

    function addWatchlistMenuItem(sheet) {
        if (!pendingMenu || Date.now() - pendingMenu.at > 3000) { return; }
        var scroller = sheet.querySelector('.actionSheetScroller');
        if (!scroller || scroller.querySelector('.mtgMenuWant')) { return; }
        var itemId = pendingMenu.id;
        pendingMenu = null;
        var btn = h('button', { 'is': 'emby-button', 'type': 'button', 'class': 'listItem listItem-button actionSheetMenuItem mtgMenuWant', 'data-id': 'mtgwatchlist' });
        var icon = h('span', { 'class': 'actionsheetMenuItemIcon listItemIcon listItemIcon-transparent material-icons bookmark_border', 'aria-hidden': 'true' });
        btn.appendChild(icon);
        var body = h('div', { 'class': 'listItemBody actionsheetListItemBody' });
        var text = h('div', { 'class': 'listItemBodyText actionSheetItemText' }, WATCHLIST_ADD);
        body.appendChild(text);
        btn.appendChild(body);
        btn.mtgWanted = false;
        btn.addEventListener('click', function () {
            var next = !btn.mtgWanted;
            setOwnedWanted(itemId, next).then(function () { syncOwnedWanted(itemId, next); }, function () { alertUser('Could not update your watchlist.'); });
        });
        // Beside the sheet's own list actions when it has them, else at the end.
        var after = scroller.querySelector('.actionSheetMenuItem[data-id="playlist"]') || scroller.querySelector('.actionSheetMenuItem[data-id="addtocollection"]');
        if (after && after.nextSibling) { scroller.insertBefore(btn, after.nextSibling); } else { scroller.appendChild(btn); }
        ownedWanted(itemId).then(function (state) {
            btn.mtgWanted = !!state;
            text.textContent = state ? WATCHLIST_REMOVE : WATCHLIST_ADD;
            icon.className = 'actionsheetMenuItemIcon listItemIcon listItemIcon-transparent material-icons ' + (state ? 'bookmark' : 'bookmark_border');
        }, function () { /* leave as add */ });
    }

    document.addEventListener('click', onMenuClick, true);
    new MutationObserver(function (records) {
        for (var i = 0; i < records.length; i++) {
            for (var j = 0; j < records[i].addedNodes.length; j++) {
                var n = records[i].addedNodes[j];
                if (n.nodeType === 1 && n.querySelector && n.querySelector('.actionSheetScroller')) { addWatchlistMenuItem(n); }
            }
        }
    }).observe(document.body, { childList: true });

    var DETAIL_WANT_CLASS = 'mtgWantDetail';

    function setDetailButtonState(btn, wanted) {
        btn.setAttribute('title', wanted ? WATCHLIST_REMOVE : WATCHLIST_ADD);
        btn.setAttribute('aria-label', wanted ? WATCHLIST_REMOVE : WATCHLIST_ADD);
        btn.setAttribute('aria-pressed', wanted ? 'true' : 'false');
        btn.mtgWanted = wanted;
        var icon = btn.querySelector('.material-icons');
        icon.className = 'material-icons detailButton-icon ' + (wanted ? 'bookmark' : 'bookmark_border');
    }

    // The bookmark in the detail header, to the left of the "more" button, in the same markup as its
    // neighbours so it takes the same size and TV focus style.
    function renderDetailWant(page, itemId) {
        var old = page.querySelector('.' + DETAIL_WANT_CLASS);
        if (old) { old.parentNode.removeChild(old); }
        var more = page.querySelector('.mainDetailButtons .btnMoreCommands');
        var host = more ? more.parentNode : page.querySelector('.mainDetailButtons');
        if (!host) { return; }
        var btn = h('button', { 'is': 'emby-button', 'type': 'button', 'class': 'button-flat detailButton ' + DETAIL_WANT_CLASS, 'data-itemid': itemId });
        var content = h('div', { 'class': 'detailButton-content' });
        content.appendChild(h('span', { 'class': 'material-icons detailButton-icon bookmark_border', 'aria-hidden': 'true' }));
        btn.appendChild(content);
        setDetailButtonState(btn, false);
        btn.addEventListener('click', function () {
            btn.disabled = true;
            var next = !btn.mtgWanted;
            setOwnedWanted(itemId, next).then(function () {
                syncOwnedWanted(itemId, next);
                btn.disabled = false;
            }, function () {
                btn.disabled = false;
                alertUser('Could not update your want-to-watch playlist.');
            });
        });
        host.insertBefore(btn, more || null);
        ownedWanted(itemId).then(function (state) { setDetailButtonState(btn, !!state); }, function () { /* leave unmarked */ });
    }

    // ---- Detail dialog ----

    // The dialog is a history entry, as jellyfin-web's own dialogs are: opening pushes one on the same URL,
    // and Back, however the client delivers it (a key, the Android app calling the router's back natively,
    // the browser button), pops it, which closes the dialog. Closing by any other means pops it ourselves.
    // The router keeps its position in history.state.idx; the pushed state carries idx + 1, as the router's
    // own push would, so its back arithmetic is unaffected.
    function dialogState() {
        var current = window.history.state || {};
        var idx = typeof current.idx === 'number' ? current.idx + 1 : 1;
        return { idx: idx, mtgDialog: true };
    }

    function closeDialog(fromHistory) {
        var dlg = document.getElementById(DIALOG_ID);
        if (!dlg) { return; }
        var restore = dlg.mtgRestoreFocus;
        document.removeEventListener('keydown', onDialogKey, true);
        document.removeEventListener('focusin', keepFocusInDialog, true);
        dlg.parentNode.removeChild(dlg);
        if (restore && restore.focus) { restore.focus(); }
        if (!fromHistory && window.history.state && window.history.state.mtgDialog) {
            window.history.back();
        }
    }

    window.addEventListener('popstate', function () {
        if (document.getElementById(DIALOG_ID)) { closeDialog(true); }
    });

    // Anything that still manages to focus outside the dialog (a mouse, a scroller's own handling) is
    // pulled back to the dialog's first control.
    function keepFocusInDialog(e) {
        var dlg = document.getElementById(DIALOG_ID);
        if (!dlg || dlg.contains(e.target)) { return; }
        var items = dialogFocusables(dlg);
        if (items.length) { items[0].focus(); }
    }

    // Back on TV remotes arrives under several names and codes (Tizen 10009, webOS 461, Android 4).
    var BACK_KEYS = { 'Escape': 1, 'GoBack': 1, 'BrowserBack': 1, 'Back': 1, 'XF86Back': 1 };
    var BACK_CODES = { 27: 1, 10009: 1, 461: 1, 4: 1 };

    function dialogFocusables(dlg) {
        return Array.prototype.filter.call(dlg.querySelectorAll('button, a[href], select, input'), function (el) {
            return !el.disabled && el.offsetParent !== null && el.getAttribute('tabindex') !== '-1';
        });
    }

    // While the dialog is open it owns the keyboard: Back closes it, and on a TV the arrows only move
    // between the dialog's own controls, so jellyfin-web's spatial navigation cannot reach the page behind.
    function onDialogKey(e) {
        var dlg = document.getElementById(DIALOG_ID);
        if (!dlg) { return; }
        var tag = e.target && e.target.tagName;
        var typing = tag === 'INPUT' || tag === 'SELECT';
        if (BACK_KEYS[e.key] || BACK_CODES[e.keyCode] || (e.key === 'Backspace' && !typing)) {
            e.preventDefault();
            e.stopPropagation();
            closeDialog();
            return;
        }

        var key = keyName(e);
        if (!key) { return; }
        // A focused select changes its value with up and down, and a text field moves its caret with left and
        // right; leave those to them.
        if (tag === 'SELECT' && (key === 'ArrowUp' || key === 'ArrowDown')) { e.stopPropagation(); return; }
        if (tag === 'INPUT' && (key === 'ArrowLeft' || key === 'ArrowRight')) { e.stopPropagation(); return; }

        var close = dlg.querySelector('.mtgClose');
        var items = dialogFocusables(dlg).filter(function (el) { return el !== close; });
        var active = document.activeElement;
        var index = items.indexOf(active);
        var target = null;
        if (key === 'ArrowUp') {
            // Up is always the way to the X in the corner.
            target = active === close ? null : close;
        } else if (key === 'ArrowDown') {
            target = active === close || index < 0 ? items[0] : items[Math.min(index + 1, items.length - 1)];
        } else if (key === 'ArrowRight') {
            target = index < 0 ? items[0] : items[Math.min(index + 1, items.length - 1)];
        } else if (key === 'ArrowLeft') {
            target = index < 0 ? items[items.length - 1] : items[Math.max(index - 1, 0)];
        }
        e.preventDefault();
        e.stopPropagation();
        if (target) { target.focus(); }
    }

    function loadProfiles() {
        if (!profilesPromise) {
            profilesPromise = api('GET', 'MindTheGaps/WebUi/Profiles').catch(function () { profilesPromise = null; return null; });
        }
        return profilesPromise;
    }

    function metaLine(d) {
        var parts = [];
        if (d.Year) { parts.push(String(d.Year)); }
        if (d.Kind === 'Series') {
            if (d.Seasons) { parts.push(d.Seasons + (d.Seasons === 1 ? ' season' : ' seasons')); }
            if (d.Episodes) { parts.push(d.Episodes + (d.Episodes === 1 ? ' episode' : ' episodes')); }
            if (d.Network) { parts.push(d.Network); }
        }
        if (d.RuntimeMinutes) { parts.push(d.RuntimeMinutes + ' min' + (d.Kind === 'Series' ? ' / ep' : '')); }
        if (d.Rating != null) { parts.push('\u2605 ' + d.Rating.toFixed(1) + (d.VoteCount ? ' (' + d.VoteCount + ')' : '')); }
        if (d.Status && d.Status !== 'Released') { parts.push(d.Status); }
        return parts.join(' \u00b7 ');
    }

    // Opens a box as the one modal overlay: focus trapped inside, Back and Escape close it, a history entry
    // so Back works however the client delivers it. Returns the close button so callers can order focus.
    function openOverlay(box, label) {
        closeDialog();
        // "dialogContainer" is the class jellyfin-web's router checks before it will go back on a start page
        // (the home screen): without it, Back on a TV does nothing there, or exits the app.
        var overlay = h('div', { 'id': DIALOG_ID, 'class': 'dialogContainer mtgOverlay', 'role': 'dialog', 'aria-modal': 'true', 'aria-label': label });
        overlay.mtgRestoreFocus = document.activeElement;
        overlay.addEventListener('click', function (e) { if (e.target === overlay) { closeDialog(); } });
        var close = h('button', { 'is': 'paper-icon-button-light', 'type': 'button', 'class': 'mtgClose', 'title': 'Close', 'aria-label': 'Close' });
        close.appendChild(h('span', { 'class': 'material-icons close', 'aria-hidden': 'true' }));
        close.addEventListener('click', closeDialog);
        box.insertBefore(close, box.firstChild);
        overlay.appendChild(box);
        document.body.appendChild(overlay);
        document.addEventListener('keydown', onDialogKey, true);
        document.addEventListener('focusin', keepFocusInDialog, true);
        try { window.history.pushState(dialogState(), '', window.location.href); } catch (err) { /* history unavailable: Escape and X still close */ }
        return close;
    }

    function renderDialog(ctx, item, detail, profiles) {
        var canSend = ctx.canSend(item.Kind) && !item.InArr;
        var box = h('div', { 'class': 'mtgDialog', 'data-gapid': item.GapId });
        var bg = safeImage(detail.BackdropUrl);
        if (bg) { box.style.backgroundImage = 'linear-gradient(rgba(16,16,16,.88), rgba(16,16,16,.97)), ' + bg; }

        var body = h('div', { 'class': 'mtgDialogBody' });
        var poster = h('div', { 'class': 'mtgDialogPoster' });
        var p = safeImage(detail.PosterUrl);
        if (p) { poster.style.backgroundImage = p; }
        body.appendChild(poster);

        var text = h('div', { 'class': 'mtgDialogText' });
        text.appendChild(h('h2', { 'class': 'mtgDialogTitle' }, detail.Title + (detail.Year ? ' (' + detail.Year + ')' : '')));
        var meta = metaLine(detail);
        if (meta) { text.appendChild(h('div', { 'class': 'mtgDialogMeta' }, meta)); }
        if (detail.Genres && detail.Genres.length) { text.appendChild(h('div', { 'class': 'mtgDialogMeta' }, detail.Genres.join(', '))); }
        if (detail.Role) { text.appendChild(h('div', { 'class': 'mtgDialogRole' }, 'Credit: ' + detail.Role)); }
        if (detail.Because) { text.appendChild(h('div', { 'class': 'mtgDialogRole' }, detail.Because)); }
        if (detail.Tagline) { text.appendChild(h('div', { 'class': 'mtgDialogTagline' }, detail.Tagline)); }
        text.appendChild(h('p', { 'class': 'mtgDialogOverview' }, detail.Overview || 'No overview on TMDB.'));

        var links = h('div', { 'class': 'mtgDialogLinks' });
        [['TMDB', detail.TmdbUrl], ['IMDb', detail.ImdbUrl], ['Trailer', detail.TrailerUrl]].forEach(function (pair) {
            var href = safeHref(pair[1]);
            if (!href) { return; }
            links.appendChild(h('a', { 'is': 'emby-linkbutton', 'class': 'button-link', 'href': href, 'target': '_blank', 'rel': 'noopener noreferrer' }, pair[0]));
        });
        if (links.childNodes.length) { text.appendChild(links); }

        var actions = h('div', { 'class': 'mtgDialogActions' });
        var firstFocus = null;
        if (canEditWant()) {
            var want = wantButton(ctx, item, true);
            actions.appendChild(want);
            firstFocus = firstFocus || want;
        }
        if (item.InArr) {
            actions.appendChild(h('div', { 'class': 'mtgArrNote' }, arrLabel(item)));
        }
        if (canSend) {
            var list = profiles ? (item.Kind === 'Movie' ? profiles.Radarr : profiles.Sonarr) : null;
            var defaultId = profiles ? (item.Kind === 'Movie' ? profiles.RadarrDefault : profiles.SonarrDefault) : 0;
            var select = null;
            if (list && list.length) {
                var wrap = h('div', { 'class': 'selectContainer mtgProfile' });
                wrap.appendChild(h('label', { 'class': 'selectLabel', 'for': 'mtgProfileSelect' }, 'Quality profile'));
                select = h('select', { 'is': 'emby-select', 'id': 'mtgProfileSelect' });
                list.forEach(function (prof) {
                    var opt = h('option', { 'value': String(prof.Id) }, prof.Name + (prof.Id === defaultId ? ' (default)' : ''));
                    if (prof.Id === defaultId) { opt.selected = true; }
                    select.appendChild(opt);
                });
                wrap.appendChild(select);
                actions.appendChild(wrap);
            }
            var dl = h('button', { 'is': 'emby-button', 'type': 'button', 'class': 'raised button-submit mtgDialogDownload' }, 'Download Now');
            dl.addEventListener('click', function () {
                var profileId = select ? parseInt(select.value, 10) : 0;
                send(ctx, item, dl, profileId, function () { markSent(item); });
            });
            actions.appendChild(dl);
            firstFocus = firstFocus || dl;
        }
        text.appendChild(actions);
        body.appendChild(text);
        box.appendChild(body);
        var close = openOverlay(box, detail.Title);
        (firstFocus || close).focus();
    }

    // What a card says instead of Download Now when Radarr or Sonarr already has the title.
    function arrLabel(item) {
        if (item.ArrState === 'downloaded') { return 'In ' + item.InArr + ' (downloaded)'; }
        if (item.ArrState === 'monitored') { return 'In ' + item.InArr + ', waiting for release'; }
        return 'In ' + item.InArr + ' (not monitored)';
    }

    function openDetail(ctx, item) {
        var params = { source: ctx.source, gapId: item.GapId };
        if (ctx.sourceId) { params.sourceId = ctx.sourceId; }
        var loads = [api('GET', 'MindTheGaps/WebUi/Detail', params)];
        loads.push(ctx.canSend(item.Kind) ? loadProfiles() : Promise.resolve(null));
        Promise.all(loads).then(function (results) {
            renderDialog(ctx, item, results[0], results[1]);
        }, function () {
            alertUser('Could not load details for ' + item.Title + '.');
        });
    }

    // ---- Watchlist search: "Add a title" ----

    function openSearch() {
        var box = h('div', { 'class': 'mtgDialog mtgSearch' });
        var body = h('div', { 'class': 'mtgSearchBody' });
        body.appendChild(h('h2', { 'class': 'mtgDialogTitle' }, 'Add a title to your watchlist'));
        var form = h('form', { 'class': 'mtgSearchForm' });
        var field = h('div', { 'class': 'inputContainer mtgSearchField' });
        var input = h('input', { 'is': 'emby-input', 'type': 'search', 'id': 'mtgSearchInput', 'placeholder': 'Movie or show title', 'autocomplete': 'off' });
        field.appendChild(input);
        form.appendChild(field);
        var go = h('button', { 'is': 'emby-button', 'type': 'submit', 'class': 'raised button-submit' }, 'Search');
        form.appendChild(go);
        body.appendChild(form);
        var note = h('p', { 'class': 'mtgNote' }, 'Titles that are not out yet can be bookmarked and sent to Radarr or Sonarr to download on release.');
        body.appendChild(note);
        var results = h('div', { 'is': 'emby-itemscontainer', 'class': 'itemsContainer vertical-wrap mtgSearchResults' });
        body.appendChild(results);
        box.appendChild(body);
        var ctx = {
            source: 'search',
            sourceId: null,
            canSend: function (kind) { return !!(searchCanSend && (kind === 'Movie' ? searchCanSend.movies : searchCanSend.series)); }
        };
        form.addEventListener('submit', function (e) {
            e.preventDefault();
            var q = input.value.trim();
            if (q.length < 2) { return; }
            note.textContent = 'Searching\u2026';
            results.innerHTML = '';
            api('GET', 'MindTheGaps/WebUi/Search', { q: q }).then(function (titles) {
                note.textContent = titles.length ? '' : 'Nothing on TMDB matches that.';
                titles.forEach(function (t) { results.appendChild(card(ctx, t, false)); });
            }, function () { note.textContent = 'Search failed.'; });
        });
        openOverlay(box, 'Add a title');
        input.focus();
    }

    // Whether the viewer may send from search results: an administrator with the targets configured, learnt
    // from any row already loaded (the Discover or want rows carry the flags).
    var searchCanSend = null;

    function addTitleButton() {
        var btn = h('button', { 'is': 'emby-button', 'type': 'button', 'class': 'raised raised-mini mtgAddTitle' });
        btn.appendChild(h('span', { 'class': 'material-icons add', 'aria-hidden': 'true' }));
        btn.appendChild(h('span', null, 'Add a title'));
        btn.addEventListener('click', openSearch);
        return btn;
    }

    // ---- Cards ----

    // A poster card in jellyfin-web's own card markup, so a row matches the page's other rows in every
    // theme. In the TV layout the card itself is the focusable element, as jellyfin-web's cards are, so a
    // controller lands on the poster (and the section header stays in view) rather than on the button.
    // `overflow` picks the horizontal-scroller card size; otherwise the grid size.
    function card(ctx, item, overflow) {
        var tv = isTv();
        var shape = overflow ? 'overflowPortrait' : 'portrait';
        var el = h(tv ? 'button' : 'div', {
            'class': 'card ' + shape + 'Card mtgCard' + (tv ? ' show-focus show-animation' : ' card-hoverable'),
            'data-gapid': item.GapId,
            'type': tv ? 'button' : null,
            'aria-label': tv ? item.Title : null
        });
        var box = h('div', { 'class': 'cardBox cardBox-bottompadded' });
        var scalable = h('div', { 'class': 'cardScalable' });
        scalable.appendChild(h('div', { 'class': 'cardPadder cardPadder-' + shape }));
        var img = h(tv ? 'div' : 'button', {
            'class': 'cardImageContainer coveredImage cardContent mtgCardImage',
            'type': tv ? null : 'button',
            'aria-label': tv ? null : 'Details for ' + item.Title,
            'title': 'Details'
        });
        var bg = safeImage(item.ImageUrl);
        if (bg) {
            img.style.backgroundImage = bg;
        } else {
            img.classList.add('defaultCardBackground', 'defaultCardBackground1');
            img.appendChild(h('div', { 'class': 'cardText cardDefaultText' }, item.Title));
        }
        if (item.Upcoming) {
            img.appendChild(h('div', { 'class': 'mtgUpcomingBadge' }, 'Upcoming'));
        }
        scalable.appendChild(img);
        box.appendChild(scalable);

        var title = h('div', { 'class': 'cardText cardTextCentered cardText-first' });
        title.appendChild(h('bdi', null, item.Title));
        box.appendChild(title);
        var sub = item.Year ? String(item.Year) : '';
        if (item.OwnedItemId) { sub = sub ? sub + ' \u00b7 In your library' : 'In your library'; }
        if (item.Role) { sub = sub ? sub + ' \u00b7 ' + item.Role : item.Role; }
        if (item.Because && ctx.becauseOnCards) { sub = sub ? sub + ' \u00b7 ' + item.Because : item.Because; }
        var secondary = h('div', { 'class': 'cardText cardTextCentered cardText-secondary', 'title': sub });
        secondary.appendChild(h('bdi', null, sub));
        box.appendChild(secondary);

        var open = function (e) {
            e.preventDefault();
            e.stopPropagation();
            if (item.OwnedItemId) {
                closeDialog();
                window.location.hash = '#/details?id=' + encodeURIComponent(item.OwnedItemId) + '&serverId=' + encodeURIComponent(ApiClient.serverId());
                return;
            }
            openDetail(ctx, item);
        };
        (tv ? el : img).addEventListener('click', open);

        var actions = h('div', { 'class': 'mtgCardActions' });
        if (item.InArr) {
            actions.appendChild(h('span', { 'class': 'mtgArrBadge', 'title': arrLabel(item) }, 'In ' + item.InArr));
        } else if (ctx.canSend(item.Kind)) {
            var btn = h('button', { 'is': 'emby-button', 'type': 'button', 'class': 'raised raised-mini mtgSendButton', 'tabindex': tv ? '-1' : null }, 'Download Now');
            btn.addEventListener('click', function (e) {
                e.stopPropagation();
                send(ctx, item, btn, 0, function () { markSent(item); });
            });
            actions.appendChild(btn);
        }
        if (item.OwnedItemId && surfaceFlags.WantToWatch) {
            actions.appendChild(ownedWantButton(item.OwnedItemId));
        } else if (canEditWant() && ctx.wantOnCards !== false) {
            actions.appendChild(wantButton(ctx, item, false));
        }
        if (actions.childNodes.length) { box.appendChild(actions); }
        el.appendChild(box);
        return el;
    }

    // A wrapping grid of cards (the person page).
    function grid(ctx, name, items) {
        var section = h('div', { 'class': 'verticalSection' });
        var head = h('div', { 'class': 'sectionTitleContainer sectionTitleContainer-cards' });
        head.appendChild(h('h2', { 'class': 'sectionTitle sectionTitle-cards' }, name + ' (' + items.length + ')'));
        section.appendChild(head);
        var container = h('div', { 'is': 'emby-itemscontainer', 'class': 'itemsContainer vertical-wrap padded-right' });
        items.forEach(function (item) { container.appendChild(card(ctx, item, false)); });
        section.appendChild(container);
        return section;
    }

    // A horizontal scroller of cards, the markup jellyfin-web uses for "More Like This" and the home rows.
    function scroller(ctx, name, items, titleClass) {
        var section = h('div', { 'class': 'verticalSection' });
        section.appendChild(h('h2', { 'class': 'sectionTitle sectionTitle-cards ' + (titleClass || '') }, name));
        var scrollerEl = h('div', { 'is': 'emby-scroller', 'class': 'padded-top-focusscale padded-bottom-focusscale', 'data-centerfocus': 'true' });
        var container = h('div', { 'is': 'emby-itemscontainer', 'class': 'itemsContainer scrollSlider focuscontainer-x' });
        items.forEach(function (item) { container.appendChild(card(ctx, item, true)); });
        scrollerEl.appendChild(container);
        section.appendChild(scrollerEl);
        return section;
    }

    // ---- Person page ----

    function renderPerson(page, personId, data) {
        remove(page, PERSON_ID);
        if (!data || (!data.Reason && !data.Movies.length && !data.Series.length)) { return; }
        var ctx = {
            source: 'person',
            sourceId: personId,
            canSend: function (kind) { return kind === 'Movie' ? data.CanSendMovies : data.CanSendSeries; }
        };
        var wrap = h('div', { 'id': PERSON_ID, 'class': 'detailPageSecondaryContainer padded-left padded-bottom-page' });
        var lead = h('div', { 'class': 'verticalSection' });
        lead.appendChild(h('h2', { 'class': 'sectionTitle sectionTitle-cards' }, 'Missing from your library'));
        if (data.Reason) { lead.appendChild(h('p', { 'class': 'mtgNote' }, data.Reason)); }
        wrap.appendChild(lead);
        if (data.Movies.length) { wrap.appendChild(grid(ctx, 'Movies', data.Movies)); }
        if (data.Series.length) { wrap.appendChild(grid(ctx, 'Shows', data.Series)); }

        // Below the person's own items, above "More like this".
        var anchor = page.querySelector('#similarCollapsible');
        var host = anchor ? anchor.parentNode : page.querySelector('.detailPageContent') || page;
        host.insertBefore(wrap, anchor || null);
    }

    // ---- Movie / series page ----

    function renderRelated(page, itemId, data) {
        remove(page, RELATED_ID);
        if (!data || !data.Titles.length) { return; }
        var ctx = {
            source: 'item',
            sourceId: itemId,
            canSend: function () { return data.CanSend; }
        };
        var section = scroller(ctx, "More like this you don't have", data.Titles, 'padded-right');
        section.id = RELATED_ID;
        section.classList.add('detailVerticalSection', 'verticalSection-extrabottompadding');
        // Right after Jellyfin's own "More Like This" (which may be hidden when it has nothing), inside the
        // same container so it takes the same padding.
        var anchor = page.querySelector('#similarCollapsible');
        if (anchor) {
            anchor.parentNode.insertBefore(section, anchor.nextSibling);
        } else {
            (page.querySelector('.detailPageContent') || page).appendChild(section);
        }
    }

    // ---- Home ----

    function renderHome(sectionsEl, data) {
        var old = sectionsEl.querySelector('#' + HOME_ID);
        if (old) { old.parentNode.removeChild(old); }
        if (!data || !data.Titles.length) { return; }
        var ctx = {
            source: 'home',
            sourceId: null,
            wantOnCards: false,
            becauseOnCards: false,
            canSend: function (kind) { return kind === 'Movie' ? data.CanSendMovies : data.CanSendSeries; }
        };
        var section = scroller(ctx, "Discover: not in your library", data.Titles, 'padded-left');
        section.id = HOME_ID;
        sectionsEl.appendChild(section);
    }

    // A card for an owned title on the want-to-watch playlist: a link to its page, in the same markup.
    function ownedCard(item) {
        var tv = isTv();
        var el = h('a', {
            'class': 'card overflowPortraitCard mtgCard mtgOwnedCard' + (tv ? ' show-focus show-animation' : ' card-hoverable'),
            'href': '#/details?id=' + encodeURIComponent(item.Id) + '&serverId=' + encodeURIComponent(ApiClient.serverId()),
            'data-itemid': item.Id,
            'aria-label': item.Name
        });
        var box = h('div', { 'class': 'cardBox cardBox-bottompadded' });
        var scalable = h('div', { 'class': 'cardScalable' });
        scalable.appendChild(h('div', { 'class': 'cardPadder cardPadder-overflowPortrait' }));
        var img = h('div', { 'class': 'cardImageContainer coveredImage cardContent' });
        if (item.ImageTags && item.ImageTags.Primary) {
            img.style.backgroundImage = 'url("' + ApiClient.getImageUrl(item.Id, { type: 'Primary', maxHeight: 450, tag: item.ImageTags.Primary }) + '")';
        } else {
            img.classList.add('defaultCardBackground', 'defaultCardBackground1');
            img.appendChild(h('div', { 'class': 'cardText cardDefaultText' }, item.Name));
        }
        scalable.appendChild(img);
        box.appendChild(scalable);
        var title = h('div', { 'class': 'cardText cardTextCentered cardText-first' });
        title.appendChild(h('bdi', null, item.Name));
        box.appendChild(title);
        var secondary = h('div', { 'class': 'cardText cardTextCentered cardText-secondary' });
        secondary.appendChild(h('bdi', null, (item.ProductionYear ? item.ProductionYear + ' \u00b7 ' : '') + 'In your library'));
        box.appendChild(secondary);
        el.appendChild(box);
        return el;
    }

    // The keys a rendered row's cards carry, in order, so a reload can tell whether anything changed.
    function rowKeys(section) {
        return Array.prototype.map.call(section.querySelectorAll('.mtgCard'), function (c) { return c.getAttribute('data-gapid') || c.getAttribute('data-itemid') || ''; });
    }

    function renderWant(sectionsEl, data, owned) {
        var titles = (data && data.Titles) || [];
        if (data) { searchCanSend = { movies: !!data.CanSendMovies, series: !!data.CanSendSeries }; }
        var old = sectionsEl.querySelector('#' + WANT_ID);
        var newKeys = owned.map(function (i) { return i.Id; }).concat(titles.map(function (t) { return t.GapId; }));
        if (old) {
            // The home view is cached and Jellyfin restores focus into it on return; rebuilding an unchanged
            // row would remove the focused card, and the next remote press would start from nowhere.
            if (rowKeys(old).join('|') === newKeys.join('|')) { return; }
            var focused = old.contains(document.activeElement) ? document.activeElement.closest('.mtgCard') : null;
            var focusKey = focused ? (focused.getAttribute('data-gapid') || focused.getAttribute('data-itemid')) : null;
            var focusIndex = focused ? Array.prototype.indexOf.call(old.querySelectorAll('.mtgCard'), focused) : -1;
            old.parentNode.removeChild(old);
        }
        if (!titles.length && !owned.length && !canEditWant()) { return; }
        var ctx = {
            source: 'todo',
            sourceId: null,
            canSend: function (kind) { return data && (kind === 'Movie' ? data.CanSendMovies : data.CanSendSeries); }
        };
        var section = scroller(ctx, surfaceFlags.WatchlistName || 'Want to watch', titles, 'padded-left');
        if (canEditWant()) {
            var head = section.querySelector('.sectionTitle');
            var wrap = h('div', { 'class': 'sectionTitleContainer sectionTitleContainer-cards padded-left mtgWantHead' });
            head.classList.remove('padded-left');
            head.parentNode.insertBefore(wrap, head);
            wrap.appendChild(head);
            wrap.appendChild(addTitleButton());
        }
        // Owned titles first: they can be watched now.
        var container = section.querySelector('.scrollSlider');
        owned.slice().reverse().forEach(function (item) { container.insertBefore(ownedCard(item), container.firstChild); });
        section.id = WANT_ID;
        // Above Discover when both are present.
        var discover = sectionsEl.querySelector('#' + HOME_ID);
        sectionsEl.insertBefore(section, discover || null);
        if (focusKey) {
            // The same card if it is still there, else its neighbour (the card that took its place, or the last).
            var again = section.querySelector('.mtgCard[data-gapid="' + focusKey + '"], .mtgCard[data-itemid="' + focusKey + '"]');
            if (!again && focusIndex >= 0) {
                var all = section.querySelectorAll('.mtgCard');
                again = all[Math.min(focusIndex, all.length - 1)] || null;
            }
            if (again) { (again.tagName === 'A' || again.tagName === 'BUTTON' ? again : again.querySelector('button')).focus(); }
        }
    }

    function loadWantRow(sectionsEl) {
        if (!sectionsEl || !surfaceFlags.WantToWatch) { return; }
        var unowned = api('GET', 'MindTheGaps/Home/WantToWatch').catch(function () { return null; });
        var owned = findPlaylistId(false).then(function (pid) { return pid ? playlistEntries(pid) : []; }).catch(function () { return []; });
        Promise.all([unowned, owned]).then(function (r) { renderWant(sectionsEl, r[0], r[1]); });
    }

    var homeObserver = null;

    function watchHome(page) {
        var sectionsEl = page.querySelector('#homeTab .sections');
        if (!sectionsEl) { return; }
        if (homeObserver) { homeObserver.disconnect(); }
        var timer = null;
        var load = function () {
            timer = null;
            // Jellyfin has laid out its own sections (or found nothing to show) once the container has any
            // child; until then an appended row would be wiped by its innerHTML assignment.
            if (!sectionsEl.firstChild) { return; }
            // The want-to-watch row reflects a list the viewer edits elsewhere (a title page, a card), so it
            // is reloaded every time the home screen shows; Discover only when it is not there yet.
            if (surfaceFlags.WantToWatch) { loadWantRow(sectionsEl); }
            if (surfaceFlags.HomeRow && !sectionsEl.querySelector('#' + HOME_ID)) {
                api('GET', 'MindTheGaps/Home/Discover').then(function (data) { renderHome(sectionsEl, data); }, function () { /* off, or not signed in */ });
            }
        };
        var schedule = function () {
            if (timer) { clearTimeout(timer); }
            timer = setTimeout(load, 250);
        };
        var ours = function (node) { return node.nodeType === 1 && (node.id === WANT_ID || node.id === HOME_ID); };
        homeObserver = new MutationObserver(function (records) {
            for (var i = 0; i < records.length; i++) {
                if (records[i].target !== sectionsEl) { continue; }
                // Our own rows coming and going must not trigger another load.
                var foreign = Array.prototype.some.call(records[i].addedNodes, function (n) { return !ours(n); })
                    || Array.prototype.some.call(records[i].removedNodes, function (n) { return !ours(n); });
                if (foreign) { schedule(); return; }
            }
        });
        homeObserver.observe(sectionsEl, { childList: true });
        schedule();
    }

    // ---- TV navigation inside a row ----
    //
    // In the TV layout the buttons under a card are taken out of the focus order (tabindex -1), so a
    // controller moves poster to poster along a row, as it does on jellyfin-web's own rows. Down from a
    // poster steps onto its first button; left and right move between that card's buttons and then on to
    // the neighbouring card's poster; up returns to the poster. Handled in the capture phase and marked as
    // handled, which jellyfin-web's own key handler respects.

    function keyName(e) {
        if (e.key === 'ArrowLeft' || e.key === 'ArrowRight' || e.key === 'ArrowUp' || e.key === 'ArrowDown') { return e.key; }
        return { 37: 'ArrowLeft', 38: 'ArrowUp', 39: 'ArrowRight', 40: 'ArrowDown' }[e.keyCode] || null;
    }

    function cardActionButtons(card) {
        return Array.prototype.slice.call(card.querySelectorAll('.mtgCardActions button'));
    }

    function siblingCard(card, direction) {
        var el = direction > 0 ? card.nextElementSibling : card.previousElementSibling;
        while (el && !el.classList.contains('mtgCard')) { el = direction > 0 ? el.nextElementSibling : el.previousElementSibling; }
        return el;
    }

    function onRowKey(e) {
        if (!isTv() || e.defaultPrevented || e.ctrlKey || e.altKey || e.metaKey || e.shiftKey) { return; }
        var key = keyName(e);
        if (!key) { return; }
        var active = document.activeElement;
        if (!active) { return; }

        var target = null;
        if (active.classList.contains('mtgCard')) {
            if (key === 'ArrowDown') { target = cardActionButtons(active)[0] || null; }
        } else if (active.closest && active.closest('.mtgCardActions')) {
            var card = active.closest('.mtgCard');
            var buttons = cardActionButtons(card);
            var index = buttons.indexOf(active);
            if (key === 'ArrowUp') {
                target = card;
            } else if (key === 'ArrowLeft') {
                target = index > 0 ? buttons[index - 1] : siblingCard(card, -1);
            } else if (key === 'ArrowRight') {
                target = index < buttons.length - 1 ? buttons[index + 1] : siblingCard(card, 1);
            }
        }

        if (target) {
            e.preventDefault();
            e.stopPropagation();
            target.focus();
        }
    }

    document.addEventListener('keydown', onRowKey, true);

    // ---- Routing ----

    function onViewShow(e) {
        var page = e.target;
        if (!page || !page.classList || !page.classList.contains('page') || !window.ApiClient) { return; }

        if (page.id === 'indexPage') {
            surfaces().then(function (s) { if (s.HomeRow || s.WantToWatch) { watchHome(page); } });
            return;
        }

        var itemId = itemIdFromLocation();
        if (!itemId) { remove(page, PERSON_ID); remove(page, RELATED_ID); return; }

        var token = ++pending;
        Promise.all([ApiClient.getItem(ApiClient.getCurrentUserId(), itemId), surfaces()]).then(function (results) {
            if (token !== pending) { return; }
            var item = results[0];
            var s = results[1];
            remove(page, PERSON_ID);
            remove(page, RELATED_ID);
            currentPageItem = item || null;
            if (!item) { return; }
            if (item.Type === 'Person' && s.PersonPage) {
                return api('GET', 'MindTheGaps/Person/' + item.Id + '/Missing').then(function (data) {
                    if (token === pending) { renderPerson(page, item.Id, data); }
                });
            }
            if (item.Type === 'Movie' || item.Type === 'Series') {
                if (s.WantToWatch) { renderDetailWant(page, item.Id); }
                if (s.ItemPage) {
                    return api('GET', 'MindTheGaps/Item/' + item.Id + '/Related').then(function (data) {
                        if (token === pending) { renderRelated(page, item.Id, data); }
                    });
                }
            }
        }).catch(function () {
            // A 404 means the surface was switched off or the id is not one we handle; either way show nothing.
            if (token === pending) { remove(page, PERSON_ID); remove(page, RELATED_ID); }
        });
    }

    var style = document.createElement('style');
    style.textContent =
        '.mtgCard{cursor:pointer}' +
        'button.mtgCard{background:none;border:0;padding:0;margin:0;color:inherit;font:inherit;text-align:inherit}' +
        'a.mtgOwnedCard,a.mtgOwnedCard:hover,a.mtgOwnedCard:focus{text-decoration:none;color:inherit}' +
        '.mtgCard .mtgCardImage{border:0;padding:0;cursor:pointer;opacity:.85}' +
        '.mtgCard:hover .mtgCardImage,.mtgCard:focus-within .mtgCardImage{opacity:1}' +
        '.mtgCardActions{display:flex;justify-content:center;align-items:center;gap:.2em;margin-top:.35em}' +
        '.mtgCard .mtgSendButton{font-size:80%}' +
        '.mtgWantButton.mtgWanted{color:#00a4dc}' +
        '.mtgWantButtonText .material-icons{margin-right:.3em}' +
        '.mtgCard .mtgSendButton.mtgSent,.mtgDialogDownload.mtgSent{opacity:.6}' +
        '.mtgUpcomingBadge{position:absolute;top:.5em;left:.5em;z-index:1;padding:.2em .6em;border-radius:.3em;background:rgba(0,0,0,.75);color:#fff;font-size:75%;line-height:1.4}' +
        '.mtgNote{opacity:.8}' +
        '.mtgArrBadge{display:inline-block;padding:.35em .8em;border-radius:.3em;background:rgba(255,255,255,.12);font-size:80%;opacity:.9}' +
        '.mtgArrNote{opacity:.85;align-self:center}' +
        '.mtgWantHead{display:flex;align-items:center;gap:1em}' +
        '.mtgWantHead .sectionTitle{margin:0}' +
        '.mtgAddTitle .material-icons{font-size:1.2em;margin-right:.2em}' +
        '.mtgSearch{width:min(70em,100%)}' +
        '.mtgSearchBody{padding:1.5em}' +
        '.mtgSearchForm{display:flex;gap:1em;align-items:flex-end;margin-bottom:.5em}' +
        '.mtgSearchField{flex:1 1 auto;margin:0}' +
        '.mtgSearchResults{margin-top:1em}' +
        '.mtgSearchResults .card{width:11em!important}' +
        '.mtgOverlay{position:fixed;inset:0;z-index:1100;background:rgba(0,0,0,.6);display:flex;align-items:center;justify-content:center;padding:2em}' +
        '.mtgDialog{position:relative;width:min(56em,100%);max-height:90vh;overflow:auto;border-radius:.4em;background:#181818 center/cover no-repeat;color:#fff;box-shadow:0 .5em 2em rgba(0,0,0,.6)}' +
        '.mtgClose{position:absolute;top:.4em;right:.4em;z-index:1}' +
        '.mtgDialogBody{display:flex;gap:1.5em;padding:1.5em}' +
        '.mtgDialogPoster{flex:0 0 12em;aspect-ratio:2/3;border-radius:.3em;background:#333 center/cover no-repeat}' +
        '.mtgDialogText{flex:1 1 auto;min-width:0}' +
        '.mtgDialogTitle{margin:0 1.5em .3em 0;font-size:1.6em}' +
        '.mtgDialogMeta,.mtgDialogRole{opacity:.8;margin-bottom:.3em}' +
        '.mtgDialogTagline{font-style:italic;opacity:.85;margin:.6em 0 .2em}' +
        '.mtgDialogOverview{line-height:1.5}' +
        '.mtgDialogLinks a{margin-right:1em}' +
        '.mtgDialogActions{display:flex;flex-wrap:wrap;align-items:flex-end;gap:1em;margin-top:1em}' +
        '.mtgDialogActions .mtgProfile{flex:1 1 14em;margin:0}' +
        '@media (max-width:40em){.mtgDialogBody{flex-direction:column}.mtgDialogPoster{flex-basis:auto;width:9em}}';
    document.head.appendChild(style);
    document.addEventListener('viewshow', onViewShow);
})();
