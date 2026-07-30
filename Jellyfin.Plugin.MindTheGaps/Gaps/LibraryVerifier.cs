using System;
using System.Collections.Generic;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Answers "does the library hold this one gap now?" without building a whole <see cref="OwnershipIndex"/>.
/// Backs the report's verify actions, which run inside the request and so must stay cheap: each check is a
/// focused, provider-id-indexed query rather than the library-wide read a scan does.
/// </summary>
public sealed class LibraryVerifier
{
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
