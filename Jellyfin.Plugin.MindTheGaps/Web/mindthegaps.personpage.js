// Mind the Gaps: the person page "Missing" section.
//
// Loaded by jellyfin-web's index.html (the plugin adds the script tag as the page is served). On every
// details view that turns out to be a Person, asks the plugin which of the person's credited movies and
// series the library lacks, and renders them as card rows below the person's own items. A card opens a
// detail dialog (TMDB's summary, rating, links, and for an administrator a quality-profile choice and a
// Download button); the button under each card downloads with the configured default profile. Talks to
// the server only through the web client's own ApiClient, so it inherits the signed-in user's session
// and base URL. Touches nothing but the section and dialog it owns, and removes the section again before
// rendering a different item.
(function () {
    'use strict';

    var SECTION_ID = 'mtgPersonMissing';
    var DIALOG_ID = 'mtgPersonDialog';
    var pending = 0;
    var profilesPromise = null;

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

    // The details page is `#/details?id=…` on a hash router; read the id from the hash, not the search.
    function itemIdFromLocation() {
        var hash = window.location.hash || '';
        var q = hash.indexOf('?');
        if (hash.indexOf('/details') < 0 || q < 0) { return null; }
        return new URLSearchParams(hash.slice(q + 1)).get('id');
    }

    function removeSection(page) {
        var old = page.querySelector('#' + SECTION_ID);
        if (old) { old.parentNode.removeChild(old); }
        closeDialog();
    }

    function serviceName(kind) {
        return kind === 'movie' ? 'Radarr' : 'Sonarr';
    }

    function send(personId, item, kind, btn, profileId, onDone) {
        btn.disabled = true;
        var was = btn.textContent;
        btn.textContent = 'Sending\u2026';
        var params = { gapId: item.GapId };
        if (profileId) { params.qualityProfileId = profileId; }
        ApiClient.ajax({
            type: 'POST',
            url: ApiClient.getUrl('MindTheGaps/Person/' + personId + '/Send', params),
            dataType: 'json'
        }).then(function (result) {
            if (result && result.Success) {
                btn.textContent = 'Sent to ' + serviceName(kind);
                btn.classList.add('mtgSent');
                if (onDone) { onDone(true); }
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
        if (e.key === 'Escape' || e.key === 'Backspace' && e.target && e.target.tagName !== 'INPUT' && e.target.tagName !== 'SELECT' || e.key === 'GoBack' || e.key === 'BrowserBack') {
            e.preventDefault();
            e.stopPropagation();
            closeDialog();
        }
    }

    function loadProfiles() {
        if (!profilesPromise) {
            profilesPromise = ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('MindTheGaps/PersonPage/Profiles'), dataType: 'json' })
                .catch(function () { profilesPromise = null; return null; });
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

    function renderDialog(personId, item, kind, canSend, detail, profiles) {
        closeDialog();
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
        if (detail.Tagline) { text.appendChild(h('div', { 'class': 'mtgDialogTagline' }, detail.Tagline)); }
        text.appendChild(h('p', { 'class': 'mtgDialogOverview' }, detail.Overview || 'No overview on TMDB.'));

        var links = h('div', { 'class': 'mtgDialogLinks' });
        [['TMDB', detail.TmdbUrl], ['IMDb', detail.ImdbUrl], ['Trailer', detail.TrailerUrl]].forEach(function (pair) {
            var href = safeHref(pair[1]);
            if (!href) { return; }
            var a = h('a', { 'is': 'emby-linkbutton', 'class': 'button-link', 'href': href, 'target': '_blank', 'rel': 'noopener noreferrer' }, pair[0]);
            links.appendChild(a);
        });
        if (links.childNodes.length) { text.appendChild(links); }

        var actions = h('div', { 'class': 'mtgDialogActions' });
        var firstFocus = close;
        if (canSend) {
            var list = profiles ? (kind === 'movie' ? profiles.Radarr : profiles.Sonarr) : null;
            var defaultId = profiles ? (kind === 'movie' ? profiles.RadarrDefault : profiles.SonarrDefault) : 0;
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
            var dl = h('button', { 'is': 'emby-button', 'type': 'button', 'class': 'raised button-submit mtgDialogDownload' }, 'Download (' + serviceName(kind) + ')');
            dl.addEventListener('click', function () {
                var profileId = select ? parseInt(select.value, 10) : 0;
                send(personId, item, kind, dl, profileId, function () {
                    var cardBtn = document.querySelector('#' + SECTION_ID + ' [data-gapid="' + item.GapId + '"] .mtgSendButton');
                    if (cardBtn) { cardBtn.textContent = 'Sent to ' + serviceName(kind); cardBtn.disabled = true; cardBtn.classList.add('mtgSent'); }
                });
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

    function openDetail(personId, item, kind, canSend) {
        var loads = [ApiClient.ajax({
            type: 'GET',
            url: ApiClient.getUrl('MindTheGaps/Person/' + personId + '/Missing/Detail', { gapId: item.GapId }),
            dataType: 'json'
        })];
        loads.push(canSend ? loadProfiles() : Promise.resolve(null));
        Promise.all(loads).then(function (results) {
            renderDialog(personId, item, kind, canSend, results[0], results[1]);
        }, function () {
            alertUser('Could not load details for ' + item.Title + '.');
        });
    }

    // ---- Section ----

    // A poster card in jellyfin-web's own card markup, so the row matches the page's other rows in every
    // theme. In the TV layout the card itself is the focusable element, as jellyfin-web's cards are, so a
    // controller lands on the poster (and the section header stays in view) rather than on the button.
    function card(personId, item, kind, canSend) {
        var tv = isTv();
        var el = h(tv ? 'button' : 'div', {
            'class': 'card portraitCard mtgMissingCard' + (tv ? ' show-focus' : ' card-hoverable'),
            'data-gapid': item.GapId,
            'type': tv ? 'button' : null,
            'aria-label': tv ? item.Title : null
        });
        var box = h('div', { 'class': 'cardBox cardBox-bottompadded' });
        var scalable = h('div', { 'class': 'cardScalable' });
        scalable.appendChild(h('div', { 'class': 'cardPadder cardPadder-portrait' }));
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
        var secondary = h('div', { 'class': 'cardText cardTextCentered cardText-secondary', 'title': sub });
        secondary.appendChild(h('bdi', null, sub));
        box.appendChild(secondary);

        var open = function (e) {
            e.preventDefault();
            e.stopPropagation();
            openDetail(personId, item, kind, canSend);
        };
        (tv ? el : img).addEventListener('click', open);

        if (canSend) {
            var btn = h('button', { 'is': 'emby-button', 'type': 'button', 'class': 'raised raised-mini mtgSendButton' }, 'Download (' + serviceName(kind) + ')');
            btn.addEventListener('click', function (e) {
                e.stopPropagation();
                send(personId, item, kind, btn, 0, null);
            });
            box.appendChild(btn);
        }
        el.appendChild(box);
        return el;
    }

    function row(personId, name, items, kind, canSend) {
        var section = h('div', { 'class': 'verticalSection' });
        var head = h('div', { 'class': 'sectionTitleContainer sectionTitleContainer-cards' });
        head.appendChild(h('h2', { 'class': 'sectionTitle sectionTitle-cards' }, name + ' (' + items.length + ')'));
        section.appendChild(head);
        var container = h('div', { 'is': 'emby-itemscontainer', 'class': 'itemsContainer vertical-wrap padded-right' });
        items.forEach(function (item) { container.appendChild(card(personId, item, kind, canSend)); });
        section.appendChild(container);
        return section;
    }

    function render(page, personId, data) {
        removeSection(page);
        if (!data || (!data.Reason && !data.Movies.length && !data.Series.length)) { return; }

        var wrap = h('div', { 'id': SECTION_ID, 'class': 'detailPageSecondaryContainer padded-left padded-bottom-page' });
        var lead = h('div', { 'class': 'verticalSection' });
        lead.appendChild(h('h2', { 'class': 'sectionTitle sectionTitle-cards' }, 'Missing from your library'));
        if (data.Reason) {
            lead.appendChild(h('p', { 'class': 'mtgMissingNote' }, data.Reason));
        }
        wrap.appendChild(lead);

        if (data.Movies.length) { wrap.appendChild(row(personId, 'Movies', data.Movies, 'movie', data.CanSendMovies)); }
        if (data.Series.length) { wrap.appendChild(row(personId, 'Shows', data.Series, 'series', data.CanSendSeries)); }

        // Below the person's own items, above "More like this".
        var anchor = page.querySelector('#similarCollapsible');
        var host = anchor ? anchor.parentNode : page.querySelector('.detailPageContent') || page;
        host.insertBefore(wrap, anchor || null);
    }

    function onViewShow(e) {
        var page = e.target;
        if (!page || !page.classList || !page.classList.contains('page')) { return; }
        var itemId = itemIdFromLocation();
        if (!itemId || !window.ApiClient) { removeSection(page); return; }

        var token = ++pending;
        ApiClient.getItem(ApiClient.getCurrentUserId(), itemId).then(function (item) {
            if (token !== pending) { return; }
            if (!item || item.Type !== 'Person') { removeSection(page); return; }
            return ApiClient.ajax({
                type: 'GET',
                url: ApiClient.getUrl('MindTheGaps/Person/' + item.Id + '/Missing'),
                dataType: 'json'
            }).then(function (data) {
                if (token === pending) { render(page, item.Id, data); }
            });
        }).catch(function () {
            // A 404 means the feature was switched off or the id is not a person; either way show nothing.
            if (token === pending) { removeSection(page); }
        });
    }

    var style = document.createElement('style');
    style.textContent =
        '.mtgMissingCard{cursor:pointer}' +
        'button.mtgMissingCard{background:none;border:0;padding:0;margin:0;color:inherit;font:inherit;text-align:inherit}' +
        '.mtgMissingCard .mtgCardImage{border:0;padding:0;cursor:pointer;opacity:.85}' +
        '.mtgMissingCard:hover .mtgCardImage,.mtgMissingCard:focus-within .mtgCardImage{opacity:1}' +
        '.mtgMissingCard .mtgSendButton{display:block;margin:.35em auto 0;font-size:80%}' +
        '.mtgMissingCard .mtgSendButton.mtgSent,.mtgDialogDownload.mtgSent{opacity:.6}' +
        '.mtgUpcomingBadge{position:absolute;top:.5em;left:.5em;z-index:1;padding:.2em .6em;border-radius:.3em;background:rgba(0,0,0,.75);color:#fff;font-size:75%;line-height:1.4}' +
        '.mtgMissingNote{opacity:.8}' +
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
