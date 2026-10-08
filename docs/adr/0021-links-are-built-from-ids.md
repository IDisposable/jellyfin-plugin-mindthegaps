# 21. Links are built from ids, never stored; the plugin provides the ids it depends on

Status: Accepted.

## Context

A gap carried its external links (`Links`) and its source's links (`SourceLinks`) as stored URLs beside the
provider ids they were built from. Every one of those URLs was a copy of what the ids already said, except
two that a source added by hand: a JustWatch title page from the JustWatch watchlist and a Trakt page from a
Trakt filmography slug. Storing them meant a report held thousands of redundant URLs, a change in how a link
is made reached only gaps a later scan rebuilt, and a provider plugin installed since a gap was found added
nothing to it.

The book and music sources also depend on two ids core has no provider for: OpenLibrary (a book is owned,
and its author resolved, by its work id) and Discogs (an album is owned by its release id, an artist's
discography is read by its artist id). With no provider registered, the metadata editor has no field for
either, so there was no way to give a book the id the book sources need.

## Decision

- **Links are built from ids, on every read.** `ExternalLinkEnricher.Fill` builds a gap's links from its
  provider ids (the host's url providers over the hand-built `ProviderLinks`) and its source's links from
  `SourceProviderIds` (`CreatorLinks`). It runs on whatever a scan, an explore, a re-check or the
  availability pass hands over, and on the report when `GapStore` loads it. `StoredJson` never writes
  `Links`, `SourceLinks` or a TODO entry's `Links`; a TODO entry's links are rebuilt when its list is read.
- **Nothing a link needs lives only in the link.** The JustWatch title path is the `JustWatch` provider id,
  which is what the JustWatch plugin stamps on an item. Trakt is linked from the IMDb id, as the Trakt plugin
  does, when the Trakt sources are on.
- **Existing files convert on load.** `LegacyLinkIds` reads the JustWatch path and a source's ids back out of
  the links an older file stored, before they are dropped, so nothing is lost before the next scan. The first
  save after a load writes the new shape.
- **An address on a known host is stored as a token.** `StoredUrls` shortens image and offer addresses on the
  hosts the plugin builds them on (`~tmdbimg/...` for `https://image.tmdb.org/t/p/...`). The tokens are a
  storage format: one is never renamed or reused for another host. An address on any other host, and every
  address a file written before this holds, reads back unchanged.
- **The plugin registers providers for the ids it depends on, and steps aside for a dedicated one.**
  `OpenLibraryExternalId`/`OpenLibraryExternalUrlProvider` and `DiscogsExternalId`/`DiscogsExternalUrlProvider`
  give those ids a field in the metadata editor and a link on an item's page. `ProviderPrecedence` reads the
  other enabled plugins through `IPluginManager` once: a provider type with a parameterless constructor is
  created and its key or name read, and a plugin with a provider that cannot be created that way claims what
  its name contains. When another plugin claims a key, this plugin's provider for it returns nothing. TheTVDB
  and JustWatch have their own plugins, so this plugin registers nothing for them and keeps its hand-built
  links as the fallback when those plugins are absent; the host's link wins by name when they are present.

## Consequences

- A report on disk holds ids, not links, and a link format change or a newly installed provider plugin
  reaches every gap on the next load.
- Loading a report builds every gap's links through the host's url providers once per process, which is the
  same pass a scan already made over every gap.
- The provider-id map is the one input to a link, so a source that wants a link it cannot build from a
  standard id adds the id, not the link.
- A dedicated OpenLibrary or Discogs plugin takes over by being installed. Its id and url provider classes
  should have parameterless constructors so the takeover is exact rather than matched by name.
- Gap ids remain the persistence contract of [ADR-0008](0008-stable-gap-ids.md); links never were part of it.
