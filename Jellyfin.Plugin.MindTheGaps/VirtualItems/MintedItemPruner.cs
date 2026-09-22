using System;
using System.Collections.Generic;
using System.Diagnostics;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.VirtualItems;

/// <summary>
/// Finds and removes the virtual items <see cref="VirtualItemMinter"/> has minted, whether wholesale (the
/// settings page's undo) or once the library owns the real file for one (the reconciliation that runs
/// after every scan). Split out of <see cref="VirtualItemMinter"/> because it is a complete, self-contained
/// concern: both entry points share the same marker query and the same delete, differing only in which
/// minted items the caller's predicate keeps.
/// </summary>
public sealed class MintedItemPruner
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<MintedItemPruner> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MintedItemPruner"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="logger">The logger.</param>
    public MintedItemPruner(ILibraryManager libraryManager, ILogger<MintedItemPruner> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>
    /// Removes every virtual item this plugin has minted. The cleanup/undo for the experiment.
    /// </summary>
    /// <param name="dryRun">When true, logs what would be removed without deleting anything.</param>
    /// <returns>The number of minted items removed (or, in a dry run, that would be removed).</returns>
    public int RemoveAll(bool dryRun)
    {
        var stopwatch = Stopwatch.StartNew();
        var removed = RemoveMinted(_ => true, dryRun);
        stopwatch.Stop();
        _logger.LogInformation(
            "{Verb} {Count} minted virtual items in {ElapsedMs} ms",
            dryRun ? "Would remove" : "Removed",
            removed,
            stopwatch.ElapsedMilliseconds);
        return removed;
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
        var provider = MintableKind.PrimaryProvider(kind);
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
            DtoOptions = LibraryQueryOptions.WithProviderIds(),
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

    private int RemoveMinted(Func<BaseItem, bool> predicate, bool dryRun)
    {
        var minted = _libraryManager.GetItemList(new InternalItemsQuery
        {
            DtoOptions = LibraryQueryOptions.WithProviderIds(),
            HasAnyProviderId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [VirtualItemMinter.MintedMarker] = "1" },
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
}
