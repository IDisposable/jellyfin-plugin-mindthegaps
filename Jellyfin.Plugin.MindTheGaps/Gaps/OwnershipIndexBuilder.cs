using System;
using System.Collections.Generic;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Reads the library once and indexes every owned item of the requested kinds by provider id, so a gap
/// source can decide "do I own this?" without a query per candidate. Shared by the full scan (which indexes
/// the union of every enabled source's kinds) and any on-demand, per-request lookup that needs the same
/// answer outside of a scan (a person or item page) without depending on the scan engine itself.
/// </summary>
public sealed class OwnershipIndexBuilder
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<OwnershipIndexBuilder> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OwnershipIndexBuilder"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="logger">The logger.</param>
    public OwnershipIndexBuilder(ILibraryManager libraryManager, ILogger<OwnershipIndexBuilder> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>
    /// Builds an ownership index over every library item of these kinds.
    /// </summary>
    /// <param name="kinds">The item kinds to index. An empty list yields an empty index without a query.</param>
    /// <returns>The index.</returns>
    public OwnershipIndex Build(IReadOnlyList<BaseItemKind> kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var itemCount = 0;

        if (kinds.Count > 0)
        {
            var owned = _libraryManager.GetItemList(new InternalItemsQuery
            {
                DtoOptions = LibraryQueryOptions.WithProviderIds(),
                IncludeItemTypes = [.. kinds],
                Recursive = true
            });
            itemCount = owned.Count;
            foreach (var item in owned)
            {
                var kind = item.GetBaseItemKind();
                foreach (var providerId in item.ProviderIds)
                {
                    if (!string.IsNullOrEmpty(providerId.Value))
                    {
                        keys.Add(OwnershipIndex.MakeKey(kind, providerId.Key, providerId.Value));
                    }
                }

                // Also index an album by its artist-and-title name key, so a source whose ids do not overlap
                // the library's (a Discogs release against a MusicBrainz-tagged album) can still match by name.
                if (item is MusicAlbum album && !string.IsNullOrEmpty(album.Name))
                {
                    keys.Add(OwnershipIndex.MakeKey(kind, OwnershipIndex.NameKeyProvider, OwnershipIndex.NameKey(album.AlbumArtist, album.Name)));
                }
            }
        }

        var ownership = new OwnershipIndex(keys);
        _logger.LogInformation(
            "Ownership index: {Items} owned items, {Keys} provider-id keys, across kinds [{Kinds}].",
            itemCount,
            ownership.Count,
            string.Join(", ", kinds));

        return ownership;
    }
}
