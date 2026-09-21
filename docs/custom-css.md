# Advanced CSS customization

Every outbound link in the report (the per-row provider links, the links on a creator or set group
header, and the ids in the Diagnose popup) is tagged so a stylesheet can target a specific service, for
example to inject a service icon. Each link carries:

- a per-provider class, `cgProvider-<service>`, with the service name lowercased and stripped to letters
  and digits: `cgProvider-tmdb`, `cgProvider-imdb`, `cgProvider-thetvdb`, `cgProvider-tvmaze`,
  `cgProvider-trakt`, `cgProvider-musicbrainz`, `cgProvider-discogs`, `cgProvider-openlibrary`,
  `cgProvider-justwatch`;
- a `data-provider` attribute with the original service name (`data-provider="TheTVDB"`), for attribute
  selectors.

Drop CSS into your server via the community
[Custom CSS](https://github.com/sealednut/jellyfin-custom-css) or
[File Transformation](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation) plugins (this
plugin ships no CSS or icons itself).

Here is a ready-to-paste snippet using [Simple Icons](https://simpleicons.org) (CC0), pulled from their CDN
at display time and masked to each link's text color, so the icons match your theme and recolor on hover.
Simple Icons carries TMDB, IMDb, Trakt, MusicBrainz, and Discogs; it does not carry TheTVDB, TVmaze,
OpenLibrary, or JustWatch, so those are left for you to point at your own hosted SVG.

```css
/* Mind the Gaps: provider icons. Paste into your custom CSS. */
.cgProvider-tmdb::before,
.cgProvider-imdb::before,
.cgProvider-trakt::before,
.cgProvider-musicbrainz::before,
.cgProvider-discogs::before {
    content: "";
    display: inline-block;
    width: 1em;
    height: 1em;
    margin-right: 0.35em;
    vertical-align: text-bottom;
    background-color: currentColor;
    -webkit-mask: var(--mtg-icon) center / contain no-repeat;
    mask: var(--mtg-icon) center / contain no-repeat;
}
.cgProvider-tmdb {
    --mtg-icon: url("https://cdn.simpleicons.org/themoviedatabase");
}
.cgProvider-imdb {
    --mtg-icon: url("https://cdn.simpleicons.org/imdb");
}
.cgProvider-trakt {
    --mtg-icon: url("https://cdn.simpleicons.org/trakt");
}
.cgProvider-musicbrainz {
    --mtg-icon: url("https://cdn.simpleicons.org/musicbrainz");
}
.cgProvider-discogs {
    --mtg-icon: url("https://cdn.simpleicons.org/discogs");
}

/* Not in Simple Icons. To add one, list its ::before in the rule above and host your own SVG, e.g.:
.cgProvider-thetvdb { --mtg-icon: url("/path/to/thetvdb.svg"); } */
```

The icons load from the Simple Icons CDN; self-host the SVGs instead if you prefer no external request. The
logos are trademarks of their respective services, so this is your choice to display them. These class and
attribute names are a stable contract; the link text and layout around them are not.

To go icon-only (hide the text label, show just the icon), add this alongside the snippet above:

```css
.cgProvider-tmdb,
.cgProvider-imdb,
.cgProvider-trakt,
.cgProvider-musicbrainz,
.cgProvider-discogs {
    font-size: 0; /* collapses the text label */
}
.cgProvider-tmdb::before,
.cgProvider-imdb::before,
.cgProvider-trakt::before,
.cgProvider-musicbrainz::before,
.cgProvider-discogs::before {
    font-size: 1rem; /* the icon is sized in em, so give it a real size again */
    margin-right: 0;
    vertical-align: middle;
}
```

Each link carries a `title` and an `aria-label` (for example "Open on TheTVDB"), so it stays labeled for
tooltips and screen readers even when shown icon-only.
