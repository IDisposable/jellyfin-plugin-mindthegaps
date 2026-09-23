// Mind the Gaps: the web UI surfaces.
//
// Loaded by jellyfin-web's index.html (the plugin adds the script tag as the page is served). Renders,
// where the matching surface is switched on in the plugin settings:
//   - on a Person page, a "Missing from your library" section of the person's unowned movies and shows;
//   - on a Movie or Series page, a "More like this you don't have" row of unowned similar titles;
//   - on a Music Artist page, an "Albums you don't have" row, and on a Book page or an author's own page a
//     "More by this author you don't have" row (their cards carry their own links, since these works have no
//     TMDB id to look up);
//   - on the home screen, a "Discover" row of the recommendations the scan has accumulated;
//   - where want to watch is on, a bookmark on every card's image that puts the title on the signed-in user's own list
//     and takes it off again, and a home row of what is still on that list.
// Talks to the server only through the web client's own ApiClient, so it inherits the signed-in user's
// session and base URL. Touches nothing but the elements it owns, and removes them again before rendering
// a different view.
//
// A card's only control of its own is the bookmark: clicking anywhere else on it opens a detail dialog (TMDB's
// own synopsis, genres, rating, a trailer link when TMDB has one) with the want-to-watch button moved into it.
// There is no acquisition handoff here on purpose: an administrator monitors what everyone wants through the
// report's own Maintenance section (the fulfillment queue) instead, so this surface never talks to Radarr or
// Sonarr. The dialog is appended to document.body rather than the page, since jellyfin-web's own page wrapper
// sets CSS containment (see CLAUDE.md's "position: fixed is not safe" note) which would otherwise make it the
// containing block for a fixed-position overlay and misplace it.
(function () {
    'use strict';

    var PERSON_ID = 'mtgPersonMissing';
    var RELATED_ID = 'mtgRelatedMissing';
    var WORKS_ID = 'mtgWorksMissing';
    var HOME_ID = 'mtgHomeDiscover';
    var WANTED_ID = 'mtgHomeWanted';
    var SEARCH_RESULTS_ID = 'mtgSearchResults';

    // The classes every dialog button and link carries, so a link and a button look the same. .emby-button is
    // jellyfin-web's own box model (padding, weight, line height) as plain CSS, which is all that is needed:
    // the custom element only adds that class when it connects, and these are not upgraded.
    var ACTION_BUTTON = 'emby-button raised raised-mini mtgActionButton';
    var pending = 0;
    var homeObserver = null;

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

    function cssUrl(url) {
        return 'url("' + url.replace(/["\\]/g, '') + '")';
    }

    // Puts a provider's image on an element as its background, loaded through the server's image cache. The server
    // redirects a browser to the provider itself whenever it cannot serve an image, so what is left for the page is
    // a host the route does not allow, or a server that cannot be reached. A background cannot report that it
    // failed to load, so a probe of the same address does, and the provider's own address is used instead: the
    // cache can only make an image arrive sooner and never make one go missing.
    // Returns whether there was an image to show.
    function setImage(el, url) {
        if (!url || !/^https:\/\//i.test(url)) { return false; }
        var cached = ApiClient.getUrl('MindTheGaps/Image', { u: url });
        el.style.backgroundImage = cssUrl(cached);
        var probe = new Image();
        probe.onerror = function () { el.style.backgroundImage = cssUrl(url); };
        probe.src = cached;
        return true;
    }

    function alertUser(message) {
        if (window.Dashboard && Dashboard.alert) { Dashboard.alert(message); } else { window.alert(message); }
    }

    function api(type, path, params) {
        return ApiClient.ajax({ type: type, url: ApiClient.getUrl(path, params || {}), dataType: 'json' });
    }

    // The details page is `#/details?id=…` on a hash router; read the id from the hash, not the search.
    function itemIdFromLocation() {
        var hash = window.location.hash || '';
        var q = hash.indexOf('?');
        if (hash.indexOf('/details') < 0 || q < 0) { return null; }
        return new URLSearchParams(hash.slice(q + 1)).get('id');
    }

    // The result of a call that may legitimately answer 404 (a surface that is off, or a page it has nothing for).
    function quietly(promise) {
        return promise.then(function (data) { return data; }, function () { return null; });
    }

    function remove(page, id) {
        var old = page.querySelector('#' + id);
        if (old) { old.parentNode.removeChild(old); }
    }

    // ctx: { kind: 'Person'|'Item'|'Home', id: the owning page's Jellyfin id (empty for Home), canTodo, scope }.
    // scope is an extra path segment before the action, for a page whose actions live under their own route
    // (an artist's or book's works).
    function actionUrl(ctx, action) {
        var path = 'MindTheGaps/' + ctx.kind + '/' + (ctx.id ? ctx.id + '/' : '') + (ctx.scope ? ctx.scope + '/' : '');
        return path + action;
    }

    // A work (an album or a book) carries its own links and has no TMDB id; a title carries a TMDB id.
    function isWork(item) {
        return item.Kind === 'MusicAlbum' || item.Kind === 'Book';
    }

    // The TMDB page for a card: always available, so a card is still actionable with no arr configured
    // and for a viewer who is not an administrator.
    function tmdbUrl(item) {
        return 'https://www.themoviedb.org/' + (item.Kind === 'Series' ? 'tv' : 'movie') + '/' + item.TmdbId;
    }

    // ---- Want to watch ----
    //
    // The bookmark on a card and the button in the dialog are two views of one fact: whether the title is on the
    // signed-in user's own list. Either one changes it and refreshWant shows the result in both. The server
    // finds the gap again from the same lookup the card came from, and takes a title off by what it is, so an
    // entry that reached the list some other way (the report, another page) comes off with it.
    var BOOKMARK_OFF = '<svg viewBox="0 0 24 24" width="1.3em" height="1.3em" aria-hidden="true" focusable="false"><path fill="currentColor" d="M17 3H7c-1.1 0-2 .9-2 2v16l7-3 7 3V5c0-1.1-.9-2-2-2zm0 15-5-2.18L7 18V5h10v13z"/></svg>';
    var BOOKMARK_ON = '<svg viewBox="0 0 24 24" width="1.3em" height="1.3em" aria-hidden="true" focusable="false"><path fill="currentColor" d="M17 3H7c-1.1 0-2 .9-2 2v16l7-3 7 3V5c0-1.1-.9-2-2-2z"/></svg>';

    function wantVerb(item) {
        return item.Kind === 'MusicAlbum' ? 'listen' : (item.Kind === 'Book' ? 'read' : 'watch');
    }

    function removeUrl(ctx) {
        return ctx.removeUrl || actionUrl(ctx, 'Todo/Remove');
    }

    // The title search (ctx.scope === 'Search') has no persisted gap to rehydrate by id: the server looks
    // the title up fresh by kind and TMDB id instead, both of which every card already carries.
    function wantParams(ctx, item) {
        return ctx.scope === 'Search' ? { kind: item.Kind, tmdbId: item.TmdbId } : { gapId: item.GapId };
    }

    // Puts the item on the signed-in user's list, or takes it off, and shows the result wherever it appears.
    function setWanted(ctx, item, on, control) {
        if (control) { control.disabled = true; }
        return api('POST', on ? actionUrl(ctx, 'Todo') : removeUrl(ctx), wantParams(ctx, item)).then(function () {
            item.OnList = on;
            refreshWant(ctx, item);
        }, function () {
            alertUser('Could not update your list.');
        }).then(function () {
            // A title taken off the wanted row is gone; its button stays as it was left.
            if (control && !(ctx.wanted && !on)) { control.disabled = false; }
        });
    }

    function paintBookmark(btn, item) {
        var label = item.OnList ? 'Remove from your list' : 'Want to ' + wantVerb(item);
        btn.innerHTML = item.OnList ? BOOKMARK_ON : BOOKMARK_OFF;
        btn.setAttribute('aria-pressed', item.OnList ? 'true' : 'false');
        btn.setAttribute('title', label);
        btn.setAttribute('aria-label', label);
    }

    function paintWantButton(btn, item) {
        btn.textContent = item.OnList ? 'On your list' : 'Want to ' + wantVerb(item);
        btn.classList.toggle('mtgSent', !!item.OnList);
    }

    // Repaints every bookmark and dialog button for this item. On the home row of what is wanted, a title
    // taken off the list leaves the row (the row itself stays, since its header holds the title search).
    // A card found through the title search instead reloads the wanted row, since the row is the only
    // place a search result's add/remove is otherwise reflected.
    function refreshWant(ctx, item) {
        var id = item.GapId.replace(/["\\]/g, '');
        var card = '.mtgCard[data-gapid="' + id + '"]';
        var dialogButton = '.mtgDialog .mtgWantButton[data-want="' + id + '"]';
        var dialogBookmark = '.mtgDialog .mtgWant[data-want="' + id + '"]';
        Array.prototype.forEach.call(document.querySelectorAll(card + ' .mtgWant, ' + dialogBookmark), function (btn) { paintBookmark(btn, item); });
        Array.prototype.forEach.call(document.querySelectorAll(dialogButton), function (btn) { paintWantButton(btn, item); });

        if (ctx.scope === 'Search') {
            var sectionsEl = document.querySelector('#homeTab .sections');
            if (sectionsEl) { loadWantedRow(sectionsEl); }
            return;
        }

        if (!ctx.wanted || item.OnList) { return; }
        Array.prototype.forEach.call(document.querySelectorAll('#' + WANTED_ID + ' ' + card), function (cardEl) {
            cardEl.parentNode.removeChild(cardEl);
        });
        Array.prototype.forEach.call(document.querySelectorAll(dialogButton), function (btn) {
            btn.textContent = 'Removed from your list';
            btn.disabled = true;
        });
        Array.prototype.forEach.call(document.querySelectorAll(dialogBookmark), function (btn) { btn.disabled = true; });
    }

    function wantBookmark(ctx, item) {
        var btn = h('button', { 'type': 'button', 'class': 'mtgWant' });
        paintBookmark(btn, item);
        btn.addEventListener('click', function (e) {
            e.stopPropagation();
            setWanted(ctx, item, !item.OnList, btn);
        });
        return btn;
    }

    // ---- Detail dialog ----
    //
    // One dialog, built once and reused across opens (a fresh .mtgDialogBody replaces the old one each
    // time). Appended straight to document.body: see the file header for why it cannot live inside the
    // page like the cards do.
    //
    // Remote/keyboard handling is hand-rolled rather than hooked into jellyfin-web's own focusManager/
    // inputManager/dialogHelper: those are plain ES module imports in their bundle, not exposed on
    // window the way ApiClient/Dashboard deliberately are for legacy plugin scripts, so an externally
    // injected script has no way to join their focus-scope stack or their internal history-based dialog
    // list. What is reachable with only standard browser APIs: Tab/Arrow trap focus within the dialog's
    // own controls (mirroring their focusManager's per-scope up/down/left/right), and a real
    // history.pushState/popstate pair so the hardware/software Back button closes the dialog instead of
    // leaving the page, the same technique their own dialogHelper uses against its internal router
    // history. A platform whose native Back button bypasses browser history entirely (for example
    // Tizen's proprietary tizenhwkey event, used by some native TV shells) is not covered by this and
    // would need its own on-device check; Escape and the close button/backdrop click remain regardless.

    var dialogEl = null;
    var dialogInner = null;
    var dialogCloseBtn = null;
    var dialogToken = 0;
    var dialogOpenerEl = null;
    var dialogHistoryPushed = false;

    function dialogFocusable() {
        var all = dialogInner.querySelectorAll('button, a[href], select, [tabindex="0"]');
        return Array.prototype.filter.call(all, function (el) { return el.offsetParent !== null; });
    }

    function moveDialogFocus(delta) {
        var items = dialogFocusable();
        if (!items.length) { return; }
        var at = items.indexOf(document.activeElement);
        var next = at === -1 ? 0 : (at + delta + items.length) % items.length;
        items[next].focus();
    }

    // A Tab/Shift+Tab or Arrow key never leaves the dialog while it is open: jellyfin-web's own focus
    // scope would otherwise let it wander back into the page underneath. Left/Right/Tab always cycle;
    // Up/Down are left to a focused <select> so it keeps its native value-cycling behavior, which a
    // real remote's D-pad already drives correctly on its own.
    function onDialogKeydown(e) {
        if (e.key === 'Tab') {
            e.preventDefault();
            moveDialogFocus(e.shiftKey ? -1 : 1);
        } else if (e.key === 'ArrowRight' || (e.key === 'ArrowDown' && document.activeElement.tagName !== 'SELECT')) {
            e.preventDefault();
            moveDialogFocus(1);
        } else if (e.key === 'ArrowLeft' || (e.key === 'ArrowUp' && document.activeElement.tagName !== 'SELECT')) {
            e.preventDefault();
            moveDialogFocus(-1);
        }
    }

    function closeDialog() {
        if (!dialogEl || !dialogEl.classList.contains('mtgDialogOpen')) { return; }
        dialogEl.classList.remove('mtgDialogOpen');
        if (dialogHistoryPushed) {
            dialogHistoryPushed = false;
            window.history.back();
        }

        if (dialogOpenerEl && typeof dialogOpenerEl.focus === 'function') { dialogOpenerEl.focus(); }
        dialogOpenerEl = null;
    }

    function ensureDialog() {
        if (dialogEl) { return dialogEl; }
        var backdrop = h('div', { 'class': 'mtgDialogBackdrop' });
        backdrop.addEventListener('click', function (e) { if (e.target === backdrop) { closeDialog(); } });
        var dialog = h('div', { 'class': 'mtgDialog', 'role': 'dialog', 'aria-modal': 'true' });
        dialog.addEventListener('keydown', onDialogKeydown);
        // An inline SVG rather than a text glyph, so the cross is centered by geometry and not by a font's
        // metrics. The class is jellyfin-web's own for a round icon button; the elements this script builds are
        // never upgraded to emby-button (they are created with createElement, then given an is attribute), so
        // the styling has to come from the classes.
        var closeBtn = h('button', { 'type': 'button', 'class': 'paper-icon-button-light mtgDialogClose', 'aria-label': 'Close' });
        closeBtn.innerHTML = '<svg viewBox="0 0 24 24" width="1.3em" height="1.3em" aria-hidden="true" focusable="false"><path fill="currentColor" d="M19 6.41 17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z"/></svg>';
        closeBtn.addEventListener('click', closeDialog);
        dialog.appendChild(closeBtn);
        backdrop.appendChild(dialog);
        document.body.appendChild(backdrop);
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && backdrop.classList.contains('mtgDialogOpen')) { closeDialog(); }
        });
        // The Back button on a platform that surfaces it as real browser history navigation (rather than
        // a proprietary key event some native TV shells use instead): openDialog pushes one history entry,
        // so a Back closes the dialog here without ever reaching a real page navigation.
        window.addEventListener('popstate', function () {
            if (backdrop.classList.contains('mtgDialogOpen')) {
                dialogHistoryPushed = false;
                backdrop.classList.remove('mtgDialogOpen');
                if (dialogOpenerEl && typeof dialogOpenerEl.focus === 'function') { dialogOpenerEl.focus(); }
                dialogOpenerEl = null;
            }
        });
        dialogEl = backdrop;
        dialogInner = dialog;
        dialogCloseBtn = closeBtn;
        return backdrop;
    }

    // The want-to-watch control: independent of the TMDB detail lookup below, since a gap already carries
    // everything it needs, so it must not wait on (or fail because of) a slow or failing TMDB call.
    function renderActions(actionsEl, ctx, item) {
        if (ctx.canTodo) {
            var wantBtn = h('button', { 'type': 'button', 'class': ACTION_BUTTON + ' mtgWantButton', 'data-want': item.GapId });
            paintWantButton(wantBtn, item);
            wantBtn.addEventListener('click', function () { setWanted(ctx, item, !item.OnList, wantBtn); });
            actionsEl.appendChild(wantBtn);
        }
    }

    function metaLine(detail) {
        var parts = [];
        if (detail.RuntimeMinutes) { parts.push(detail.RuntimeMinutes + ' min'); }
        if (detail.Status) { parts.push(detail.Status); }
        if (detail.Kind === 'Series' && detail.NumberOfSeasons) {
            parts.push(detail.NumberOfSeasons + (detail.NumberOfSeasons === 1 ? ' season' : ' seasons'));
        }
        if (detail.Networks && detail.Networks.length) { parts.push(detail.Networks.join(', ')); }
        if (detail.VoteAverage) { parts.push('TMDB ' + detail.VoteAverage.toFixed(1) + '/10'); }
        return parts.join(' \u00b7 ');
    }

    // Everything that needs the TMDB lookup: tagline, meta line, genres, overview, an IMDb link, a
    // trailer link. Inserted once the lookup resolves, ahead of the links row so the layout reads
    // top-to-bottom: title, synopsis, then the row of links/actions.
    function fillDialogDetail(refs, detail) {
        refs.loading.remove();
        setImage(refs.backdropImg, detail.BackdropUrl);
        setImage(refs.poster, detail.PosterUrl);

        var extra = document.createDocumentFragment();
        if (detail.Tagline) { extra.appendChild(h('p', { 'class': 'mtgDialogTagline' }, detail.Tagline)); }
        var meta = metaLine(detail);
        if (meta) { extra.appendChild(h('p', { 'class': 'mtgDialogMeta' }, meta)); }
        if (detail.Genres && detail.Genres.length) { extra.appendChild(h('p', { 'class': 'mtgDialogGenres' }, detail.Genres.join(', '))); }
        if (detail.Overview) { extra.appendChild(h('p', { 'class': 'mtgDialogOverview' }, detail.Overview)); }
        refs.info.insertBefore(extra, refs.links);

        if (detail.ImdbUrl) {
            refs.links.appendChild(h('a', { 'href': detail.ImdbUrl, 'target': '_blank', 'rel': 'noopener noreferrer', 'class': ACTION_BUTTON }, 'View on IMDb'));
        }
        if (detail.JustWatchUrl) {
            refs.links.appendChild(h('a', { 'href': detail.JustWatchUrl, 'target': '_blank', 'rel': 'noopener noreferrer', 'class': ACTION_BUTTON, 'title': 'See which services stream it' }, 'Search JustWatch'));
        }
        if (detail.YoutubeTrailerKey) {
            refs.links.appendChild(h('a', {
                'href': 'https://www.youtube.com/watch?v=' + encodeURIComponent(detail.YoutubeTrailerKey),
                'target': '_blank',
                'rel': 'noopener noreferrer',
                'class': ACTION_BUTTON
            }, 'Watch trailer'));
        }
    }

    // Everything that does not need to wait on TMDB: the shell, the poster (already have its URL from
    // the card), the TMDB link, and the Add-to-TODO action.
    function dialogBody(ctx, item) {
        var body = h('div', { 'class': 'mtgDialogBody' });
        var backdropImg = h('div', { 'class': 'mtgDialogBackdropImage' });
        body.appendChild(backdropImg);

        var content = h('div', { 'class': 'mtgDialogContent' });
        var poster = h('div', { 'class': 'mtgDialogPoster' + (item.Kind === 'MusicAlbum' ? ' mtgDialogPosterSquare' : '') });
        setImage(poster, item.ImageUrl);
        // The same bookmark, in the same corner, as on the card the dialog was opened from.
        if (ctx.canTodo) {
            var mark = wantBookmark(ctx, item);
            mark.setAttribute('data-want', item.GapId);
            poster.appendChild(mark);
        }

        content.appendChild(poster);

        var info = h('div', { 'class': 'mtgDialogInfo' });
        info.appendChild(h('h2', { 'class': 'mtgDialogTitle' }, item.Title + (item.Year ? ' (' + item.Year + ')' : '')));
        var loading = null;
        var links = h('div', { 'class': 'mtgDialogLinks' });
        if (isWork(item)) {
            // Everything a work has is on the card already: who it is by, and where to read about it.
            if (item.Creator) { info.appendChild(h('p', { 'class': 'mtgDialogMeta' }, (item.Kind === 'Book' ? 'By ' : 'Album by ') + item.Creator)); }
            (item.Links || []).forEach(function (link) {
                if (!link || !link.Url || !/^https:\/\//i.test(link.Url)) { return; }
                // Amazon and the configured web search are query links, not a page about the work, so they
                // read as "Search Amazon"/"Web search" rather than "View on ...".
                var label = link.Name === 'Amazon' ? 'Search Amazon' : (link.Name === 'Web search' ? 'Web search' : 'View on ' + link.Name);
                links.appendChild(h('a', { 'href': link.Url, 'target': '_blank', 'rel': 'noopener noreferrer', 'class': ACTION_BUTTON }, label));
            });
        } else {
            loading = h('p', { 'class': 'mtgNote' }, 'Loading details\u2026');
            info.appendChild(loading);
            links.appendChild(h('a', { 'href': tmdbUrl(item), 'target': '_blank', 'rel': 'noopener noreferrer', 'class': ACTION_BUTTON }, 'View on TMDB'));
        }
        info.appendChild(links);

        var actions = h('div', { 'class': 'mtgDialogActions' });
        info.appendChild(actions);
        renderActions(actions, ctx, item);

        content.appendChild(info);
        body.appendChild(content);
        return { body: body, info: info, loading: loading, links: links, poster: poster, backdropImg: backdropImg };
    }

    function openDialog(ctx, item) {
        ensureDialog();
        var wasOpen = dialogEl.classList.contains('mtgDialogOpen');
        var token = ++dialogToken;
        var old = dialogInner.querySelector('.mtgDialogBody');
        if (old) { old.remove(); }
        var refs = dialogBody(ctx, item);
        dialogInner.appendChild(refs.body);
        dialogEl.classList.add('mtgDialogOpen');

        if (!wasOpen) {
            dialogOpenerEl = document.activeElement;
            window.history.pushState({ mtgDialog: true }, '');
            dialogHistoryPushed = true;
        }

        // Autofocus the close button, not the want-to-watch button: a remote's Select right after opening
        // must not risk triggering an action before the title has even loaded.
        dialogCloseBtn.focus();

        if (isWork(item)) { return; }

        api('GET', 'MindTheGaps/WebUi/Detail', { tmdbId: item.TmdbId, kind: item.Kind }).then(function (detail) {
            if (token !== dialogToken) { return; }
            if (detail) { fillDialogDetail(refs, detail); } else { refs.loading.textContent = 'No further details available.'; }
        }, function () {
            if (token !== dialogToken) { return; }
            refs.loading.textContent = 'Could not load details from TMDB.';
        });
    }

    // A plain card: image, title, year/role, and the want-to-watch bookmark in the image's upper right corner
    // (absolutely placed inside the card's own image box, which is safe under the page's CSS containment
    // where a fixed overlay is not; that containment is also why the dialog itself is appended to
    // document.body rather than here). Clicking anywhere else on it, or pressing Enter/Space while it has
    // focus, opens the detail dialog. tabindex/role make it reachable at all from a keyboard or a
    // remote's D-pad: without them a plain div is invisible to Tab order and jellyfin-web's own focus
    // conventions do not apply to it (see the detail dialog's own header comment for why not).
    function card(ctx, item) {
        // An album cover is square; a poster or a book cover is portrait.
        var shape = item.Kind === 'MusicAlbum' ? 'square' : 'portrait';
        var el = h('div', { 'class': 'card ' + shape + 'Card mtgCard card-hoverable', 'data-gapid': item.GapId, 'tabindex': '0', 'role': 'button' });
        var box = h('div', { 'class': 'cardBox cardBox-bottompadded' });
        var scalable = h('div', { 'class': 'cardScalable' });
        scalable.appendChild(h('div', { 'class': 'cardPadder cardPadder-' + shape }));
        var img = h('div', { 'class': 'cardImageContainer coveredImage cardContent' });
        if (!setImage(img, item.ImageUrl)) {
            img.classList.add('defaultCardBackground', 'defaultCardBackground1');
            img.appendChild(h('div', { 'class': 'cardText cardDefaultText' }, item.Title));
        }

        if (item.Upcoming) { img.appendChild(h('div', { 'class': 'mtgUpcomingBadge' }, 'Upcoming')); }
        scalable.appendChild(img);
        if (ctx.canTodo) { scalable.appendChild(wantBookmark(ctx, item)); }
        box.appendChild(scalable);

        // The full title on hover, since a long one is cut to the card's width.
        var title = h('div', { 'class': 'cardText cardTextCentered cardText-first', 'title': item.Title });
        title.appendChild(h('bdi', null, item.Title));
        box.appendChild(title);

        // The year is never cut; a long role gives way to an ellipsis, with the whole line on hover.
        var sub = item.Year ? String(item.Year) : '';
        if (item.Role) { sub = sub ? sub + ' \u00b7 ' + item.Role : item.Role; }
        var secondary = h('div', { 'class': 'cardText cardTextCentered cardText-secondary mtgCardMeta', 'title': sub });
        if (item.Year) { secondary.appendChild(h('span', { 'class': 'mtgCardYear' }, String(item.Year) + (item.Role ? ' \u00b7' : ''))); }
        if (item.Role) { secondary.appendChild(h('span', { 'class': 'mtgCardRole' }, item.Role)); }
        box.appendChild(secondary);

        el.appendChild(box);
        el.addEventListener('click', function () { openDialog(ctx, item); });
        el.addEventListener('keydown', function (e) {
            // The bookmark is a button of its own: Enter and Space on it act on it, not on the card.
            if (e.target !== el) { return; }
            if (e.key === 'Enter' || e.key === ' ' || e.key === 'Spacebar') {
                e.preventDefault();
                openDialog(ctx, item);
            }
        });
        return el;
    }

    // Arrow-key navigation between cards in the same container, delegated onto the container rather than
    // per-card. Left/Right moves to the adjacent card in DOM order, which for a wrapping grid also reads
    // as "next/previous in reading order" (rolling from the end of one row to the start of the next) with
    // no extra geometry needed; for a single-row scroller it is simply the next/previous card. Up/Down
    // need actual geometry, since the next card in DOM order is not the one below it once a grid wraps:
    // the nearest card whose center lies in that direction, weighted to prefer the closest row over exact
    // horizontal alignment. In a single-row scroller nothing ever qualifies as "above" or "below", so
    // Up/Down are a no-op there (left to the browser, e.g. page scroll) rather than std::abs geometry.
    // This is the hand-rolled substitute for jellyfin-web's own focusManager.moveUp/Down/Left/Right,
    // which is not reachable from an externally injected script (see the detail dialog's own comment for
    // why not); a real remote's D-pad sends the same arrow keys either way.
    function focusAdjacentCard(current, delta) {
        var sib = delta > 0 ? current.nextElementSibling : current.previousElementSibling;
        if (sib && sib.classList.contains('mtgCard')) { sib.focus(); return true; }
        return false;
    }

    function focusCardInRow(container, current, verticalDelta) {
        var curRect = current.getBoundingClientRect();
        var curX = curRect.left + (curRect.width / 2);
        var curY = curRect.top + (curRect.height / 2);
        var best = null;
        var bestScore = Infinity;
        Array.prototype.forEach.call(container.children, function (candidate) {
            if (candidate === current || !candidate.classList.contains('mtgCard')) { return; }
            var rect = candidate.getBoundingClientRect();
            var cy = rect.top + (rect.height / 2);
            var inDirection = verticalDelta > 0 ? cy > curY + 1 : cy < curY - 1;
            if (!inDirection) { return; }
            var cx = rect.left + (rect.width / 2);
            // Rows differ by far more than a card's width, so weighting the vertical gap heavily groups
            // candidates by "closest row" first and only then breaks ties by horizontal position.
            var score = (Math.abs(cy - curY) * 1000) + Math.abs(cx - curX);
            if (score < bestScore) { bestScore = score; best = candidate; }
        });
        if (best) { best.focus(); return true; }
        return false;
    }

    function wireCardNavigation(container) {
        container.addEventListener('keydown', function (e) {
            var current = e.target;
            if (!current || !current.classList || !current.classList.contains('mtgCard')) { return; }
            var moved = false;
            if (e.key === 'ArrowRight') { moved = focusAdjacentCard(current, 1); }
            else if (e.key === 'ArrowLeft') { moved = focusAdjacentCard(current, -1); }
            else if (e.key === 'ArrowDown') { moved = focusCardInRow(container, current, 1); }
            else if (e.key === 'ArrowUp') { moved = focusCardInRow(container, current, -1); }
            if (moved) { e.preventDefault(); }
        });
    }

    // A wrapping grid of cards (the person page).
    function grid(ctx, name, items) {
        var section = h('div', { 'class': 'verticalSection' });
        var head = h('div', { 'class': 'sectionTitleContainer sectionTitleContainer-cards' });
        head.appendChild(h('h2', { 'class': 'sectionTitle sectionTitle-cards' }, name + ' (' + items.length + ')'));
        section.appendChild(head);
        var container = h('div', { 'is': 'emby-itemscontainer', 'class': 'itemsContainer vertical-wrap padded-right' });
        items.forEach(function (item) { container.appendChild(card(ctx, item)); });
        wireCardNavigation(container);
        section.appendChild(container);
        return section;
    }

    // A horizontal scroller of cards, in the markup jellyfin-web uses for its own rows, which differs by page.
    // On the item page the row sits in .detailVerticalSection, which already pads the left edge, so the
    // scroller takes no-padding of its own and the title goes straight in. A home section is not padded by its
    // parent: its title sits in a padded-left container and the scroller supplies the cards' own offset.
    function scroller(ctx, name, items, onHome, extraHeader) {
        var section = h('div', { 'class': 'verticalSection' });
        if (onHome) {
            var head = h('div', { 'class': 'sectionTitleContainer sectionTitleContainer-cards padded-left' });
            head.appendChild(h('h2', { 'class': 'sectionTitle sectionTitle-cards' }, name));
            if (extraHeader) { head.appendChild(extraHeader); }
            section.appendChild(head);
        } else {
            section.appendChild(h('h2', { 'class': 'sectionTitle sectionTitle-cards padded-right' }, name));
        }
        var scrollerEl = h('div', { 'is': 'emby-scroller', 'class': 'padded-top-focusscale padded-bottom-focusscale' + (onHome ? '' : ' no-padding'), 'data-centerfocus': 'true' });
        var container = h('div', { 'is': 'emby-itemscontainer', 'class': 'itemsContainer scrollSlider focuscontainer-x' });
        items.forEach(function (item) { container.appendChild(card(ctx, item)); });
        wireCardNavigation(container);
        scrollerEl.appendChild(container);
        section.appendChild(scrollerEl);
        return section;
    }

    // ---- Person page ----

    function renderPerson(page, personId, data) {
        remove(page, PERSON_ID);
        if (!data || (!data.Reason && !data.Movies.length && !data.Series.length)) { return; }

        var ctx = { kind: 'Person', id: personId, canTodo: !!data.CanTodo };
        var wrap = h('div', { 'id': PERSON_ID, 'class': 'detailPageSecondaryContainer padded-left padded-bottom-page' });
        var lead = h('div', { 'class': 'verticalSection' });
        lead.appendChild(h('h2', { 'class': 'sectionTitle sectionTitle-cards' }, 'Missing from your library'));
        if (data.Reason) { lead.appendChild(h('p', { 'class': 'mtgNote' }, data.Reason)); }
        wrap.appendChild(lead);
        if (data.Movies.length) { wrap.appendChild(grid(ctx, 'Movies', data.Movies)); }
        if (data.Series.length) { wrap.appendChild(grid(ctx, 'Shows', data.Series)); }

        // Below the person's own items, above jellyfin-web's own "More Like This".
        var anchor = page.querySelector('#similarCollapsible');
        var host = anchor ? anchor.parentNode : page.querySelector('.detailPageContent') || page;
        host.insertBefore(wrap, anchor || null);
    }

    // ---- Movie / series page ----

    function renderRelated(page, itemId, data) {
        remove(page, RELATED_ID);
        if (!data || (!data.Reason && !data.Titles.length)) { return; }

        var ctx = { kind: 'Item', id: itemId, canTodo: !!data.CanTodo };
        var section;
        if (data.Titles.length) {
            section = scroller(ctx, "More like this you don't have", data.Titles);
        } else {
            section = h('div', { 'class': 'verticalSection' });
            section.appendChild(h('h2', { 'class': 'sectionTitle sectionTitle-cards' }, "More like this you don't have"));
            section.appendChild(h('p', { 'class': 'mtgNote' }, data.Reason));
        }

        section.id = RELATED_ID;
        section.classList.add('detailVerticalSection', 'verticalSection-extrabottompadding');

        // Right after jellyfin-web's own "More Like This" (which may be hidden when it has nothing), inside
        // the same container so it takes the same padding.
        var anchor = page.querySelector('#similarCollapsible');
        if (anchor) {
            anchor.parentNode.insertBefore(section, anchor.nextSibling);
        } else {
            (page.querySelector('.detailPageContent') || page).appendChild(section);
        }
    }

    // ---- Artist / book page ----

    // onPerson: an author's own page, where the row sits under the filmography section and takes that section's
    // markup (a padded secondary container) so the two line up; on an artist or book page it is a
    // .detailVerticalSection, like the movie and series row.
    function renderWorks(page, itemId, data, onPerson) {
        remove(page, WORKS_ID);
        if (!data || (!data.Reason && !data.Works.length)) { return; }

        var heading = data.Kind === 'Book' ? "More by this author you don't have" : "Albums you don't have";
        var ctx = { kind: 'Item', id: itemId, canTodo: !!data.CanTodo, scope: 'Works' };
        var section;
        if (data.Works.length) {
            section = scroller(ctx, heading, data.Works);
        } else {
            section = h('div', { 'class': 'verticalSection' });
            section.appendChild(h('h2', { 'class': 'sectionTitle sectionTitle-cards' }, heading));
            section.appendChild(h('p', { 'class': 'mtgNote' }, data.Reason));
        }

        var anchor = page.querySelector('#similarCollapsible');
        if (onPerson) {
            var wrap = h('div', { 'id': WORKS_ID, 'class': 'detailPageSecondaryContainer padded-left padded-bottom-page' });
            wrap.appendChild(section);
            // Right after the filmography section, which went in before the same anchor, else at the end.
            var host = anchor ? anchor.parentNode : page.querySelector('.detailPageContent') || page;
            host.insertBefore(wrap, anchor || null);
            return;
        }

        section.id = WORKS_ID;
        section.classList.add('detailVerticalSection', 'verticalSection-extrabottompadding');

        // Where the movie and series row goes: after jellyfin-web's own "More Like This" when the page has
        // one, else at the end of the page's content.
        if (anchor) {
            anchor.parentNode.insertBefore(section, anchor.nextSibling);
        } else {
            (page.querySelector('.detailPageContent') || page).appendChild(section);
        }
    }

    // ---- Home ----

    function renderHome(sectionsEl, data) {
        remove(sectionsEl, HOME_ID);
        if (!data || !data.Titles.length) { return; }

        var ctx = { kind: 'Home', id: '', canTodo: !!data.CanTodo };
        var section = scroller(ctx, 'Discover: not in your library', data.Titles, true);
        section.id = HOME_ID;
        sectionsEl.appendChild(section);
    }

    // The search box in the wanted row's own header: a movie or series no page already lists, found on
    // TMDB and added straight to the list. restoreState (kind/query) survives a reload the row's own add
    // or remove triggers (loadWantedRow), so clicking a result's bookmark does not wipe what was typed.
    function buildSearchBox(sectionsEl, restoreState) {
        var wrap = h('div', { 'class': 'mtgSearchBox' });
        var kindSel = h('select', { 'class': 'mtgSearchKind', 'aria-label': 'Kind to search for' });
        kindSel.appendChild(h('option', { 'value': 'Movie' }, 'Movie'));
        kindSel.appendChild(h('option', { 'value': 'Series' }, 'Series'));
        if (restoreState && restoreState.kind) { kindSel.value = restoreState.kind; }
        var input = h('input', {
            'type': 'search',
            'class': 'mtgSearchInput',
            'placeholder': 'Add a title…',
            'aria-label': 'Search for a movie or series to add to your list'
        });
        if (restoreState && restoreState.query) { input.value = restoreState.query; }
        wrap.appendChild(kindSel);
        wrap.appendChild(input);

        var timer = null;
        var trigger = function (immediate) {
            if (timer) { clearTimeout(timer); }
            var kind = kindSel.value;
            var query = input.value;
            if (immediate) { runSearch(sectionsEl, kind, query); } else { timer = setTimeout(function () { runSearch(sectionsEl, kind, query); }, 300); }
        };
        input.addEventListener('input', function () { trigger(false); });
        input.addEventListener('keydown', function (e) { if (e.key === 'Enter') { trigger(true); } });
        kindSel.addEventListener('change', function () { if (input.value) { trigger(true); } });
        // The header sits inside a card the click-to-open-dialog wiring is not on, but stopPropagation
        // keeps a click here from ever being read as a card interaction if that ever changes.
        wrap.addEventListener('click', function (e) { e.stopPropagation(); });
        return wrap;
    }

    // The search results under the wanted row's header: replaced on every keystroke (debounced) or kind
    // change, and hidden entirely once the query is cleared. A result's card is the same one every other
    // surface uses; its bookmark goes through wantParams' Search branch, since a search result has no
    // persisted gap of its own to rehydrate by id.
    //
    // query is what the signed-in user just typed, so it is treated as hostile when it appears in the "no
    // matches" message: h()'s text parameter assigns it via textContent (never innerHTML or string-built
    // markup), so it can only ever render as literal text, whatever characters it contains.
    function renderSearchResults(section, titles, query) {
        remove(section, SEARCH_RESULTS_ID);
        if (!query) { return; }

        var box = h('div', { 'id': SEARCH_RESULTS_ID, 'class': 'mtgSearchResultsBox' });
        if (!titles.length) {
            box.appendChild(h('p', { 'class': 'mtgNote mtgSearchNote' }, 'No matches for “' + query + '”.'));
        } else {
            var searchCtx = { kind: 'Home', id: '', scope: 'Search', canTodo: true };
            var container = h('div', { 'is': 'emby-itemscontainer', 'class': 'itemsContainer vertical-wrap padded-left' });
            titles.forEach(function (item) { container.appendChild(card(searchCtx, item)); });
            wireCardNavigation(container);
            box.appendChild(container);
        }

        var head = section.querySelector('.sectionTitleContainer');
        head.parentNode.insertBefore(box, head.nextSibling);
    }

    function runSearch(sectionsEl, kind, query) {
        var section = sectionsEl.querySelector('#' + WANTED_ID);
        if (!section) { return; }
        var trimmed = (query || '').trim();
        if (!trimmed) { renderSearchResults(section, [], ''); return; }
        api('GET', 'MindTheGaps/Home/Search', { kind: kind, q: trimmed }).then(function (titles) {
            renderSearchResults(section, titles || [], trimmed);
        }, function () { renderSearchResults(section, [], ''); });
    }

    // The current search box's kind/query, read before a reload replaces the row, so loadWantedRow can put
    // them back (and re-run the search) once the fresh row is in place.
    function currentSearchState(sectionsEl) {
        var box = sectionsEl.querySelector('#' + WANTED_ID + ' .mtgSearchBox');
        if (!box) { return null; }
        return { kind: box.querySelector('.mtgSearchKind').value, query: box.querySelector('.mtgSearchInput').value };
    }

    // The home row of what the signed-in user still wants: the movies and series on their own list that the
    // library does not hold, plus the title search. It goes ahead of the Discover row when both are there,
    // and a title taken off the list leaves it (refreshWant) without removing the row itself, since the row's
    // header is also where the search lives. Renders even with an empty list, as long as want to watch is on
    // for this user (an empty result would otherwise leave no way to add a first title).
    function renderWanted(sectionsEl, data, restoreState) {
        remove(sectionsEl, WANTED_ID);
        if (!data) { return; }

        var ctx = { kind: 'Home', id: '', canTodo: true, wanted: true, removeUrl: 'MindTheGaps/Home/Wanted/Remove' };
        var searchBox = buildSearchBox(sectionsEl, restoreState);
        var section = scroller(ctx, 'Want to watch', data.Titles || [], true, searchBox);
        section.id = WANTED_ID;
        sectionsEl.insertBefore(section, sectionsEl.querySelector('#' + HOME_ID));
        if (restoreState && restoreState.query) { runSearch(sectionsEl, restoreState.kind, restoreState.query); }
    }

    // Reloads the wanted row (an add or remove through the row's own search, or the card the row already
    // showed), preserving whatever the search box currently holds across the rebuild.
    function loadWantedRow(sectionsEl) {
        var state = currentSearchState(sectionsEl);
        api('GET', 'MindTheGaps/Home/Wanted').then(function (data) { renderWanted(sectionsEl, data, state); }, function () { /* off, or not signed in */ });
    }

    // The home view is cached by jellyfin-web: returning to it fires viewshow without a reload, and its
    // own sections are laid out asynchronously after that (appending to this container, wiping anything
    // already there via innerHTML). A fixed delay from viewshow would be guessing whether that finished;
    // a MutationObserver knows it is actually happening, but still fires per mutation record, so a short
    // debounce timer (schedule/load) waits for a burst of them to settle before checking, rather than
    // reacting mid-layout. Our own row's insertion is excluded from what counts as "still changing", so
    // it cannot trigger its own reload loop.
    function watchHome(page) {
        var sectionsEl = page.querySelector('#homeTab .sections');
        if (!sectionsEl) { return; }
        if (homeObserver) { homeObserver.disconnect(); }

        var timer = null;
        var load = function () {
            timer = null;
            if (!sectionsEl.firstChild) { return; }
            if (!sectionsEl.querySelector('#' + HOME_ID)) {
                api('GET', 'MindTheGaps/Home/Discover').then(function (data) { renderHome(sectionsEl, data); }, function () { /* off, or not signed in */ });
            }

            if (!sectionsEl.querySelector('#' + WANTED_ID)) {
                loadWantedRow(sectionsEl);
            }
        };
        var schedule = function () {
            if (timer) { clearTimeout(timer); }
            timer = setTimeout(load, 250);
        };
        var ours = function (node) { return node.nodeType === 1 && (node.id === HOME_ID || node.id === WANTED_ID); };
        homeObserver = new MutationObserver(function (records) {
            for (var i = 0; i < records.length; i++) {
                if (records[i].target !== sectionsEl) { continue; }
                var foreign = Array.prototype.some.call(records[i].addedNodes, function (n) { return !ours(n); })
                    || Array.prototype.some.call(records[i].removedNodes, function (n) { return !ours(n); });
                if (foreign) { schedule(); return; }
            }
        });
        homeObserver.observe(sectionsEl, { childList: true });
        schedule();
    }

    function onViewShow(e) {
        var page = e.target;
        if (!page || !page.classList || !page.classList.contains('page') || !window.ApiClient) { return; }

        if (page.id === 'indexPage') {
            watchHome(page);
            return;
        }

        var itemId = itemIdFromLocation();
        if (!itemId) { remove(page, PERSON_ID); remove(page, RELATED_ID); remove(page, WORKS_ID); return; }

        var token = ++pending;
        Promise.resolve(ApiClient.getItem(ApiClient.getCurrentUserId(), itemId)).then(function (item) {
            if (token !== pending) { return; }
            remove(page, PERSON_ID);
            remove(page, RELATED_ID);
            remove(page, WORKS_ID);
            if (!item) { return; }
            if (item.Type === 'Person') {
                // A person may be an actor, an author, or both: ask for the filmography and for the books, each
                // answering 404 when it has nothing to say about them.
                return Promise.all([
                    quietly(api('GET', 'MindTheGaps/Person/' + item.Id + '/Missing')),
                    quietly(api('GET', 'MindTheGaps/Item/' + item.Id + '/Works'))
                ]).then(function (results) {
                    if (token !== pending) { return; }
                    var missing = results[0];
                    var works = results[1];
                    // An author has no filmography to look up, and the note that says so is noise on their page.
                    if (works && missing && missing.Reason && !missing.Movies.length && !missing.Series.length) { missing = null; }
                    renderPerson(page, item.Id, missing);
                    renderWorks(page, item.Id, works, true);
                });
            }

            if (item.Type === 'Movie' || item.Type === 'Series') {
                return api('GET', 'MindTheGaps/Item/' + item.Id + '/Related').then(function (data) {
                    if (token === pending) { renderRelated(page, item.Id, data); }
                });
            }

            if (item.Type === 'MusicArtist' || item.Type === 'Book') {
                return api('GET', 'MindTheGaps/Item/' + item.Id + '/Works').then(function (data) {
                    if (token === pending) { renderWorks(page, item.Id, data); }
                });
            }
        }).catch(function () {
            // A 404 means the surface was switched off or the id is not one we handle; either way show nothing.
            if (token === pending) { remove(page, PERSON_ID); remove(page, RELATED_ID); remove(page, WORKS_ID); }
        });
    }

    var style = document.createElement('style');
    style.textContent =
        '.mtgUpcomingBadge{position:absolute;top:.5em;left:.5em;z-index:1;padding:.2em .6em;border-radius:.3em;background:rgba(0,0,0,.75);color:#fff;font-size:75%;line-height:1.4}' +
        '.mtgNote{opacity:.8}' +
        '.mtgCardMeta{display:flex;align-items:baseline;justify-content:center;gap:.3em}' +
        '.mtgCardYear{flex:none}' +
        '.mtgCardRole{flex:0 1 auto;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}' +
        // The bookmark sits in the upper right corner of an image (a card's, or the dialog's poster), over a dark
        // disc so it reads on any artwork. Absolute inside the image's own box, never fixed.
        '.mtgCard .cardScalable,.mtgDialogPoster{position:relative}' +
        '.mtgWant{position:absolute;top:.4em;right:.4em;z-index:1;display:inline-flex;align-items:center;justify-content:center;box-sizing:border-box;width:2.4em;height:2.4em;margin:0;padding:0;border:0;border-radius:50%;background:rgba(0,0,0,.6);color:#fff;cursor:pointer;opacity:.85}' +
        '.mtgWant svg{width:1.5em;height:1.5em}' +
        '.mtgWant:hover,.mtgWant:focus,.mtgWant[aria-pressed="true"]{opacity:1}' +
        '.mtgWant:focus{outline:3px solid #00a4dc;outline-offset:2px}' +
        '.mtgCard{cursor:pointer}' +
        '.mtgDialogBackdrop{display:none;position:fixed;top:0;left:0;right:0;bottom:0;z-index:9999;background:rgba(0,0,0,.7);align-items:center;justify-content:center;padding:2em;overflow-y:auto}' +
        '.mtgDialogBackdrop.mtgDialogOpen{display:flex}' +
        '.mtgDialog{position:relative;max-width:56em;width:100%;max-height:90vh;overflow-y:auto;background:#101010;border-radius:.5em;box-shadow:0 1em 3em rgba(0,0,0,.6)}' +
        // Two classes, so these win over jellyfin-web's own padding and margin on .paper-icon-button-light and
        // .emby-button whatever order the stylesheets load in.
        '.mtgDialog .mtgDialogClose{position:absolute;top:.5em;right:.5em;z-index:2;display:flex;align-items:center;justify-content:center;box-sizing:border-box;width:2.4em;height:2.4em;margin:0;padding:0;border-radius:50%;background:rgba(0,0,0,.6);color:#fff}' +
        '.mtgDialogBackdropImage{width:100%;padding-top:33%;background-size:cover;background-position:center;background-color:#1c1c1c}' +
        '.mtgDialogContent{display:flex;flex-wrap:wrap;gap:1.5em;padding:1.5em}' +
        '.mtgDialogPoster{flex:0 0 10em;width:10em;height:15em;background-size:cover;background-position:center;background-color:#2b2b2b;border-radius:.3em}' +
        '.mtgDialogPosterSquare{height:10em}' +
        '.mtgDialogInfo{flex:1 1 16em;min-width:0}' +
        '.mtgDialogTitle{margin:0 0 .3em}' +
        '.mtgDialogTagline{font-style:italic;opacity:.8;margin:.3em 0}' +
        '.mtgDialogMeta,.mtgDialogGenres{opacity:.8;margin:.3em 0}' +
        '.mtgDialogOverview{margin:.6em 0}' +
        '.mtgDialogLinks,.mtgDialogActions{display:flex;flex-wrap:wrap;align-items:center;gap:.5em;margin-top:1em}' +
        '.mtgDialog .mtgActionButton{margin:0}' +
        '.mtgActionButton.mtgSent{opacity:.6}' +
        // Plain :focus, not :focus-visible: a TV has no mouse to distinguish from, and an older TV
        // browser that does not recognize :focus-visible would otherwise drop the rule entirely and
        // show no focus ring at all, which matters far more here than a mouse click briefly seeing one.
        '.mtgCard:focus{outline:3px solid #00a4dc;outline-offset:2px}' +
        '.mtgDialog :focus{outline:3px solid #00a4dc;outline-offset:2px}' +
        '.mtgSearchBox{display:flex;align-items:center;gap:.5em;margin-left:1.5em;flex:1 1 auto;min-width:0;max-width:26em}' +
        '.mtgSearchKind{flex:0 0 auto;background:rgba(255,255,255,.08);color:inherit;border:1px solid rgba(255,255,255,.3);border-radius:.3em;padding:.3em .4em}' +
        '.mtgSearchInput{flex:1 1 auto;min-width:0;background:rgba(255,255,255,.08);color:inherit;border:1px solid rgba(255,255,255,.3);border-radius:.3em;padding:.3em .6em}' +
        '.mtgSearchResultsBox{padding:0 0 .8em}' +
        '.mtgSearchNote{padding-left:1.5em}';
    document.head.appendChild(style);

    document.addEventListener('viewshow', onViewShow);
})();
