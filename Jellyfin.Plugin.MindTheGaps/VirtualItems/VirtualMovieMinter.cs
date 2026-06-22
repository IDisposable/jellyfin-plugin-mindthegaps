using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.VirtualItems;

/// <summary>
/// TEMPORARY. Mints pathless virtual <see cref="Movie"/> items into BoxSets for the
/// missing parts of an owned TMDB collection, so they render greyed-out the way missing episodes do.
/// <para>
/// This deliberately does, from a plugin, something the server has no supported API for. It exists to
/// prove out the feature and to demonstrate the friction for the upstream proposal
/// (docs/upstream/discussion-mint-virtual-items.md): there is no "create virtual item" API, so it
/// hand-rolls creation; the server does not reconcile or garbage-collect these, so it runs its own
/// reconciliation; and there is no per-user display toggle, so minted movies show for everyone. This
/// belongs in core. It is off by default, and everything it creates is tagged with
/// <see cref="MintedMarker"/> and fully removable via <see cref="RemoveAllAsync"/>.
/// </para>
/// </summary>
public sealed class VirtualMovieMinter : IDisposable
{
    /// <summary>
    /// Provider-id key stamped on every item this plugin mints, so they can be found and removed.
    /// </summary>
    public const string MintedMarker = "MindTheGapsMinted";

    /// <summary>
    /// Name of the catch-all BoxSet used as a parent for one-off mints that have no owning collection
    /// (filmography or recommendation gaps), so the virtual movie has a valid home and is removable.
    /// </summary>
    public const string CatchAllCollectionName = "Mind the Gaps (minted)";

    // Upper bound on a single multi-select request so a malformed payload cannot enqueue unbounded work.
    private const int MaxMintSelection = 2000;

    private readonly ILibraryManager _libraryManager;
    private readonly ICollectionManager _collectionManager;
    private readonly IProviderManager _providerManager;
    private readonly IDirectoryService _directoryService;
    private readonly TmdbClient _tmdb;
    private readonly ILogger<VirtualMovieMinter> _logger;

    // Serializes find-or-create of the catch-all collection so two concurrent mints (a per-row mint and a
    // multi-select pass, which do not share the MintRunner) cannot both create it and leave duplicates.
    private readonly SemaphoreSlim _catchAllGate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="VirtualMovieMinter"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="collectionManager">The collection manager.</param>
    /// <param name="providerManager">The provider manager (queues metadata refreshes).</param>
    /// <param name="directoryService">The directory service (required to build refresh options).</param>
    /// <param name="tmdb">The TMDB client.</param>
    /// <param name="logger">The logger.</param>
    public VirtualMovieMinter(
        ILibraryManager libraryManager,
        ICollectionManager collectionManager,
        IProviderManager providerManager,
        IDirectoryService directoryService,
        TmdbClient tmdb,
        ILogger<VirtualMovieMinter> logger)
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
    /// Removes every virtual movie this plugin has minted. The cleanup/undo for the experiment.
    /// </summary>
    /// <param name="dryRun">When true, logs what would be removed without deleting anything.</param>
    /// <returns>The number of minted movies removed (or, in a dry run, that would be removed).</returns>
    public Task<int> RemoveAllAsync(bool dryRun)
    {
        var stopwatch = Stopwatch.StartNew();
        var removed = RemoveMinted(_ => true, dryRun);
        stopwatch.Stop();
        _logger.LogInformation(
            "{Verb} {Count} minted virtual movies in {ElapsedMs} ms",
            dryRun ? "Would remove" : "Removed",
            removed,
            stopwatch.ElapsedMilliseconds);
        return Task.FromResult(removed);
    }

    /// <summary>
    /// Removes any minted placeholder whose movie the library now owns for real (the reconciliation the
    /// server would do). Run after each scan, since the bulk-mint path that used to reconcile is gone.
    /// </summary>
    /// <returns>The number of minted placeholders reconciled away.</returns>
    public int ReconcileMinted()
    {
        var ownedRealTmdbIds = OwnedRealMovieTmdbIds();
        var reconciled = RemoveMinted(item => HasOwnedRealCounterpart(item, ownedRealTmdbIds), dryRun: false);
        if (reconciled > 0)
        {
            _logger.LogInformation("Reconciled {Count} minted movies the library now owns for real", reconciled);
        }

        return reconciled;
    }

    private static bool HasOwnedRealCounterpart(BaseItem mintedItem, HashSet<string> ownedRealTmdbIds)
        => mintedItem.TryGetProviderId(MetadataProvider.Tmdb, out var id)
            && !string.IsNullOrEmpty(id)
            && ownedRealTmdbIds.Contains(id);

    private HashSet<string> OwnedRealMovieTmdbIds()
    {
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie },
            IsVirtualItem = false,
            Recursive = true
        }))
        {
            if (item.TryGetProviderId(MetadataProvider.Tmdb, out var id) && !string.IsNullOrEmpty(id))
            {
                owned.Add(id);
            }
        }

        return owned;
    }

    /// <summary>
    /// Temporary debug aid: mints a single gap from the report. A collection gap (SourceItemType
    /// "BoxSet") goes into its BoxSet; any other gap (filmography, recommendation) goes into the
    /// catch-all collection, and a filmography gap (SourceItemType "Person") additionally attaches the
    /// owning person so the movie surfaces on that person's page. Only Movie gaps are mintable.
    /// </summary>
    /// <param name="gap">The gap to mint.</param>
    /// <param name="dryRun">When true, logs what would happen without writing anything.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A human-readable status message for the dashboard.</returns>
    public async Task<string> MintGapAsync(GapItem gap, bool dryRun, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        if (gap.TargetKind != BaseItemKind.Movie)
        {
            _logger.LogInformation(
                "One-off mint skipped: '{Name}' is a {Kind}; only Movie gaps are mintable today",
                gap.Name,
                gap.TargetKind);
            return string.Create(CultureInfo.InvariantCulture, $"Only Movie gaps can be minted today; '{gap.Name}' is a {gap.TargetKind}.");
        }

        if (!gap.ProviderIds.TryGetValue("Tmdb", out var tmdbId) || string.IsNullOrEmpty(tmdbId))
        {
            _logger.LogWarning("One-off mint skipped: '{Name}' has no TMDB id", gap.Name);
            return string.Create(CultureInfo.InvariantCulture, $"'{gap.Name}' has no TMDB id; cannot mint.");
        }

        var personName = string.Equals(gap.SourceItemType, "Person", StringComparison.Ordinal) ? gap.SourceItemName : null;
        var personSuffix = string.IsNullOrEmpty(personName)
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $" and attach person '{personName}'");

        var movieId = _libraryManager.GetNewItemId("mindthegaps-virtual-movie-" + tmdbId, typeof(Movie));
        var alreadyMinted = _libraryManager.GetItemById(movieId) is not null;

        var container = await ResolveContainerAsync(gap, dryRun, cancellationToken).ConfigureAwait(false);
        var containerName = container?.Name ?? CatchAllCollectionName;

        if (dryRun)
        {
            stopwatch.Stop();
            var verb = alreadyMinted ? "re-link existing" : "mint new";
            _logger.LogInformation(
                "DRY RUN one-off: would {Verb} '{Name}' (TMDB {Tmdb}) into '{Container}'{Person} in ~{Ms} ms",
                verb,
                gap.Name,
                tmdbId,
                containerName,
                personSuffix,
                stopwatch.ElapsedMilliseconds);
            return string.Create(CultureInfo.InvariantCulture, $"Dry run: would {verb} '{gap.Name}' into '{containerName}'{personSuffix}. Nothing written.");
        }

        if (container is null)
        {
            return "Could not resolve a collection to mint into.";
        }

        if (alreadyMinted)
        {
            await _collectionManager.AddToCollectionAsync(container.Id, new[] { movieId }).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(personName))
            {
                await AttachPersonAsync(movieId, personName, cancellationToken).ConfigureAwait(false);
            }

            stopwatch.Stop();
            _logger.LogInformation(
                "One-off: '{Name}' already minted ({MovieId}); ensured linkage in {Ms} ms",
                gap.Name,
                movieId,
                stopwatch.ElapsedMilliseconds);
            return string.Create(CultureInfo.InvariantCulture, $"'{gap.Name}' was already minted; ensured it is linked into '{containerName}'.");
        }

        var movie = new Movie
        {
            Id = movieId,
            Name = gap.Name,
            Overview = gap.Overview,
            ProductionYear = gap.Year,
            PremiereDate = gap.ReleaseDate,
            IsVirtualItem = true,
            DateCreated = DateTime.UtcNow
        };
        movie.ProviderIds[MetadataProvider.Tmdb.ToString()] = tmdbId;
        movie.ProviderIds[MintedMarker] = "1";

        _libraryManager.CreateItem(movie, container);
        await _collectionManager.AddToCollectionAsync(container.Id, new[] { movie.Id }).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(personName))
        {
            await AttachPersonAsync(movie.Id, personName, cancellationToken).ConfigureAwait(false);
        }

        // Let providers fill in whatever we could not seed at insert time (artwork, overview, ...).
        QueueMetadataRefresh(movie);

        stopwatch.Stop();
        _logger.LogInformation(
            "One-off: minted '{Name}' (TMDB {Tmdb}) as {MovieId} into '{Container}'{Person} in {Ms} ms",
            gap.Name,
            tmdbId,
            movie.Id,
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

            // The report only offers Mint on movie gaps with a TMDB id; count anything else as skipped
            // rather than minted, so the total reflects actual mint attempts (MintGapAsync would no-op it).
            if (gap.TargetKind != BaseItemKind.Movie
                || !gap.ProviderIds.TryGetValue("Tmdb", out var tmdbId)
                || string.IsNullOrEmpty(tmdbId))
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

    private async Task<BaseItem?> ResolveContainerAsync(GapItem gap, bool dryRun, CancellationToken cancellationToken)
    {
        if (string.Equals(gap.SourceItemType, "BoxSet", StringComparison.Ordinal)
            && Guid.TryParse(gap.SourceItemId, out var boxSetId)
            && _libraryManager.GetItemById(boxSetId) is BoxSet ownerBoxSet)
        {
            return ownerBoxSet;
        }

        // No owning BoxSet (filmography or recommendation gap, or the owner is gone): use a single
        // catch-all collection so the virtual movie has a valid parent and a place to be found/removed.
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

    private async Task AttachPersonAsync(Guid movieId, string personName, CancellationToken cancellationToken)
    {
        if (_libraryManager.GetItemById(movieId) is not { } movie)
        {
            return;
        }

        await _libraryManager.UpdatePeopleAsync(
            movie,
            new[] { new PersonInfo { Name = personName, Type = PersonKind.Actor } },
            cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Attached person '{Person}' to minted movie {MovieId}", personName, movieId);
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

            item.TryGetProviderId(MetadataProvider.Tmdb, out var tmdbId);
            _logger.LogDebug(
                "{Action} minted virtual movie '{Name}' ({Id}, TMDB {Tmdb}). File deletion is disabled",
                dryRun ? "Would remove" : "Removing",
                item.Name,
                item.Id,
                tmdbId);

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
