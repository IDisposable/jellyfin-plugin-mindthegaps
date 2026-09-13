// Mind the Gaps: the person page "Missing" section.
//
// Loaded by jellyfin-web's index.html (the plugin adds the script tag as the page is served). On every
// details view that turns out to be a Person, asks the plugin which of the person's credited movies and
// series the library lacks, and renders them as two card rows below the person's own items, with a Send
// button for an administrator when Radarr/Sonarr are configured. Talks to the server only through the web
// client's own ApiClient, so it inherits the signed-in user's session and base URL. Touches nothing but
// the section it owns, and removes that section again before rendering a different person.
(function () {
    'use strict';

    var SECTION_ID = 'mtgPersonMissing';
    var pending = 0;

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
    }

    // A poster card in jellyfin-web's own card markup, so the row matches the page's other rows in every
    // theme. No link target: the title is not in the library, so there is nothing to open.
    function card(item, kind, canSend, onSend) {
        var el = h('div', { 'class': 'card portraitCard mtgMissingCard', 'data-gapid': item.GapId });
        var box = h('div', { 'class': 'cardBox cardBox-bottompadded' });
        var scalable = h('div', { 'class': 'cardScalable' });
        scalable.appendChild(h('div', { 'class': 'cardPadder cardPadder-portrait' }));
        var img = h('div', { 'class': 'cardImageContainer coveredImage cardContent', 'aria-label': item.Title, 'role': 'img' });
        if (item.ImageUrl && /^https:\/\//i.test(item.ImageUrl)) {
            img.style.backgroundImage = 'url("' + item.ImageUrl.replace(/["\\]/g, '') + '")';
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

        if (canSend) {
            var btn = h('button', { 'is': 'emby-button', 'type': 'button', 'class': 'raised raised-mini mtgSendButton' }, kind === 'movie' ? 'Send to Radarr' : 'Send to Sonarr');
            btn.addEventListener('click', function () { onSend(item, btn); });
            box.appendChild(btn);
        }
        el.appendChild(box);
        return el;
    }

    function row(name, items, kind, canSend, onSend) {
        var section = h('div', { 'class': 'verticalSection' });
        var head = h('div', { 'class': 'sectionTitleContainer sectionTitleContainer-cards' });
        head.appendChild(h('h2', { 'class': 'sectionTitle sectionTitle-cards' }, name + ' (' + items.length + ')'));
        section.appendChild(head);
        var container = h('div', { 'is': 'emby-itemscontainer', 'class': 'itemsContainer vertical-wrap padded-right' });
        items.forEach(function (item) { container.appendChild(card(item, kind, canSend, onSend)); });
        section.appendChild(container);
        return section;
    }

    function send(personId, item, btn) {
        btn.disabled = true;
        var was = btn.textContent;
        btn.textContent = 'Sending\u2026';
        ApiClient.ajax({
            type: 'POST',
            url: ApiClient.getUrl('MindTheGaps/Person/' + personId + '/Send', { gapId: item.GapId }),
            dataType: 'json'
        }).then(function (result) {
            if (result && result.Success) {
                btn.textContent = 'Sent';
                btn.classList.add('mtgSent');
            } else {
                btn.textContent = was;
                btn.disabled = false;
                if (window.Dashboard && Dashboard.alert) { Dashboard.alert((result && result.Message) || 'Send failed.'); }
            }
        }, function () {
            btn.textContent = was;
            btn.disabled = false;
            if (window.Dashboard && Dashboard.alert) { Dashboard.alert('Send failed.'); }
        });
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

        var onSend = function (item, btn) { send(personId, item, btn); };
        if (data.Movies.length) { wrap.appendChild(row('Movies', data.Movies, 'movie', data.CanSendMovies, onSend)); }
        if (data.Series.length) { wrap.appendChild(row('Shows', data.Series, 'series', data.CanSendSeries, onSend)); }

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
        '.mtgMissingCard .cardImageContainer{opacity:.85}' +
        '.mtgMissingCard .mtgSendButton{display:block;margin:.35em auto 0;font-size:80%}' +
        '.mtgMissingCard .mtgSendButton.mtgSent{opacity:.6}' +
        '.mtgUpcomingBadge{position:absolute;top:.5em;left:.5em;z-index:1;padding:.2em .6em;border-radius:.3em;background:rgba(0,0,0,.75);color:#fff;font-size:75%;line-height:1.4}' +
        '.mtgMissingNote{opacity:.8}';
    document.head.appendChild(style);
    document.addEventListener('viewshow', onViewShow);
})();
