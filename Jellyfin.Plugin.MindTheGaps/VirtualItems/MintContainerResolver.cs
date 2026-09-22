using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.VirtualItems;

/// <summary>
/// Finds (or, for the catch-all, creates) the container a gap's minted item should land in: a collection
/// gap's own BoxSet, a music-album gap's owned artist, or <see cref="VirtualItemMinter.CatchAllCollectionName"/>
/// for everything else. Split out of <see cref="VirtualItemMinter"/> because it is a complete, self-contained
/// concern with its own concurrency invariant (the catch-all's find-or-create has to be serialized so a
/// per-row mint and a multi-select pass cannot both create it and leave duplicates).
/// </summary>
public sealed class MintContainerResolver : IDisposable
{
    private readonly ILibraryManager _libraryManager;
    private readonly ICollectionManager _collectionManager;
    private readonly ILogger<MintContainerResolver> _logger;

    // Serializes find-or-create of the catch-all collection so two concurrent mints (a per-row mint and a
    // multi-select pass, which do not share the MintRunner) cannot both create it and leave duplicates.
    private readonly SemaphoreSlim _catchAllGate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="MintContainerResolver"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="collectionManager">The collection manager.</param>
    /// <param name="logger">The logger.</param>
    public MintContainerResolver(ILibraryManager libraryManager, ICollectionManager collectionManager, ILogger<MintContainerResolver> logger)
    {
        _libraryManager = libraryManager;
        _collectionManager = collectionManager;
        _logger = logger;
    }

    /// <summary>
    /// Resolves the container a gap's minted item should land in, creating the catch-all collection on
    /// first use if needed.
    /// </summary>
    /// <param name="gap">The gap being minted.</param>
    /// <param name="dryRun">When true, never creates the catch-all collection.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The container, or null when a dry run would need to create the catch-all collection.</returns>
    public async Task<BaseItem?> ResolveContainerAsync(GapItem gap, bool dryRun, CancellationToken cancellationToken)
    {
        // A collection gap (a movie or a series in a TMDB collection) goes into its owning BoxSet.
        if (string.Equals(gap.SourceItemType, "BoxSet", StringComparison.Ordinal)
            && Guid.TryParse(gap.SourceItemId, out var boxSetId)
            && _libraryManager.GetItemById(boxSetId) is BoxSet ownerBoxSet)
        {
            return ownerBoxSet;
        }

        // A music-album gap carries the owning artist's library id (the discography source sets SourceItemId
        // to the artist's guid and SourceItemType to "MusicArtist"). When that resolves to a MusicArtist in
        // the library, mint the album as a child of the artist so it lands under their discography.
        if (gap.TargetKind == BaseItemKind.MusicAlbum
            && Guid.TryParse(gap.SourceItemId, out var artistId)
            && _libraryManager.GetItemById(artistId) is MusicArtist ownerArtist)
        {
            return ownerArtist;
        }

        // No owning container (filmography, recommendation, book, or the owner is gone): use a single
        // catch-all collection so the virtual item has a valid parent and a place to be found/removed.
        // Hold the gate across the lookup and the create so concurrent mints share one collection.
        await _catchAllGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var catchAll = _libraryManager
                .GetItemList(new InternalItemsQuery
                {
                    DtoOptions = LibraryQueryOptions.Minimal(),
                    IncludeItemTypes = new[] { BaseItemKind.BoxSet },
                    Recursive = true
                })
                .FirstOrDefault(b => string.Equals(b.Name, VirtualItemMinter.CatchAllCollectionName, StringComparison.Ordinal));

            if (catchAll is not null)
            {
                return catchAll;
            }

            if (dryRun)
            {
                return null;
            }

            _logger.LogInformation("Creating catch-all collection '{Name}' for one-off mints", VirtualItemMinter.CatchAllCollectionName);
            return await _collectionManager
                .CreateCollectionAsync(new CollectionCreationOptions { Name = VirtualItemMinter.CatchAllCollectionName })
                .ConfigureAwait(false);
        }
        finally
        {
            _catchAllGate.Release();
        }
    }

    /// <summary>
    /// Adds a minted item to its resolved container. A real collection (a BoxSet) takes the item through
    /// the collection manager; a MusicArtist parent already holds the item from <c>CreateItem</c>, so there
    /// is nothing to add.
    /// </summary>
    /// <param name="container">The container the item resolved into.</param>
    /// <param name="itemId">The minted item's id.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task AddToContainerAsync(BaseItem container, Guid itemId)
    {
        if (container is MusicArtist)
        {
            return;
        }

        await _collectionManager.AddToCollectionAsync(container.Id, new[] { itemId }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _catchAllGate.Dispose();
    }
}
