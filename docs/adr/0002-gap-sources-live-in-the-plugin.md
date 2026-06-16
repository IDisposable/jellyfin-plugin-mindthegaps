# 2. Gap sources live in the plugin (no plugin-to-plugin SPI)

Status: Accepted.

## Context

It would be elegant to let other plugins contribute their own `IGapSource` implementations (a Trakt
gap plugin, a MusicBrainz gap plugin, and so on), the way metadata providers compose. But Jellyfin
loads each plugin in its own isolated, collectible `PluginLoadContext`, and there is no first-class
plugin-to-plugin SPI: an interface defined inside one plugin cannot be implemented from another
without shipping a duplicate of the defining assembly (a different type identity) or failing to
resolve at all.

## Decision

All gap sources ship inside this plugin, under `Gaps/Sources/{Tmdb,Trakt,Library,Series}/`.
Integrations that flow through host-mediated data (a provider id stamped on a `BaseItem`, like the
JustWatch link rendered from `ProviderIds["JustWatch"]`) can live in separate plugins, because they
do not implement an in-process interface.

## Consequences

- One plugin carries every source; adding a source is a code change here, not a separate plugin.
- A real "gap provider" ecosystem would require upstreaming `IGapSource` plus the small result model
  (`GapItem`, `GapPattern`, `MediaDomain`) into a host assembly. That is captured as an upstream ask,
  not done here.
