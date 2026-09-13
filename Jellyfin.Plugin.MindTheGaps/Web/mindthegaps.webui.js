// Mind the Gaps: the web UI surfaces.
//
// Loaded by jellyfin-web's index.html (the plugin adds the script tag as the page is served). Renders, where
// the matching surface is switched on in the plugin settings:
//   - on a Person page, a "Missing from your library" section of the person's unowned movies and shows;
//   - on a Movie or Series page, a "More like this you don't have" row of unowned similar titles;
//   - on the home screen, a "Discover" row of the recommendations the scan has accumulated.
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
            surfacesPromise = api('GET', 'MindTheGaps/WebUi/Surfaces').catch(function () { surfacesPromise = null; return {}; });
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

    function serviceName(kind) {
        return kind === 'Movie' ? 'Radarr' : 'Sonarr';
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
                btn.textContent = 'Sent to ' + serviceName(item.Kind);
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
            b.textContent = 'Sent to ' + serviceName(item.Kind);
            b.disabled = true;
            b.classList.add('mtgSent');
        });
    }

    // ---- Detail dialog ----

    function closeDialog() {
        var dlg = document.getElementById(DIALOG_ID);
        if (!dlg) { return; }
        var restore = dlg.mtgRestoreFocus;
        dlg.parentNode.removeChild(dlg);
        document.removeEventListener('keydown', onDialogKey, true);
        if (restore && restore.focus) { restore.focus(); }
    }

    function onDialogKey(e) {
        var typing = e.target && (e.target.tagName === 'INPUT' || e.target.tagName === 'SELECT');
        if (e.key === 'Escape' || (e.key === 'Backspace' && !typing) || e.key === 'GoBack' || e.key === 'BrowserBack') {
            e.preventDefault();
            e.stopPropagation();
            closeDialog();
        }
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

    function renderDialog(ctx, item, detail, profiles) {
        closeDialog();
        var canSend = ctx.canSend(item.Kind);
        var overlay = h('div', { 'id': DIALOG_ID, 'class': 'mtgOverlay', 'role': 'dialog', 'aria-modal': 'true', 'aria-label': detail.Title });
        overlay.mtgRestoreFocus = document.activeElement;
        overlay.addEventListener('click', function (e) { if (e.target === overlay) { closeDialog(); } });

        var box = h('div', { 'class': 'mtgDialog' });
        var bg = safeImage(detail.BackdropUrl);
        if (bg) { box.style.backgroundImage = 'linear-gradient(rgba(16,16,16,.88), rgba(16,16,16,.97)), ' + bg; }

        var close = h('button', { 'is': 'paper-icon-button-light', 'type': 'button', 'class': 'mtgClose', 'title': 'Close', 'aria-label': 'Close' });
        close.appendChild(h('span', { 'class': 'material-icons close', 'aria-hidden': 'true' }));
        close.addEventListener('click', closeDialog);
        box.appendChild(close);

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
        var firstFocus = close;
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
            var dl = h('button', { 'is': 'emby-button', 'type': 'button', 'class': 'raised button-submit mtgDialogDownload' }, 'Download (' + serviceName(item.Kind) + ')');
            dl.addEventListener('click', function () {
                var profileId = select ? parseInt(select.value, 10) : 0;
                send(ctx, item, dl, profileId, function () { markSent(item); });
            });
            actions.appendChild(dl);
            firstFocus = dl;
        }
        text.appendChild(actions);
        body.appendChild(text);
        box.appendChild(body);
        overlay.appendChild(box);
        document.body.appendChild(overlay);
        document.addEventListener('keydown', onDialogKey, true);
        firstFocus.focus();
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

    // ---- Cards ----

    // A poster card in jellyfin-web's own card markup, so a row matches the page's other rows in every
    // theme. In the TV layout the card itself is the focusable element, as jellyfin-web's cards are, so a
    // controller lands on the poster (and the section header stays in view) rather than on the button.
    // `overflow` picks the horizontal-scroller card size; otherwise the grid size.
    function card(ctx, item, overflow) {
        var tv = isTv();
        var shape = overflow ? 'overflowPortrait' : 'portrait';
        var el = h(tv ? 'button' : 'div', {
            'class': 'card ' + shape + 'Card mtgCard' + (tv ? ' show-focus' : ' card-hoverable'),
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
        if (item.Role) { sub = sub ? sub + ' \u00b7 ' + item.Role : item.Role; }
        if (item.Because) { sub = sub ? sub + ' \u00b7 ' + item.Because : item.Because; }
        var secondary = h('div', { 'class': 'cardText cardTextCentered cardText-secondary', 'title': sub });
        secondary.appendChild(h('bdi', null, sub));
        box.appendChild(secondary);

        var open = function (e) {
            e.preventDefault();
            e.stopPropagation();
            openDetail(ctx, item);
        };
        (tv ? el : img).addEventListener('click', open);

        if (ctx.canSend(item.Kind)) {
            var btn = h('button', { 'is': 'emby-button', 'type': 'button', 'class': 'raised raised-mini mtgSendButton' }, 'Download (' + serviceName(item.Kind) + ')');
            btn.addEventListener('click', function (e) {
                e.stopPropagation();
                send(ctx, item, btn, 0, function () { markSent(item); });
            });
            box.appendChild(btn);
        }
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
            canSend: function (kind) { return kind === 'Movie' ? data.CanSendMovies : data.CanSendSeries; }
        };
        var section = scroller(ctx, "Discover: not in your library", data.Titles, 'padded-left');
        section.id = HOME_ID;
        sectionsEl.appendChild(section);
    }

    var homeObserver = null;

    function watchHome(page) {
        var sectionsEl = page.querySelector('#homeTab .sections');
        if (!sectionsEl) { return; }
        if (homeObserver) { homeObserver.disconnect(); }
        var timer = null;
        var load = function () {
            timer = null;
            if (sectionsEl.querySelector('#' + HOME_ID)) { return; }
            // Jellyfin has laid out its own sections (or found nothing to show) once the container has any
            // child; until then an appended row would be wiped by its innerHTML assignment.
            if (!sectionsEl.firstChild) { return; }
            api('GET', 'MindTheGaps/Home/Discover').then(function (data) { renderHome(sectionsEl, data); }, function () { /* off, or not signed in */ });
        };
        var schedule = function () {
            if (timer) { clearTimeout(timer); }
            timer = setTimeout(load, 250);
        };
        homeObserver = new MutationObserver(function (records) {
            for (var i = 0; i < records.length; i++) {
                // Ignore mutations inside our own row.
                if (records[i].target === sectionsEl) { schedule(); return; }
            }
        });
        homeObserver.observe(sectionsEl, { childList: true });
        schedule();
    }

    // ---- Routing ----

    function onViewShow(e) {
        var page = e.target;
        if (!page || !page.classList || !page.classList.contains('page') || !window.ApiClient) { return; }

        if (page.id === 'indexPage') {
            surfaces().then(function (s) { if (s.HomeRow) { watchHome(page); } });
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
            if (!item) { return; }
            if (item.Type === 'Person' && s.PersonPage) {
                return api('GET', 'MindTheGaps/Person/' + item.Id + '/Missing').then(function (data) {
                    if (token === pending) { renderPerson(page, item.Id, data); }
                });
            }
            if ((item.Type === 'Movie' || item.Type === 'Series') && s.ItemPage) {
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
        '.mtgCard{cursor:pointer}' +
        'button.mtgCard{background:none;border:0;padding:0;margin:0;color:inherit;font:inherit;text-align:inherit}' +
        '.mtgCard .mtgCardImage{border:0;padding:0;cursor:pointer;opacity:.85}' +
        '.mtgCard:hover .mtgCardImage,.mtgCard:focus-within .mtgCardImage{opacity:1}' +
        '.mtgCard .mtgSendButton{display:block;margin:.35em auto 0;font-size:80%}' +
        '.mtgCard .mtgSendButton.mtgSent,.mtgDialogDownload.mtgSent{opacity:.6}' +
        '.mtgUpcomingBadge{position:absolute;top:.5em;left:.5em;z-index:1;padding:.2em .6em;border-radius:.3em;background:rgba(0,0,0,.75);color:#fff;font-size:75%;line-height:1.4}' +
        '.mtgNote{opacity:.8}' +
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
