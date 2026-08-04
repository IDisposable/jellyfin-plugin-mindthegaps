using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Answers "does the library hold this one gap now?" without building a whole <see cref="OwnershipIndex"/>.
/// Backs the report's verify actions, which run inside the request and so must stay cheap: each check is a
/// focused query rather than the library-wide read a scan does, up to the batch size where the read wins
/// (see <see cref="OwnedAmong(IReadOnlyList{GapItem})"/>).
/// </summary>
public sealed class LibraryVerifier
{
    // A focused check cannot use the provider-id index (the server matches on a computed
    // ProviderId + ":" + ProviderValue), so it walks the items of its kind. Past this many, read them once.
    private const int BatchIndexThreshold = 25;

    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryVerifier"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    public LibraryVerifier(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    /// <summary>
    /// Determines whether the library now holds a real (non-virtual) item answering this gap, by the same
    /// rules a scan uses: any shared provider id, or for an album the artist-and-title name match.
    /// </summary>
    /// <param name="gap">The gap to check.</param>
    /// <returns><see langword="true"/> if the gap has been filled.</returns>
    public bool Owns(GapItem gap)
    {
        ArgumentNullException.ThrowIfNull(gap);

        return Owns(gap.TargetKind, gap.ProviderIds, gap.SourceItemName, gap.Name);
    }

    /// <summary>
    /// Determines whether the library now holds a real (non-virtual) item answering these details, for a
    /// caller that has the parts rather than a <see cref="GapItem"/> (a todo entry, which is a gap the user
    /// copied aside and which must answer this question the same way the report does).
    /// </summary>
    /// <param name="kind">The item kind.</param>
    /// <param name="providerIds">The candidate's provider ids.</param>
    /// <param name="artist">The album artist, for the name fallback. Ignored for other kinds.</param>
    /// <param name="title">The title.</param>
    /// <returns><see langword="true"/> if the library holds it.</returns>
    public bool Owns(BaseItemKind kind, IReadOnlyDictionary<string, string> providerIds, string? artist, string? title)
    {
        ArgumentNullException.ThrowIfNull(providerIds);

        return OwnsByProviderId(kind, providerIds) || OwnsByName(kind, artist, title);
    }

    /// <summary>
    /// Determines which of these gaps the library now holds, in one pass. Same answer as calling
    /// <see cref="Owns(GapItem)"/> on each, but a batch past <see cref="BatchIndexThreshold"/> is decided
    /// against a single read of the owned items instead of a query per gap.
    /// </summary>
    /// <param name="gaps">The gaps to check.</param>
    /// <returns>The subset the library holds, in the order given.</returns>
    public IReadOnlyList<GapItem> OwnedAmong(IReadOnlyList<GapItem> gaps)
    {
        ArgumentNullException.ThrowIfNull(gaps);

        var kinds = new HashSet<BaseItemKind>();
        foreach (var gap in gaps)
        {
            kinds.Add(gap.TargetKind);
        }

        var index = BuildBatchIndex(gaps.Count, kinds);
        var owned = new List<GapItem>();
        foreach (var gap in gaps)
        {
            var has = index is null
                ? Owns(gap)
                : OwnsIn(index, gap.TargetKind, gap.ProviderIds, gap.SourceItemName, gap.Name);
            if (has)
            {
                owned.Add(gap);
            }
        }

        return owned;
    }

    /// <summary>
    /// Determines which of these todo entries the library now holds, in one pass. The bulk form of
    /// <see cref="Owns(BaseItemKind, IReadOnlyDictionary{string, string}, string?, string?)"/>.
    /// </summary>
    /// <param name="entries">The todo entries to check.</param>
    /// <returns>Whether the library holds each, keyed by entry id. An entry whose kind does not parse is
    /// reported as not held, which is what a single check answers for it too.</returns>
    public IReadOnlyDictionary<string, bool> OwnedAmong(IReadOnlyList<TodoEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var kinds = new HashSet<BaseItemKind>();
        foreach (var entry in entries)
        {
            if (Enum.TryParse<BaseItemKind>(entry.TargetKindName, ignoreCase: false, out var kind))
            {
                kinds.Add(kind);
            }
        }

        var index = BuildBatchIndex(entries.Count, kinds);
        var states = new Dictionary<string, bool>(entries.Count, StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!Enum.TryParse<BaseItemKind>(entry.TargetKindName, ignoreCase: false, out var kind))
            {
                states[entry.Id] = false;
                continue;
            }

            states[entry.Id] = index is null
                ? Owns(kind, entry.ProviderIds, entry.Creator, entry.Name)
                : OwnsIn(index, kind, entry.ProviderIds, entry.Creator, entry.Name);
        }

        return states;
    }

    // Null when the batch is small enough that a query per check is cheaper. The album name fallback here
    // folds the title as the scan's index does rather than matching it exactly, so it can only clear rows
    // the next scan would not re-report.
    private OwnershipIndex? BuildBatchIndex(int checks, IReadOnlyCollection<BaseItemKind> kinds)
    {
        if (checks < BatchIndexThreshold || kinds.Count == 0)
        {
            return null;
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in _libraryManager.GetItemList(new InternalItemsQuery
        {
            DtoOptions = LibraryQueryOptions.WithProviderIds(),
            IncludeItemTypes = kinds.ToArray(),
            IsVirtualItem = false,
            Recursive = true
        }))
        {
            var kind = item.GetBaseItemKind();
            foreach (var pair in item.ProviderIds)
            {
                if (!string.IsNullOrEmpty(pair.Value))
                {
                    keys.Add(OwnershipIndex.MakeKey(kind, pair.Key, pair.Value));
                }
            }

            if (item is MusicAlbum album && !string.IsNullOrEmpty(album.Name))
            {
                keys.Add(OwnershipIndex.MakeKey(kind, OwnershipIndex.NameKeyProvider, OwnershipIndex.NameKey(album.AlbumArtist, album.Name)));
            }
        }

        return new OwnershipIndex(keys);
    }

    private static bool OwnsIn(
        OwnershipIndex index,
        BaseItemKind kind,
        IReadOnlyDictionary<string, string> providerIds,
        string? artist,
        string? title)
        => index.OwnsAny(kind, providerIds)
            || (kind == BaseItemKind.MusicAlbum && index.OwnsByName(kind, artist, title));

    private bool OwnsByProviderId(BaseItemKind kind, IReadOnlyDictionary<string, string> providerIds)
    {
        var hasAny = new Dictionary<string, string>(providerIds.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in providerIds)
        {
            if (!string.IsNullOrEmpty(pair.Value))
            {
                hasAny[pair.Key] = pair.Value;
            }
        }

        if (hasAny.Count == 0)
        {
            return false;
        }

        return _libraryManager.GetItemList(new InternalItemsQuery
        {
            DtoOptions = LibraryQueryOptions.Minimal(),
            IncludeItemTypes = new[] { kind },
            IsVirtualItem = false,
            HasAnyProviderId = hasAny,
            Limit = 1,
            Recursive = true
        }).Count > 0;
    }

    // The name fallback, for an album whose provider ids do not overlap the library's (a Discogs release
    // against a MusicBrainz-tagged album). Deliberately album-only, matching what the scan's ownership index
    // name-keys and what GapEngine's carry-forward re-checks: widening it here would clear a row the next
    // scan would only report again. The exact-title query is narrower than the index's fully normalized key,
    // so like OwnsByName it can only fail toward leaving a gap listed, never toward hiding one.
    private bool OwnsByName(BaseItemKind kind, string? artist, string? title)
    {
        if (kind != BaseItemKind.MusicAlbum || string.IsNullOrEmpty(title))
        {
            return false;
        }

        var wanted = OwnershipIndex.NameKey(artist, title);
        foreach (var item in _libraryManager.GetItemList(new InternalItemsQuery
        {
            DtoOptions = LibraryQueryOptions.Minimal(),
            IncludeItemTypes = new[] { BaseItemKind.MusicAlbum },
            IsVirtualItem = false,
            Name = title,
            Recursive = true
        }))
        {
            if (item is MusicAlbum album
                && string.Equals(OwnershipIndex.NameKey(album.AlbumArtist, album.Name), wanted, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
