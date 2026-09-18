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
(function () {
    'use strict';

    var PERSON_ID = 'mtgPersonMissing';
    var RELATED_ID = 'mtgRelatedMissing';
    var HOME_ID = 'mtgHomeDiscover';
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

    function send(ctx, item, btn) {
        btn.disabled = true;
        var was = btn.textContent;
        btn.textContent = 'Sending\u2026';
        api('POST', actionUrl(ctx, 'Send'), { gapId: item.GapId }).then(function (result) {
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

    // A plain, static card: no dialog, no TV remote handling (unlike the report page's own rows, this
    // renders inside jellyfin-web's native page, which also sets the CSS containment that makes
    // position:fixed/absolute land in the wrong place, so everything here is plain in-flow content).
    function card(ctx, canSend, item) {
        var el = h('div', { 'class': 'card portraitCard mtgCard card-hoverable', 'data-gapid': item.GapId });
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

        var actions = h('div', { 'class': 'mtgCardActions' });
        if (resolveCanSend(canSend, item)) {
            var sendBtn = h('button', { 'is': 'emby-button', 'type': 'button', 'class': 'raised raised-mini mtgActionButton mtgSendButton' }, 'Download Now');
            sendBtn.addEventListener('click', function () { send(ctx, item, sendBtn); });
            actions.appendChild(sendBtn);
        } else if (ctx.canTodo) {
            var todoBtn = h('button', { 'is': 'emby-button', 'type': 'button', 'class': 'raised raised-mini mtgActionButton mtgTodoButton' }, 'Add to TODO');
            todoBtn.addEventListener('click', function () { addToTodo(ctx, item, todoBtn); });
            actions.appendChild(todoBtn);
        }

        var tmdbLink = h('a', {
            'href': tmdbUrl(item),
            'target': '_blank',
            'rel': 'noopener noreferrer',
            'class': 'raised raised-mini mtgActionButton mtgTmdbLink',
            'title': 'Open on TMDB'
        }, 'TMDB');
        tmdbLink.addEventListener('click', function (e) { e.stopPropagation(); });
        actions.appendChild(tmdbLink);
        box.appendChild(actions);

        el.appendChild(box);
        return el;
    }

    // A wrapping grid of cards (the person page).
    function grid(ctx, canSend, name, items) {
        var section = h('div', { 'class': 'verticalSection' });
        var head = h('div', { 'class': 'sectionTitleContainer sectionTitleContainer-cards' });
        head.appendChild(h('h2', { 'class': 'sectionTitle sectionTitle-cards' }, name + ' (' + items.length + ')'));
        section.appendChild(head);
        var container = h('div', { 'is': 'emby-itemscontainer', 'class': 'itemsContainer vertical-wrap padded-right' });
        items.forEach(function (item) { container.appendChild(card(ctx, canSend, item)); });
        section.appendChild(container);
        return section;
    }

    // A horizontal scroller of cards, the markup jellyfin-web uses for "More Like This" (the item page row).
    function scroller(ctx, canSend, name, items) {
        var section = h('div', { 'class': 'verticalSection' });
        section.appendChild(h('h2', { 'class': 'sectionTitle sectionTitle-cards' }, name));
        var scrollerEl = h('div', { 'is': 'emby-scroller', 'class': 'padded-top-focusscale padded-bottom-focusscale', 'data-centerfocus': 'true' });
        var container = h('div', { 'is': 'emby-itemscontainer', 'class': 'itemsContainer scrollSlider focuscontainer-x' });
        items.forEach(function (item) { container.appendChild(card(ctx, canSend, item)); });
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
        var section = scroller(ctx, canSend, 'Discover: not in your library', data.Titles);
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
        '.mtgCardActions{display:flex;justify-content:center;align-items:center;gap:.2em;margin-top:.35em}' +
        '.mtgCard .mtgActionButton{font-size:80%}' +
        '.mtgCard .mtgActionButton.mtgSent{opacity:.6}' +
        '.mtgCard a.mtgActionButton{text-decoration:none;display:inline-block}';
    document.head.appendChild(style);

    document.addEventListener('viewshow', onViewShow);
})();
