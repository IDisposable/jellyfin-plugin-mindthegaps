# Architecture decision records

Short records of the decisions that shaped Mind the Gaps, newest concerns last. Each one captures the
context, the decision, and what it costs, so the reasoning survives the code.

| # | Decision |
|---|---|
| [0001](0001-domain-agnostic-gap-model.md) | Gaps are 3 patterns across N media domains |
| [0002](0002-gap-sources-live-in-the-plugin.md) | Gap sources live in the plugin (no plugin-to-plugin SPI) |
| [0003](0003-self-contained-tmdb-client.md) | Self-contained TMDB client (no MediaBrowser.Providers) |
| [0004](0004-experimental-virtual-item-minting.md) | Virtual-item minting is experimental, opt-in, reversible |
| [0005](0005-name-and-terminology.md) | Name "Mind the Gaps"; Shows vs Live TV terminology |
| [0006](0006-captured-data-testing.md) | Test parsers against real captured API responses |
| [0007](0007-library-aligned-media-domains.md) | Library-aligned media domains (and why not CollectionType) |
| [0008](0008-stable-gap-ids.md) | Gap ids are stable, derived from durable keys, and a persistence contract |
