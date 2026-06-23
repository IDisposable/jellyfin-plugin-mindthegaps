using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.VirtualItems;

/// <summary>
/// TEMPORARY. Mints pathless virtual items (a <see cref="Movie"/>, <see cref="Series"/>,
/// <see cref="MusicAlbum"/>, or <see cref="Book"/>) into a container for the missing parts of an owned set,
/// so they render greyed-out the way missing episodes do.
/// <para>
/// This deliberately does, from a plugin, something the server has no supported API for. It exists to
/// prove out the feature and to demonstrate the friction for the upstream proposal
/// (docs/upstream/discussion-mint-virtual-items.md): there is no "create virtual item" API, so it
/// hand-rolls creation; the server does not reconcile or garbage-collect these, so it runs its own
/// reconciliation; and there is no per-user display toggle, so minted items show for everyone. This
/// belongs in core. It is off by default, and everything it creates is tagged with
/// <see cref="MintedMarker"/> and fully removable via <see cref="RemoveAllAsync"/>.
/// </para>
/// <para>
/// Movies and series have a natural home (a BoxSet, or the catch-all collection) and render well. A music
/// album or book is the experimental proof-of-concept: it is still tagged and reversible even when it lands
/// in the catch-all collection and Jellyfin does not display it ideally. That is acceptable for the
/// experiment.
/// </para>
/// </summary>
public sealed class VirtualItemMinter : IDisposable
{
    /// <summary>
    /// Provider-id key stamped on every item this plugin mints, so they can be found and removed.
    /// </summary>
    public const string MintedMarker = "MindTheGapsMinted";

    /// <summary>
    /// Name of the catch-all BoxSet used as a parent for one-off mints that have no owning collection
    /// (filmography, recommendation, book, or unresolved-source gaps), so the virtual item has a valid home
    /// and is removable.
    /// </summary>
    public const string CatchAllCollectionName = "Mind the Gaps (minted)";

    // Upper bound on a single multi-select request so a malformed payload cannot enqueue unbounded work.
    private const int MaxMintSelection = 2000;

    private readonly ILibraryManager _libraryManager;
    private readonly ICollectionManager _collectionManager;
    private readonly IProviderManager _providerManager;
    private readonly IDirectoryService _directoryService;
    private readonly TmdbClient _tmdb;
    private readonly ILogger<VirtualItemMinter> _logger;

    // Serializes find-or-create of the catch-all collection so two concurrent mints (a per-row mint and a
    // multi-select pass, which do not share the MintRunner) cannot both create it and leave duplicates.
    private readonly SemaphoreSlim _catchAllGate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="VirtualItemMinter"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="collectionManager">The collection manager.</param>
    /// <param name="providerManager">The provider manager (queues metadata refreshes).</param>
    /// <param name="directoryService">The directory service (required to build refresh options).</param>
    /// <param name="tmdb">The TMDB client.</param>
    /// <param name="logger">The logger.</param>
    public VirtualItemMinter(
        ILibraryManager libraryManager,
        ICollectionManager collectionManager,
        IProviderManager providerManager,
        IDirectoryService directoryService,
        TmdbClient tmdb,
        ILogger<VirtualItemMinter> logger)
    {
        _libraryManager = libraryManager;
        _collectionManager = collectionManager;
        _providerManager = providerManager;
        _directoryService = directoryService;
        _tmdb = tmdb;
        _logger = logger;
    }

    /// <summary>
    /// Queues a metadata + image refresh for a freshly minted item so providers fill in whatever we
    /// could not write at insert time (overview, artwork, etc.). Fire-and-forget; runs in the host's
    /// refresh queue. Merge mode, so the minted marker and our seeded fields are preserved.
    /// </summary>
    /// <param name="item">The minted item.</param>
    private void QueueMetadataRefresh(BaseItem item)
    {
        try
        {
            _providerManager.QueueRefresh(
                item.Id,
                new MetadataRefreshOptions(_directoryService)
                {
                    MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                    ImageRefreshMode = MetadataRefreshMode.FullRefresh,
                    ReplaceAllMetadata = false
                },
                RefreshPriority.High);
            _logger.LogDebug("Queued metadata refresh for minted item {Id} '{Name}'", item.Id, item.Name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to queue metadata refresh for minted item {Id}", item.Id);
        }
    }

    /// <summary>
    /// Removes every virtual item this plugin has minted. The cleanup/undo for the experiment.
    /// </summary>
    /// <param name="dryRun">When true, logs what would be removed without deleting anything.</param>
    /// <returns>The number of minted items removed (or, in a dry run, that would be removed).</returns>
    public Task<int> RemoveAllAsync(bool dryRun)
    {
        var stopwatch = Stopwatch.StartNew();
        var removed = RemoveMinted(_ => true, dryRun);
        stopwatch.Stop();
        _logger.LogInformation(
            "{Verb} {Count} minted virtual items in {ElapsedMs} ms",
            dryRun ? "Would remove" : "Removed",
            removed,
            stopwatch.ElapsedMilliseconds);
        return Task.FromResult(removed);
    }

    /// <summary>
    /// Removes any minted placeholder whose item the library now owns for real (the reconciliation the
    /// server would do): a real (non-virtual) item of the same kind carrying the same primary provider id.
    /// Run after each scan, since the bulk-mint path that used to reconcile is gone.
    /// </summary>
    /// <returns>The number of minted placeholders reconciled away.</returns>
    public int ReconcileMinted()
    {
        // Build, per kind, the set of primary ids the library now owns a real file for, so a minted
        // placeholder can be matched against the owned item of its own kind and provider.
        var ownedRealByKind = new Dictionary<BaseItemKind, HashSet<string>>();
        var reconciled = RemoveMinted(item => HasOwnedRealCounterpart(item, ownedRealByKind), dryRun: false);
        if (reconciled > 0)
        {
            _logger.LogInformation("Reconciled {Count} minted items the library now owns for real", reconciled);
        }

        return reconciled;
    }

    // True when the library owns a real (non-virtual) item of the minted item's own kind carrying the same
    // primary provider id. The owned-id set per kind is built lazily and cached for the run.
    private bool HasOwnedRealCounterpart(BaseItem mintedItem, Dictionary<BaseItemKind, HashSet<string>> ownedRealByKind)
    {
        var kind = mintedItem.GetBaseItemKind();
        var provider = PrimaryProvider(kind);
        if (provider is null
            || !mintedItem.ProviderIds.TryGetValue(provider, out var id)
            || string.IsNullOrEmpty(id))
        {
            return false;
        }

        if (!ownedRealByKind.TryGetValue(kind, out var owned))
        {
            owned = OwnedRealPrimaryIds(kind, provider);
            ownedRealByKind[kind] = owned;
        }

        return owned.Contains(id);
    }

    // The primary provider ids of the real (non-virtual) items of a kind the library owns, for reconciliation.
    private HashSet<string> OwnedRealPrimaryIds(BaseItemKind kind, string provider)
    {
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { kind },
            IsVirtualItem = false,
            Recursive = true
        }))
        {
            if (item.ProviderIds.TryGetValue(provider, out var id) && !string.IsNullOrEmpty(id))
            {
                owned.Add(id);
            }
        }

        return owned;
    }

    /// <summary>
    /// Temporary debug aid: mints a single gap from the report as the right virtual entity for its kind (a
    /// Movie, Series, MusicAlbum, or Book), seeded from the gap and tagged with the minted marker. A
    /// collection gap (SourceItemType "BoxSet") goes into its BoxSet; a music-album gap whose owning artist
    /// resolves in the library goes under that artist; any other gap goes into the catch-all collection. A
    /// film or show from a filmography attaches the owning person, a book from a bibliography attaches its
    /// author, and an album carries its artist via AlbumArtists, so each surfaces under its creator. Only
    /// Movie, Series, MusicAlbum, and Book gaps are mintable, and only when the gap carries its kind's primary
    /// provider id.
    /// </summary>
    /// <param name="gap">The gap to mint.</param>
    /// <param name="dryRun">When true, logs what would happen without writing anything.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A human-readable status message for the dashboard.</returns>
    public async Task<string> MintGapAsync(GapItem gap, bool dryRun, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        var provider = PrimaryProvider(gap.TargetKind);
        if (provider is null)
        {
            _logger.LogInformation(
                "One-off mint skipped: '{Name}' is a {Kind}, which is not a mintable kind",
                gap.Name,
                gap.TargetKind);
            return string.Create(CultureInfo.InvariantCulture, $"'{gap.Name}' is a {gap.TargetKind}, which cannot be minted.");
        }

        if (!gap.ProviderIds.TryGetValue(provider, out var primaryId) || string.IsNullOrEmpty(primaryId))
        {
            _logger.LogWarning("One-off mint skipped: '{Name}' has no {Provider} id", gap.Name, provider);
            return string.Create(CultureInfo.InvariantCulture, $"'{gap.Name}' has no {provider} id; cannot mint.");
        }

        // A film or show from a filmography attaches the owned person, a book from a bibliography attaches its
        // author, and an album carries its artist through AlbumArtists below. Any other gap attaches nothing.
        var person = PersonAttachment(gap);
        var personSuffix = person.HasValue
            ? string.Create(CultureInfo.InvariantCulture, $" and attach {(person.Value.Kind == PersonKind.Author ? "author" : "person")} '{person.Value.Name}'")
            : string.Empty;

        var entityType = EntityType(gap.TargetKind);
        var itemId = _libraryManager.GetNewItemId(IdKeyPrefix(gap.TargetKind) + primaryId, entityType);
        var alreadyMinted = _libraryManager.GetItemById(itemId) is not null;

        var container = await ResolveContainerAsync(gap, dryRun, cancellationToken).ConfigureAwait(false);
        var containerName = container?.Name ?? CatchAllCollectionName;

        if (dryRun)
        {
            stopwatch.Stop();
            var verb = alreadyMinted ? "re-link existing" : "mint new";
            _logger.LogInformation(
                "DRY RUN one-off: would {Verb} '{Name}' ({Provider} {Id}) into '{Container}'{Person} in ~{Ms} ms",
                verb,
                gap.Name,
                provider,
                primaryId,
                containerName,
                personSuffix,
                stopwatch.ElapsedMilliseconds);
            return string.Create(CultureInfo.InvariantCulture, $"Dry run: would {verb} '{gap.Name}' into '{containerName}'{personSuffix}. Nothing written.");
        }

        if (container is null)
        {
            return "Could not resolve a container to mint into.";
        }

        if (alreadyMinted)
        {
            await AddToContainerAsync(container, itemId).ConfigureAwait(false);
            if (person.HasValue)
            {
                await AttachPersonAsync(itemId, person.Value.Name, person.Value.Kind, cancellationToken).ConfigureAwait(false);
            }

            stopwatch.Stop();
            _logger.LogInformation(
                "One-off: '{Name}' already minted ({ItemId}); ensured linkage in {Ms} ms",
                gap.Name,
                itemId,
                stopwatch.ElapsedMilliseconds);
            return string.Create(CultureInfo.InvariantCulture, $"'{gap.Name}' was already minted; ensured it is linked into '{containerName}'.");
        }

        var item = CreateEntity(gap.TargetKind);
        item.Id = itemId;
        item.Name = gap.Name;
        item.Overview = gap.Overview;
        item.ProductionYear = gap.Year;
        item.PremiereDate = gap.ReleaseDate;
        item.IsVirtualItem = true;
        item.DateCreated = DateTime.UtcNow;
        item.ProviderIds[provider] = primaryId;
        item.ProviderIds[MintedMarker] = "1";

        // A minted album carries its album artist (the discography source's owned artist) so it reads as that
        // artist's work even when it lands in the catch-all collection rather than under the artist.
        if (item is MusicAlbum album && gap.SourceItemName is { Length: > 0 } albumArtist)
        {
            album.AlbumArtists = new[] { albumArtist };
        }

        _libraryManager.CreateItem(item, container);
        await AddToContainerAsync(container, item.Id).ConfigureAwait(false);
        if (person.HasValue)
        {
            await AttachPersonAsync(item.Id, person.Value.Name, person.Value.Kind, cancellationToken).ConfigureAwait(false);
        }

        // Let providers fill in whatever we could not seed at insert time (artwork, overview, ...).
        QueueMetadataRefresh(item);

        stopwatch.Stop();
        _logger.LogInformation(
            "One-off: minted '{Name}' ({Provider} {Id}) as {ItemId} into '{Container}'{Person} in {Ms} ms",
            gap.Name,
            provider,
            primaryId,
            item.Id,
            containerName,
            personSuffix,
            stopwatch.ElapsedMilliseconds);
        return string.Create(CultureInfo.InvariantCulture, $"Minted '{gap.Name}' into '{containerName}'{personSuffix}.");
    }

    /// <summary>
    /// Mints several gaps at once (the report's multi-select), each through <see cref="MintGapAsync"/>.
    /// </summary>
    /// <param name="gaps">The gaps to mint.</param>
    /// <param name="dryRun">When true, logs what would happen without writing anything.</param>
    /// <param name="progress">Optional progress sink (0-100).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A human-readable status message.</returns>
    public async Task<string> MintGapsAsync(IReadOnlyList<GapItem> gaps, bool dryRun, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (gaps is null || gaps.Count == 0)
        {
            return "Nothing selected.";
        }

        if (gaps.Count > MaxMintSelection)
        {
            _logger.LogWarning("Multi-select mint: {Count} gaps requested, capping at {Max}", gaps.Count, MaxMintSelection);
            gaps = gaps.Take(MaxMintSelection).ToList();
        }

        var stopwatch = Stopwatch.StartNew();
        var minted = 0;
        var skipped = 0;
        var failed = 0;
        var index = 0;
        foreach (var gap in gaps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report((double)index++ / gaps.Count * 100);

            // Count anything not mintable (an unmintable kind, or a mintable kind missing its primary id) as
            // skipped rather than minted, so the total reflects actual mint attempts (MintGapAsync no-ops it).
            var provider = PrimaryProvider(gap.TargetKind);
            if (provider is null
                || !gap.ProviderIds.TryGetValue(provider, out var primaryId)
                || string.IsNullOrEmpty(primaryId))
            {
                skipped++;
                continue;
            }

            try
            {
                await MintGapAsync(gap, dryRun, cancellationToken).ConfigureAwait(false);
                minted++;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex, "Multi-select mint: failed on '{Name}'; continuing", gap.Name);
            }
        }

        stopwatch.Stop();
        _logger.LogInformation(
            "Multi-select mint complete{DryRun}: {Minted} of {Total} in {Ms} ms ({Skipped} skipped, {Failed} failed)",
            dryRun ? " (DRY RUN)" : string.Empty,
            minted,
            gaps.Count,
            stopwatch.ElapsedMilliseconds,
            skipped,
            failed);
        var verb = dryRun ? "Would mint" : "Minted";
        var skippedSuffix = skipped > 0 ? string.Create(CultureInfo.InvariantCulture, $", {skipped} skipped (not mintable)") : string.Empty;
        return failed == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{verb} {minted} item(s){skippedSuffix}.")
            : string.Create(CultureInfo.InvariantCulture, $"{verb} {minted} item(s){skippedSuffix}, {failed} failed. Check the server logs.");
    }

    // The provider-id key an item of this kind is minted under (its primary id), or null when the kind
    // cannot be minted. Movies and series key on TMDB, albums on the MusicBrainz release-group, books on
    // OpenLibrary (a plugin-provided key, not a core MetadataProvider enum member).
    private static string? PrimaryProvider(BaseItemKind kind) => kind switch
    {
        BaseItemKind.Movie => MetadataProvider.Tmdb.ToString(),
        BaseItemKind.Series => MetadataProvider.Tmdb.ToString(),
        BaseItemKind.MusicAlbum => MetadataProvider.MusicBrainzReleaseGroup.ToString(),
        BaseItemKind.Book => "OpenLibrary",
        _ => null
    };

    // The runtime entity type for a kind, for GetNewItemId's deterministic id derivation.
    private static Type EntityType(BaseItemKind kind) => kind switch
    {
        BaseItemKind.Movie => typeof(Movie),
        BaseItemKind.Series => typeof(Series),
        BaseItemKind.MusicAlbum => typeof(MusicAlbum),
        BaseItemKind.Book => typeof(Book),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a mintable kind")
    };

    // A fresh entity instance for a kind. Kept separate from EntityType so the typed properties are set on a
    // concrete instance the host can persist.
    private static BaseItem CreateEntity(BaseItemKind kind) => kind switch
    {
        BaseItemKind.Movie => new Movie(),
        BaseItemKind.Series => new Series(),
        BaseItemKind.MusicAlbum => new MusicAlbum(),
        BaseItemKind.Book => new Book(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a mintable kind")
    };

    // The id-key prefix that scopes a minted item's deterministic id by kind, so two kinds that happened to
    // share a primary id value cannot collide.
    private static string IdKeyPrefix(BaseItemKind kind) => kind switch
    {
        BaseItemKind.Movie => "mindthegaps-virtual-movie-",
        BaseItemKind.Series => "mindthegaps-virtual-series-",
        BaseItemKind.MusicAlbum => "mindthegaps-virtual-album-",
        BaseItemKind.Book => "mindthegaps-virtual-book-",
        _ => "mindthegaps-virtual-"
    };

    // A person is attached only for a film/show filmography gap (SourceItemType "Person"); an album or book
    // never gets a person attached.
    // The person to attach to a minted item, with the right role: the owned actor or director for a film or
    // show (the filmography case), or the author for a book (the bibliography case, whose source name is the
    // author). Null when there is no person to attach. A music album carries its artist a different way, via
    // AlbumArtists.
    private static (string Name, PersonKind Kind)? PersonAttachment(GapItem gap)
    {
        if (string.IsNullOrEmpty(gap.SourceItemName))
        {
            return null;
        }

        if (gap.TargetKind is BaseItemKind.Movie or BaseItemKind.Series
            && string.Equals(gap.SourceItemType, "Person", StringComparison.Ordinal))
        {
            return (gap.SourceItemName, PersonKind.Actor);
        }

        if (gap.TargetKind == BaseItemKind.Book
            && string.Equals(gap.SourceItemType, "Book", StringComparison.Ordinal))
        {
            return (gap.SourceItemName, PersonKind.Author);
        }

        return null;
    }

    // Add a minted item to its container. A real collection (a BoxSet) takes the item through the collection
    // manager; a MusicArtist parent already holds the item from CreateItem, so there is nothing to add.
    private async Task AddToContainerAsync(BaseItem container, Guid itemId)
    {
        if (container is MusicArtist)
        {
            return;
        }

        await _collectionManager.AddToCollectionAsync(container.Id, new[] { itemId }).ConfigureAwait(false);
    }

    private async Task<BaseItem?> ResolveContainerAsync(GapItem gap, bool dryRun, CancellationToken cancellationToken)
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
                    IncludeItemTypes = new[] { BaseItemKind.BoxSet },
                    Recursive = true
                })
                .FirstOrDefault(b => string.Equals(b.Name, CatchAllCollectionName, StringComparison.Ordinal));

            if (catchAll is not null)
            {
                return catchAll;
            }

            if (dryRun)
            {
                return null;
            }

            _logger.LogInformation("Creating catch-all collection '{Name}' for one-off mints", CatchAllCollectionName);
            return await _collectionManager
                .CreateCollectionAsync(new CollectionCreationOptions { Name = CatchAllCollectionName })
                .ConfigureAwait(false);
        }
        finally
        {
            _catchAllGate.Release();
        }
    }

    private async Task AttachPersonAsync(Guid itemId, string personName, PersonKind kind, CancellationToken cancellationToken)
    {
        if (_libraryManager.GetItemById(itemId) is not { } item)
        {
            return;
        }

        await _libraryManager.UpdatePeopleAsync(
            item,
            new[] { new PersonInfo { Name = personName, Type = kind } },
            cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Attached {Kind} '{Person}' to minted item {ItemId}", kind, personName, itemId);
    }

    private int RemoveMinted(Func<BaseItem, bool> predicate, bool dryRun)
    {
        var minted = _libraryManager.GetItemList(new InternalItemsQuery
        {
            HasAnyProviderId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [MintedMarker] = "1" },
            Recursive = true
        });
        _logger.LogDebug("Found {Count} items carrying the minted marker", minted.Count);

        var removed = 0;
        foreach (var item in minted)
        {
            if (!predicate(item))
            {
                continue;
            }

            _logger.LogDebug(
                "{Action} minted virtual item '{Name}' ({Id}, {Kind}). File deletion is disabled",
                dryRun ? "Would remove" : "Removing",
                item.Name,
                item.Id,
                item.GetBaseItemKind());

            if (!dryRun)
            {
                // DeleteFileLocation=false is the critical safety: we only ever drop the library entry we
                // created, never anything on disk. The marker query already scopes this to our own items.
                _libraryManager.DeleteItem(
                    item,
                    new DeleteOptions { DeleteFileLocation = false, DeleteFromExternalProvider = false },
                    notifyParentItem: true);
            }

            removed++;
        }

        return removed;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _catchAllGate.Dispose();
    }
}
