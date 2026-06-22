# Spike: reusing an installed plugin's provider key

## Question

A user who already runs the in-box TheTVDB metadata plugin has entered a TheTVDB subscriber key there. Can
Mind the Gaps reuse that key for its TheTVDB cross-check instead of asking for it a second time?

## Finding

There is no supported host API for one plugin to read another plugin's secrets. A plugin's configuration is
private to it; the host exposes no "give me plugin X's config" surface. The only available path is
reflection: enumerate `IPluginManager.Plugins`, match the target plugin by assembly name, read its
`Configuration` property, and read the key off that config object by property name. This is a soft dependency
on another plugin's internals and is fragile: a renamed property, a changed type, or a disabled plugin all
break it.

## What was built

`Services/InstalledPluginCredentials` does exactly that, guarded so it can never break a scan:

- It targets the TheTVDB plugin only (assembly `Jellyfin.Plugin.Tvdb`) and tries a short list of key
  property names (`SubscriberPIN`, `SubscriberPin`, `ApiKey`) in order.
- Every reflection step is wrapped in `try`/`catch`; any failure (plugin absent, disabled, property renamed,
  type mismatch) logs at debug and returns `null`. It never throws.
- It is gated behind an opt-in toggle, `ReuseInstalledProviderKeys`, off by default.
- `TvdbContentGapSource.ResolveApiKey` prefers the user's own configured key and only falls back to a
  discovered key when ours is blank and the toggle is on. A discovery miss falls back to empty, so the source
  simply reports itself disabled rather than erroring.

## Scope and non-goals

- **Trakt is intentionally not wired.** Trakt needs an OAuth client id, not a drop-in key, and its semantics
  are murkier; it is not worth broadening this fragile surface for.
- **TMDB does not need it.** The plugin already uses the public default key or the user's own; the durable
  fix is upstream (let the host expose its TMDB client/key, discussion C), not more reflection.

## Verdict

Usable as a convenience, off by default, TheTVDB only. It stays a workaround until a supported host SPI for
credentialed providers exists (a "providers expose a credentialed client" surface, the way
`IExternalUrlProvider` exposes links). Keep the reflection guarded and the toggle off by default.
