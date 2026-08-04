# PR D (server): give `Episode.IndexNumberEnd` a database column

Work order for the server repo. Everything below was verified against `release-10.11.z` at tag
`v10.11.11`. Line numbers are anchors for finding the code, not a promise; target `master` and locate by
the quoted snippets.

## What is wrong

`Episode.IndexNumberEnd` (`MediaBrowser.Controller/Entities/TV/Episode.cs:49`) is the ending episode number
of a multi-episode file (`S01E01-E02`). It has no column on `BaseItemEntity` and no line in either direction
of `BaseItemRepository.Map`. It survives today only because it is an ordinary settable property with no
`[JsonIgnore]`, so it rides inside the `Data` JSON blob and comes back through
`JsonSerializer.Deserialize`.

That is not data loss, and it is not an EF regression: the pre-EF `SqliteItemRepository` in 10.10 had no
such column either. It costs two things:

1. Any read with `InternalItemsQuery.SkipDeserialization = true` returns an `Episode` whose
   `IndexNumberEnd` is null, silently. The only current core caller is the `FixAudioData` migration routine
   (audio only), so nothing in core is broken right now, but the trap is armed for anyone who reaches for
   that flag on an episode query.
2. Nothing can filter or sort on it in SQL. "Does an owned episode cover number N" is
   `Episode.ContainsEpisodeNumber`, an in-memory check, so every caller has to materialize the episodes
   first. `TVSeriesManager.cs:177` does exactly this for next-up.

## Scope

Add the column, map it both ways, backfill existing rows. No API or DTO change:
`BaseItemDto.IndexNumberEnd` already exists and `DtoService.cs:1197` already populates it.

## Changes

### 1. Entity

`src/Jellyfin.Database/Jellyfin.Database.Implementations/Entities/BaseItemEntity.cs:31`, beside
`IndexNumber`:

```csharp
public int? IndexNumberEnd { get; set; }
```

The entity is flat across kinds and already carries Episode-only columns (`SeasonId`, `SeasonName`,
`EpisodeTitle`), so this is in keeping. No index: nothing queries it yet.

### 2. Read mapping

`Jellyfin.Server.Implementations/Item/BaseItemRepository.cs:950`, the existing block in
`public static BaseItemDto Map(BaseItemEntity entity, BaseItemDto dto, IServerApplicationHost? appHost, ILogger logger)`:

```csharp
if (dto is Episode episode)
{
    episode.SeasonName = entity.SeasonName;
    episode.SeasonId = entity.SeasonId.GetValueOrDefault();
    episode.IndexNumberEnd = entity.IndexNumberEnd;   // add
}
```

**Read the trap section before writing this line.**

### 3. Write mapping

Same file, line 1115, the mirroring block in `public BaseItemEntity Map(BaseItemDto dto)`:

```csharp
if (dto is Episode episode)
{
    entity.SeasonName = episode.SeasonName;
    entity.SeasonId = episode.SeasonId;
    entity.IndexNumberEnd = episode.IndexNumberEnd;   // add
}
```

### 4. Migration

From the repo root, per `src/Jellyfin.Database/readme.md`:

```
dotnet ef migrations add AddIndexNumberEnd --project "src/Jellyfin.Database/Jellyfin.Database.Providers.Sqlite" -- --migration-provider Jellyfin-SQLite
```

SQLite is the only provider that ships in this line, so it is the only one to generate. Commit the new
`Migrations/*.cs`, its `.Designer.cs`, and the regenerated `JellyfinDbModelSnapshot.cs`.
`tests/Jellyfin.Server.Implementations.Tests/EfMigrations/EfMigrationTests.cs` asserts
`HasPendingModelChanges() == false`, so a missing or stale snapshot fails CI rather than shipping.

### 5. Backfill (not optional, see below)

`Jellyfin.Server/Migrations/Routines/` holds the data migrations, and `FixAudioData.cs` is the closest
precedent: page `GetItemList` with `SkipDeserialization = false`, set nothing, `SaveItems`, which rewrites
each row through the new write mapping. Scope it to `BaseItemKind.Episode` and page it (`FixAudioData` uses
5000). Do not copy its `SkipDeserialization = true`: this backfill needs the blob, since the blob is the
only place the value currently exists.

A SQLite-only alternative is one `UPDATE ... SET IndexNumberEnd = json_extract(Data, '$.IndexNumberEnd')`,
which is far faster but goes around the provider abstraction. Worth asking the maintainers which they want
before writing the slow one.

## The trap

`Map` runs **after** deserialization and assigns entity fields over the already-populated DTO. So the moment
step 2 lands, every existing episode row reads back with `IndexNumberEnd = null`, because the new column is
null for every row written before the migration, and that null now overwrites the correct value that came
out of the blob. Two-part episodes lose their end number library-wide until each is re-saved.

So the ordering matters and the backfill is part of the change, not a follow-up:

- schema migration adds the nullable column,
- the startup routine populates it from the existing blobs,
- only then is the read mapping authoritative.

If you want belt and braces during the transition, guard the read (`if (entity.IndexNumberEnd.HasValue)`),
but a correct backfill makes that unnecessary and it will linger forever if you add it. Call this out in the
PR description either way; it is the one thing a reviewer must check.

## Tests

- `EfMigrationTests` already covers the snapshot, no change needed.
- Add a mapping test under `tests/Jellyfin.Server.Implementations.Tests/Item/` next to `OrderMapperTests.cs`.
  The read direction is `public static`, so it unit-tests directly: build a `BaseItemEntity` with
  `Type = "MediaBrowser.Controller.Entities.TV.Episode"` and `IndexNumberEnd = 2`, map onto a new `Episode`,
  assert it arrives. The write direction is an instance method on the repository and needs its constructor
  dependencies, so cover it through the round trip or leave it.
- Manual: a file named `S01E01-E02`, then
  `sqlite3 jellyfin.db "select Name, IndexNumber, IndexNumberEnd from BaseItems where IndexNumberEnd is not null limit 5"`.
  Check both a newly scanned episode and an upgraded (backfilled) one.

## Do not

- Do not move the property to `BaseItem` or `Video`. It is Episode-only and every consumer treats it so.
- Do not change what is serialized into `Data`. The blob keeps carrying it, which is what makes the change
  backward compatible and a downgrade survivable.
- Do not touch the same-named properties on `BaseItemDto`, `RemoteSearchResult`, `EpisodeInfo`, or
  `SubtitleSearchRequest`; they are unrelated carriers of the same number.

## Consumers, for the PR description

`grep -rn "IndexNumberEnd" --include=*.cs`:
`TVSeriesManager.cs:177`, `DtoService.cs:1197`, `LibraryManager.cs:2741-2789`, `Episode.ContainsEpisodeNumber`,
`EpisodeMetadataService.cs:108`, the XbmcMetadata episode parser and saver, the OMDb and TMDB episode
providers, and the subtitle search path.

## Why this repo cares

Mind the Gaps reconciles a series' owned episodes against a provider's canonical list, and a multi-episode
file has to count for every number in its span or the second half of every two-parter reads as missing. That
is `SeriesContentDiff`/`OwnedEpisodes` here, and it is the reason the plugin cannot use `SkipDeserialization`
on its episode reads even though it wants only columns. The plugin works fine without this change; it just
pays for the JSON parse on every episode read to get one integer.
