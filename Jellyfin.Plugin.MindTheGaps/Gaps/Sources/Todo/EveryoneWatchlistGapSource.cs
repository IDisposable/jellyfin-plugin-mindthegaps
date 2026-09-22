using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Todo;

/// <summary>
/// Discovery source over the union of every user's TODO list (the same rows the Maintenance section's
/// Fulfillment queue shows), treated as a household watchlist: still-wanted titles the library does not
/// hold, folded to one per title. Needs no account or credential, since it only reads data the plugin
/// already keeps.
/// </summary>
/// <remarks>
/// Unlike an external watchlist, this can never fail transiently (no network call) and fully re-derives its
/// scope from the current TODO lists every run, so <see cref="StillInScope"/> checks real membership rather
/// than only whether the feature is turned on: a title someone removed from their list (or marked fetched)
/// must stop being carried forward by <see cref="GapEngine"/>'s Recommendation-pattern backfill.
/// </remarks>
internal sealed class EveryoneWatchlistGapSource : IGapSource, IDiscoverSource, IConfiguredScopeSource
{
    private readonly TodoStore _todo;
    private readonly TodoOwner _owner;

    /// <summary>
    /// Initializes a new instance of the <see cref="EveryoneWatchlistGapSource"/> class.
    /// </summary>
    /// <param name="todo">The per-user todo-list store.</param>
    /// <param name="owner">Resolves whether a stored owner id is still a real user.</param>
    public EveryoneWatchlistGapSource(TodoStore todo, TodoOwner owner)
    {
        _todo = todo;
        _owner = owner;
    }

    /// <inheritdoc />
    public string Name => "Everyone's watchlist";

    /// <inheritdoc />
    public string DiscoverKind => SourceItemTypes.EveryoneWatchlist;

    /// <inheritdoc />
    public IReadOnlyCollection<BaseItemKind> OwnedKinds { get; } =
        new[] { BaseItemKind.Movie, BaseItemKind.Series, BaseItemKind.MusicAlbum, BaseItemKind.Book };

    /// <inheritdoc />
    public string GapIdPrefix => GapSourceKeys.EveryoneWatchlist.GapPrefix;

    /// <inheritdoc />
    public bool IsEnabled(PluginConfiguration config) => config.ScanEveryoneWatchlist;

    /// <inheritdoc />
    public bool StillInScope(GapItem item, PluginConfiguration config)
        => IsEnabled(config) && CurrentIds().Contains(item.Id);

    /// <inheritdoc />
    public async IAsyncEnumerable<GapItem> FindGapsAsync(
        GapScanContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        // No genuine I/O (TodoStore is a local, in-memory-cached read), so there is nothing to await; this
        // yield is only what makes an async iterator, matching the IAsyncEnumerable contract every source
        // shares.
        await Task.Yield();

        var rows = TodoDemandAggregator.Build(LoadEveryone());
        foreach (var gap in EveryoneWatchlistMapper.Build(rows, context.Ownership))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return gap;
        }

        context.ReportProgress(1);
    }

    // Every existing owner's current entries, tagged with that owner. No "include the caller even when
    // empty" rule here (unlike Api/TodoController's equivalent flatten): a scan has no caller, only owners.
    private List<OwnedTodoEntry> LoadEveryone()
    {
        var items = new List<OwnedTodoEntry>();
        foreach (var id in _todo.ListOwners())
        {
            if (!_owner.Exists(id))
            {
                continue;
            }

            var name = _owner.NameOf(id);
            items.AddRange(_todo.Load(id).Select(entry => OwnedTodoEntry.From(entry, id, name)));
        }

        return items;
    }

    // The gap ids this source would produce right now, ownership aside (StillInScope only ever needs to
    // rule a gap OUT for being no longer wanted, not back in for being owned; RunAsync's own fresh pass
    // already handles the owned case for whatever it re-emits this run).
    private HashSet<string> CurrentIds()
        => TodoDemandAggregator.Build(LoadEveryone())
            .Where(row => row.OpenCount > 0)
            .Select(row => GapSourceKeys.EveryoneWatchlist.Gap(row.Id))
            .ToHashSet(StringComparer.Ordinal);
}
