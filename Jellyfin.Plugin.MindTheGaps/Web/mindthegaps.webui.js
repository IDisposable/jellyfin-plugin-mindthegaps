// Mind the Gaps: the web UI surfaces.
//
// Loaded by jellyfin-web's index.html (the plugin adds the script tag as the page is served). Renders,
// where the matching surface is switched on in the plugin settings:
//   - on a Person page, a "Missing from your library" section of the person's unowned movies and shows;
//   - on a Movie or Series page, a "More like this you don't have" row of unowned similar titles;
//   - on the home screen, a "Discover" row of the recommendations the scan has accumulated.
// Talks to the server only through the web client's own ApiClient, so it inherits the signed-in user's
// session and base URL. Touches nothing but the elements it owns, and removes them again before rendering
// a different view.
//
// A card carries no actions itself: clicking anywhere on it opens a detail dialog (TMDB's own synopsis,
// genres, rating, a trailer link when TMDB has one) with the Send/Add-to-TODO button and, when sendable, a
// quality-profile picker moved into it. The dialog is appended to document.body rather than the page, since
// jellyfin-web's own page wrapper sets CSS containment (see CLAUDE.md's "position: fixed is not safe" note)
// which would otherwise make it the containing block for a fixed-position overlay and misplace it.
(function () {
    'use strict';

    var PERSON_ID = 'mtgPersonMissing';
    var RELATED_ID = 'mtgRelatedMissing';
    var HOME_ID = 'mtgHomeDiscover';

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

    function safeImage(url) {
        return url && /^https:\/\//i.test(url) ? 'url("' + url.replace(/["\\]/g, '') + '")' : null;
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

    function remove(page, id) {
        var old = page.querySelector('#' + id);
        if (old) { old.parentNode.removeChild(old); }
    }

    // ctx: { kind: 'Person'|'Item'|'Home', id: the owning page's Jellyfin id (empty for Home), canTodo }.
    function actionUrl(ctx, action) {
        return ctx.id ? 'MindTheGaps/' + ctx.kind + '/' + ctx.id + '/' + action : 'MindTheGaps/' + ctx.kind + '/' + action;
    }

    // The TMDB page for a card: always available, so a card is still actionable with no arr configured
    // and for a viewer who is not an administrator.
    function tmdbUrl(item) {
        return 'https://www.themoviedb.org/' + (item.Kind === 'Series' ? 'tv' : 'movie') + '/' + item.TmdbId;
    }

    function send(ctx, item, btn, qualityProfileId) {
        btn.disabled = true;
        var was = btn.textContent;
        btn.textContent = 'Sending\u2026';
        var params = { gapId: item.GapId };
        if (qualityProfileId) { params.qualityProfileId = qualityProfileId; }
        api('POST', actionUrl(ctx, 'Send'), params).then(function (result) {
            if (result && result.Success) {
                btn.textContent = 'Sent';
                btn.classList.add('mtgSent');
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

    // The fallback for an administrator with no arr configured: adds the title to the personal todo list
    // instead of sending it anywhere. Independent of the report's own Todo/Add, which only rehydrates a
    // gap already in the persisted scan; this rehydrates through the same on-demand lookup the card's own
    // data came from, so it still works for a person or title the scan rotation has not reached yet.
    function addToTodo(ctx, item, btn) {
        btn.disabled = true;
        var was = btn.textContent;
        btn.textContent = 'Adding\u2026';
        api('POST', actionUrl(ctx, 'Todo'), { gapId: item.GapId }).then(function (count) {
            if (count > 0) {
                btn.textContent = 'Added to TODO';
                btn.classList.add('mtgSent');
            } else {
                btn.textContent = was;
                btn.disabled = false;
                alertUser('Could not add that to your TODO list.');
            }
        }, function () {
            btn.textContent = was;
            btn.disabled = false;
            alertUser('Could not reach the server.');
        });
    }

    // canSend is a plain boolean for a single-kind list (a person's Movies, an item's similar titles, all
    // one kind), or a function(item) for a mixed list (the home row, movies and series interleaved).
    function resolveCanSend(canSend, item) {
        return typeof canSend === 'function' ? canSend(item) : !!canSend;
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

    // The Send/Add-to-TODO control: independent of the TMDB detail lookup below, since a gap already
    // carries everything a send needs, so it must not wait on (or fail because of) a slow or failing TMDB
    // call. A sendable card gets a quality-profile picker too, populated lazily and left hidden (Send
    // still works with the configured default) if that lookup fails.
    function renderSendOrTodo(actionsEl, ctx, canSend, item) {
        if (resolveCanSend(canSend, item)) {
            var select = h('select', { 'is': 'emby-select', 'class': 'selectSmall mtgProfileSelect' });
            select.style.display = 'none';
            var sendBtn = h('button', { 'type': 'button', 'class': ACTION_BUTTON + ' mtgSendButton' }, 'Download Now');
            sendBtn.addEventListener('click', function () {
                var profileId = select.value ? parseInt(select.value, 10) : null;
                send(ctx, item, sendBtn, profileId);
            });
            actionsEl.appendChild(select);
            actionsEl.appendChild(sendBtn);
            api('GET', 'MindTheGaps/WebUi/Profiles', { kind: item.Kind }).then(function (result) {
                if (!result || !result.Profiles || !result.Profiles.length) { return; }
                result.Profiles.forEach(function (p) {
                    var opt = h('option', { 'value': p.Id }, p.Name);
                    if (p.Id === result.DefaultId) { opt.selected = true; }
                    select.appendChild(opt);
                });
                select.style.display = '';
            }, function () { /* leave it hidden; Send still uses the configured default profile */ });
        } else if (ctx.canTodo) {
            var todoBtn = h('button', { 'type': 'button', 'class': ACTION_BUTTON + ' mtgTodoButton' }, 'Add to TODO');
            todoBtn.addEventListener('click', function () { addToTodo(ctx, item, todoBtn); });
            actionsEl.appendChild(todoBtn);
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
        var bg = safeImage(detail.BackdropUrl);
        if (bg) { refs.backdropImg.style.backgroundImage = bg; }
        var posterBg = safeImage(detail.PosterUrl);
        if (posterBg) { refs.poster.style.backgroundImage = posterBg; }

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
    // the card), the TMDB link, and the Send/Add-to-TODO action.
    function dialogBody(ctx, canSend, item) {
        var body = h('div', { 'class': 'mtgDialogBody' });
        var backdropImg = h('div', { 'class': 'mtgDialogBackdropImage' });
        body.appendChild(backdropImg);

        var content = h('div', { 'class': 'mtgDialogContent' });
        var poster = h('div', { 'class': 'mtgDialogPoster' });
        var bg = safeImage(item.ImageUrl);
        if (bg) { poster.style.backgroundImage = bg; }
        content.appendChild(poster);

        var info = h('div', { 'class': 'mtgDialogInfo' });
        info.appendChild(h('h2', { 'class': 'mtgDialogTitle' }, item.Title + (item.Year ? ' (' + item.Year + ')' : '')));
        var loading = h('p', { 'class': 'mtgNote' }, 'Loading details\u2026');
        info.appendChild(loading);

        var links = h('div', { 'class': 'mtgDialogLinks' });
        links.appendChild(h('a', { 'href': tmdbUrl(item), 'target': '_blank', 'rel': 'noopener noreferrer', 'class': ACTION_BUTTON }, 'View on TMDB'));
        info.appendChild(links);

        var actions = h('div', { 'class': 'mtgDialogActions' });
        info.appendChild(actions);
        renderSendOrTodo(actions, ctx, canSend, item);

        content.appendChild(info);
        body.appendChild(content);
        return { body: body, info: info, loading: loading, links: links, poster: poster, backdropImg: backdropImg };
    }

    function openDialog(ctx, canSend, item) {
        ensureDialog();
        var wasOpen = dialogEl.classList.contains('mtgDialogOpen');
        var token = ++dialogToken;
        var old = dialogInner.querySelector('.mtgDialogBody');
        if (old) { old.remove(); }
        var refs = dialogBody(ctx, canSend, item);
        dialogInner.appendChild(refs.body);
        dialogEl.classList.add('mtgDialogOpen');

        if (!wasOpen) {
            dialogOpenerEl = document.activeElement;
            window.history.pushState({ mtgDialog: true }, '');
            dialogHistoryPushed = true;
        }

        // Autofocus the close button, not the Send button: a remote's Select right after opening must
        // not risk triggering an action before the title has even loaded.
        dialogCloseBtn.focus();

        api('GET', 'MindTheGaps/WebUi/Detail', { tmdbId: item.TmdbId, kind: item.Kind }).then(function (detail) {
            if (token !== dialogToken) { return; }
            if (detail) { fillDialogDetail(refs, detail); } else { refs.loading.textContent = 'No further details available.'; }
        }, function () {
            if (token !== dialogToken) { return; }
            refs.loading.textContent = 'Could not load details from TMDB.';
        });
    }

    // A plain card: image, title, year/role. No actions of its own (unlike the report page's own rows,
    // this renders inside jellyfin-web's native page, which also sets the CSS containment that makes
    // position:fixed/absolute land in the wrong place, which is also why the dialog itself is appended to
    // document.body rather than here); clicking anywhere on it, or pressing Enter/Space while it has
    // focus, opens the detail dialog. tabindex/role make it reachable at all from a keyboard or a
    // remote's D-pad: without them a plain div is invisible to Tab order and jellyfin-web's own focus
    // conventions do not apply to it (see the detail dialog's own header comment for why not).
    function card(ctx, canSend, item) {
        var el = h('div', { 'class': 'card portraitCard mtgCard card-hoverable', 'data-gapid': item.GapId, 'tabindex': '0', 'role': 'button' });
        var box = h('div', { 'class': 'cardBox cardBox-bottompadded' });
        var scalable = h('div', { 'class': 'cardScalable' });
        scalable.appendChild(h('div', { 'class': 'cardPadder cardPadder-portrait' }));
        var img = h('div', { 'class': 'cardImageContainer coveredImage cardContent' });
        var bg = safeImage(item.ImageUrl);
        if (bg) {
            img.style.backgroundImage = bg;
        } else {
            img.classList.add('defaultCardBackground', 'defaultCardBackground1');
            img.appendChild(h('div', { 'class': 'cardText cardDefaultText' }, item.Title));
        }

        if (item.Upcoming) { img.appendChild(h('div', { 'class': 'mtgUpcomingBadge' }, 'Upcoming')); }
        scalable.appendChild(img);
        box.appendChild(scalable);

        var title = h('div', { 'class': 'cardText cardTextCentered cardText-first' });
        title.appendChild(h('bdi', null, item.Title));
        box.appendChild(title);

        var sub = item.Year ? String(item.Year) : '';
        if (item.Role) { sub = sub ? sub + ' \u00b7 ' + item.Role : item.Role; }
        var secondary = h('div', { 'class': 'cardText cardTextCentered cardText-secondary', 'title': sub });
        secondary.appendChild(h('bdi', null, sub));
        box.appendChild(secondary);

        el.appendChild(box);
        el.addEventListener('click', function () { openDialog(ctx, canSend, item); });
        el.addEventListener('keydown', function (e) {
            if (e.key === 'Enter' || e.key === ' ' || e.key === 'Spacebar') {
                e.preventDefault();
                openDialog(ctx, canSend, item);
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
    function grid(ctx, canSend, name, items) {
        var section = h('div', { 'class': 'verticalSection' });
        var head = h('div', { 'class': 'sectionTitleContainer sectionTitleContainer-cards' });
        head.appendChild(h('h2', { 'class': 'sectionTitle sectionTitle-cards' }, name + ' (' + items.length + ')'));
        section.appendChild(head);
        var container = h('div', { 'is': 'emby-itemscontainer', 'class': 'itemsContainer vertical-wrap padded-right' });
        items.forEach(function (item) { container.appendChild(card(ctx, canSend, item)); });
        wireCardNavigation(container);
        section.appendChild(container);
        return section;
    }

    // A horizontal scroller of cards, in the markup jellyfin-web uses for its own rows, which differs by page.
    // On the item page the row sits in .detailVerticalSection, which already pads the left edge, so the
    // scroller takes no-padding of its own and the title goes straight in. A home section is not padded by its
    // parent: its title sits in a padded-left container and the scroller supplies the cards' own offset.
    function scroller(ctx, canSend, name, items, onHome) {
        var section = h('div', { 'class': 'verticalSection' });
        if (onHome) {
            var head = h('div', { 'class': 'sectionTitleContainer sectionTitleContainer-cards padded-left' });
            head.appendChild(h('h2', { 'class': 'sectionTitle sectionTitle-cards' }, name));
            section.appendChild(head);
        } else {
            section.appendChild(h('h2', { 'class': 'sectionTitle sectionTitle-cards padded-right' }, name));
        }
        var scrollerEl = h('div', { 'is': 'emby-scroller', 'class': 'padded-top-focusscale padded-bottom-focusscale' + (onHome ? '' : ' no-padding'), 'data-centerfocus': 'true' });
        var container = h('div', { 'is': 'emby-itemscontainer', 'class': 'itemsContainer scrollSlider focuscontainer-x' });
        items.forEach(function (item) { container.appendChild(card(ctx, canSend, item)); });
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
        if (data.Movies.length) { wrap.appendChild(grid(ctx, data.CanSendMovies, 'Movies', data.Movies)); }
        if (data.Series.length) { wrap.appendChild(grid(ctx, data.CanSendSeries, 'Shows', data.Series)); }

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
            section = scroller(ctx, data.CanSend, "More like this you don't have", data.Titles);
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

    // ---- Home ----

    function renderHome(sectionsEl, data) {
        remove(sectionsEl, HOME_ID);
        if (!data || !data.Titles.length) { return; }

        var ctx = { kind: 'Home', id: '', canTodo: !!data.CanTodo };
        var canSend = function (item) { return item.Kind === 'Movie' ? data.CanSendMovies : data.CanSendSeries; };
        var section = scroller(ctx, canSend, 'Discover: not in your library', data.Titles, true);
        section.id = HOME_ID;
        sectionsEl.appendChild(section);
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
        };
        var schedule = function () {
            if (timer) { clearTimeout(timer); }
            timer = setTimeout(load, 250);
        };
        var ours = function (node) { return node.nodeType === 1 && node.id === HOME_ID; };
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
        if (!itemId) { remove(page, PERSON_ID); remove(page, RELATED_ID); return; }

        var token = ++pending;
        Promise.resolve(ApiClient.getItem(ApiClient.getCurrentUserId(), itemId)).then(function (item) {
            if (token !== pending) { return; }
            remove(page, PERSON_ID);
            remove(page, RELATED_ID);
            if (!item) { return; }
            if (item.Type === 'Person') {
                return api('GET', 'MindTheGaps/Person/' + item.Id + '/Missing').then(function (data) {
                    if (token === pending) { renderPerson(page, item.Id, data); }
                });
            }

            if (item.Type === 'Movie' || item.Type === 'Series') {
                return api('GET', 'MindTheGaps/Item/' + item.Id + '/Related').then(function (data) {
                    if (token === pending) { renderRelated(page, item.Id, data); }
                });
            }
        }).catch(function () {
            // A 404 means the surface was switched off or the id is not one we handle; either way show nothing.
            if (token === pending) { remove(page, PERSON_ID); remove(page, RELATED_ID); }
        });
    }

    var style = document.createElement('style');
    style.textContent =
        '.mtgUpcomingBadge{position:absolute;top:.5em;left:.5em;z-index:1;padding:.2em .6em;border-radius:.3em;background:rgba(0,0,0,.75);color:#fff;font-size:75%;line-height:1.4}' +
        '.mtgNote{opacity:.8}' +
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
        '.mtgDialogInfo{flex:1 1 16em;min-width:0}' +
        '.mtgDialogTitle{margin:0 0 .3em}' +
        '.mtgDialogTagline{font-style:italic;opacity:.8;margin:.3em 0}' +
        '.mtgDialogMeta,.mtgDialogGenres{opacity:.8;margin:.3em 0}' +
        '.mtgDialogOverview{margin:.6em 0}' +
        '.mtgDialogLinks,.mtgDialogActions{display:flex;flex-wrap:wrap;align-items:center;gap:.5em;margin-top:1em}' +
        '.mtgDialog .mtgActionButton{margin:0}' +
        '.mtgActionButton.mtgSent{opacity:.6}' +
        '.mtgProfileSelect{max-width:12em}' +
        // Plain :focus, not :focus-visible: a TV has no mouse to distinguish from, and an older TV
        // browser that does not recognize :focus-visible would otherwise drop the rule entirely and
        // show no focus ring at all, which matters far more here than a mouse click briefly seeing one.
        '.mtgCard:focus{outline:3px solid #00a4dc;outline-offset:2px}' +
        '.mtgDialog :focus{outline:3px solid #00a4dc;outline-offset:2px}';
    document.head.appendChild(style);

    document.addEventListener('viewshow', onViewShow);
})();
