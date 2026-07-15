// Shared by both dashboard pages: the plugin id and the markup kit. Concatenated ahead of a page's
// own script inside the shell's IIFE, so these stay private to the page rather than becoming globals.
// Must not touch either page's DOM.
var pluginId = '8c2a93cc-6cc5-493a-880a-2e67ae50e454';

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

// A tiny hyperscript: build an element with its attributes escaped and its text set as
// textContent, so an external string (a provider name, a title) can never be parsed as markup
// and href/src are sanitized through safeUrl, leaving no manual esc()/safeUrl() at the call
// site. Callers serialize with .outerHTML (directly or via wrap) to compose into a row's markup;
// an emby-* element is built plain and upgrades when that markup is parsed by innerHTML.
function h(tag, attrs, text) {
    var el = document.createElement(tag);
    if (attrs) {
        Object.keys(attrs).forEach(function (k) {
            var v = attrs[k];
            if (v == null || v === false) { return; }
            if (k === 'class') { el.className = v; }
            else if (k === 'href' || k === 'src') { el.setAttribute(k, safeUrl(v)); }
            else { el.setAttribute(k, v); }
        });
    }
    if (text != null && text !== false) { el.textContent = String(text); }
    return el;
}

// Wrap already-built child markup in an element whose attributes h() escapes/sanitizes: build the
// empty element, then splice the child string in front of its (non-void) closing tag. Containers
// use this; pure-text leaves use h(tag, attrs, text).
function wrap(tag, attrs, innerHtml) {
    var outer = h(tag, attrs).outerHTML;
    var close = '</' + tag + '>';
    return outer.slice(0, -close.length) + (innerHtml || '') + close;
}
