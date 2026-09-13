using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Acquisition;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// The want-to-watch list as the web UI sees it: the plugin's todo list, which already survives scans,
/// checks itself against the library, and exports. This adds a card view of it, a per-card "is it on the
/// list" mark, and a way to rebuild a gap from an entry so the detail dialog and Send work on it.
/// </summary>
public sealed class WantToWatchService
{
    private readonly TodoStore _todo;

    /// <summary>
    /// Initializes a new instance of the <see cref="WantToWatchService"/> class.
    /// </summary>
    /// <param name="todo">The todo store.</param>
    public WantToWatchService(TodoStore todo)
    {
        _todo = todo;
    }

    /// <summary>
    /// The row: every entry not yet done, most recently added first.
    /// </summary>
    /// <param name="isAdministrator">Whether the caller is an administrator, which gates the Send buttons.</param>
    /// <returns>The row.</returns>
    public HomeDiscoverResult Get(bool isAdministrator)
    {
        var config = Plugin.RequireConfiguration();
        return new HomeDiscoverResult
        {
            CanSendMovies = isAdministrator && AcquisitionService.RadarrConfigured(config),
            CanSendSeries = isAdministrator && AcquisitionService.SonarrConfigured(config),
            Titles = Cards(_todo.Load())
        };
    }

    /// <summary>
    /// The ids on the list, for marking cards on the other surfaces.
    /// </summary>
    /// <returns>The ids of every entry not yet done.</returns>
    public HashSet<string> WantedIds()
        => _todo.Load().Where(e => !e.Done).Select(e => e.Id).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Marks the cards that are on the list.
    /// </summary>
    /// <param name="titles">The cards.</param>
    public void Mark(IEnumerable<MissingTitle> titles)
    {
        ArgumentNullException.ThrowIfNull(titles);

        var wanted = WantedIds();
        foreach (var title in titles)
        {
            title.Wanted = wanted.Contains(title.GapId);
        }
    }

    /// <summary>
    /// Adds a gap to the list.
    /// </summary>
    /// <param name="gap">The gap.</param>
    /// <returns><see langword="true"/> when it was not on the list before.</returns>
    public bool Add(GapItem gap)
    {
        ArgumentNullException.ThrowIfNull(gap);
        return _todo.Add([gap]) > 0;
    }

    /// <summary>
    /// Removes an entry from the list.
    /// </summary>
    /// <param name="gapId">The entry id.</param>
    /// <returns><see langword="true"/> when an entry was removed.</returns>
    public bool Remove(string gapId) => _todo.Remove(gapId) > 0;

    /// <summary>
    /// Rebuilds a gap from a list entry, for a detail view or a Send. The snapshot carries what both need:
    /// the id, kind, name, year, poster and provider ids.
    /// </summary>
    /// <param name="gapId">The entry id.</param>
    /// <returns>The gap, or <see langword="null"/> when the entry is not on the list or is not a movie or series.</returns>
    public GapItem? FindGap(string gapId)
    {
        var entry = _todo.Load().FirstOrDefault(e => string.Equals(e.Id, gapId, StringComparison.Ordinal));
        return entry is null ? null : FromEntry(entry);
    }

    /// <summary>
    /// The pure shaping: undone entries, movies and series only, newest added first, as cards already marked
    /// as wanted.
    /// </summary>
    /// <param name="entries">The todo entries.</param>
    /// <returns>The cards.</returns>
    public static IReadOnlyList<MissingTitle> Cards(IEnumerable<TodoEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return entries
            .Where(e => !e.Done)
            .OrderByDescending(e => e.AddedUtc, StringComparer.Ordinal)
            .Select(FromEntry)
            .Where(g => g is not null)
            .Select(g => MissingTitleBuilder.ToTitle(g!, null, null))
            .Where(t => t is not null)
            .Select(t =>
            {
                t!.Wanted = true;
                return t;
            })
            .ToList();
    }

    /// <summary>
    /// Rebuilds a gap from an entry's snapshot.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The gap, or <see langword="null"/> for a kind the web UI does not show.</returns>
    public static GapItem? FromEntry(TodoEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!Enum.TryParse<BaseItemKind>(entry.TargetKindName, ignoreCase: false, out var kind)
            || (kind != BaseItemKind.Movie && kind != BaseItemKind.Series))
        {
            return null;
        }

        return new GapItem
        {
            Id = entry.Id,
            Name = entry.Name,
            Year = entry.Year,
            ReleaseDate = entry.ReleaseDate,
            TargetKind = kind,
            Domain = kind == BaseItemKind.Movie ? MediaDomain.Movies : MediaDomain.Shows,
            Pattern = Enum.TryParse<GapPattern>(entry.PatternName, ignoreCase: false, out var pattern) ? pattern : GapPattern.Recommendation,
            SourceItemName = entry.Creator,
            ImageUrl = entry.ImageUrl,
            ProviderIds = new Dictionary<string, string>(entry.ProviderIds, StringComparer.OrdinalIgnoreCase),
            Links = entry.Links,
            IsUpcoming = entry.ReleaseDate is { } d && d > DateTime.UtcNow
        };
    }
}
