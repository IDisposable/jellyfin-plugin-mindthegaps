using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Acquisition;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// The want-to-watch list as the web UI sees it: the plugin's todo list, which already survives scans,
/// checks itself against the library, and exports. This adds a card view of it, a per-card "is it on the
/// list" mark, a way to rebuild a gap from an entry so the detail dialog and Send work on it, and the bridge
/// to the owned side: a bookmarked title that has since arrived in the library moves onto the viewer's
/// watchlist playlist when they next look at the row.
/// </summary>
public sealed class WantToWatchService
{
    private readonly TodoStore _todo;
    private readonly LibraryVerifier _verifier;
    private readonly ILibraryManager _libraryManager;
    private readonly WatchlistPlaylistService _playlist;
    private readonly ILogger<WantToWatchService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WantToWatchService"/> class.
    /// </summary>
    /// <param name="todo">The todo store.</param>
    /// <param name="verifier">Checks entries against the library.</param>
    /// <param name="libraryManager">The library manager, to find an arrived title's item.</param>
    /// <param name="playlist">The watchlist playlist an arrived title moves onto.</param>
    /// <param name="logger">The logger.</param>
    public WantToWatchService(TodoStore todo, LibraryVerifier verifier, ILibraryManager libraryManager, WatchlistPlaylistService playlist, ILogger<WantToWatchService> logger)
    {
        _todo = todo;
        _verifier = verifier;
        _libraryManager = libraryManager;
        _playlist = playlist;
        _logger = logger;
    }

    /// <summary>
    /// The row: every entry not yet done, most recently added first. Entries the library now holds are
    /// first moved onto the viewer's watchlist playlist and marked done, so they show up as owned instead.
    /// </summary>
    /// <param name="userId">The viewer, whose playlist an arrived title moves onto.</param>
    /// <param name="isAdministrator">Whether the caller is an administrator, which gates the Send buttons.</param>
    /// <returns>The row.</returns>
    public async Task<HomeDiscoverResult> GetAsync(Guid userId, bool isAdministrator)
    {
        var config = Plugin.RequireConfiguration();
        var entries = _todo.Load().Where(e => !e.Done).ToList();
        await MoveArrivedAsync(userId, entries).ConfigureAwait(false);
        return new HomeDiscoverResult
        {
            CanSendMovies = isAdministrator && AcquisitionService.RadarrConfigured(config),
            CanSendSeries = isAdministrator && AcquisitionService.SonarrConfigured(config),
            Titles = Cards(entries.Where(e => !e.Done))
        };
    }

    // The bridge. The library check is the same one the report's TODO tab uses.
    private async Task MoveArrivedAsync(Guid userId, IReadOnlyList<TodoEntry> entries)
    {
        if (entries.Count == 0)
        {
            return;
        }

        IReadOnlyDictionary<string, bool> owned;
        try
        {
            owned = _verifier.OwnedAmong(entries);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Watchlist: could not check the list against the library");
            return;
        }

        foreach (var entry in entries)
        {
            if (!owned.TryGetValue(entry.Id, out var has) || !has)
            {
                continue;
            }

            var item = FindItem(entry);
            try
            {
                if (item is not null)
                {
                    await _playlist.AddAsync(userId, item.Id).ConfigureAwait(false);
                }

                _todo.SetDone(entry.Id, true);
                entry.Done = true;
                _logger.LogInformation("Watchlist: '{Name}' arrived in the library; moved onto user {User}'s playlist", entry.Name, userId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Watchlist: could not move '{Name}' onto the playlist", entry.Name);
            }
        }
    }

    private BaseItem? FindItem(TodoEntry entry)
    {
        if (!Enum.TryParse<BaseItemKind>(entry.TargetKindName, ignoreCase: false, out var kind))
        {
            return null;
        }

        foreach (var pair in entry.ProviderIds)
        {
            if (string.IsNullOrEmpty(pair.Value))
            {
                continue;
            }

            var items = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = [kind],
                HasAnyProviderId = new Dictionary<string, string> { [pair.Key] = pair.Value },
                IsVirtualItem = false,
                Recursive = true,
                Limit = 1
            });
            if (items.Count > 0)
            {
                return items[0];
            }
        }

        return null;
    }

    /// <summary>
    /// The titles on the list, keyed by kind and TMDB id. A title's gap id differs by the surface it came
    /// from (a filmography credit, a recommendation, a search hit), so "is it on the list" is answered by
    /// what the title is, not by which card added it.
    /// </summary>
    /// <returns>The keys of every entry not yet done.</returns>
    public HashSet<string> WantedKeys()
        => _todo.Load().Where(e => !e.Done).Select(Key).Where(k => k is not null).Select(k => k!).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Marks the cards that are on the list.
    /// </summary>
    /// <param name="titles">The cards.</param>
    public void Mark(IEnumerable<MissingTitle> titles)
    {
        ArgumentNullException.ThrowIfNull(titles);

        var wanted = WantedKeys();
        foreach (var title in titles)
        {
            title.Wanted = wanted.Contains(Key(title.Kind, title.TmdbId));
        }
    }

    /// <summary>
    /// Removes every entry for the same title as a gap, whichever surface added it.
    /// </summary>
    /// <param name="gap">The gap.</param>
    /// <returns><see langword="true"/> when an entry was removed.</returns>
    public bool RemoveMatching(GapItem gap)
    {
        ArgumentNullException.ThrowIfNull(gap);

        var key = Key(gap.TargetKind == BaseItemKind.Movie ? "Movie" : "Series", MissingTitleBuilder.TmdbId(gap) ?? 0);
        var removed = false;
        foreach (var entry in _todo.Load().Where(e => string.Equals(Key(e), key, StringComparison.Ordinal) || string.Equals(e.Id, gap.Id, StringComparison.Ordinal)).ToList())
        {
            removed |= _todo.Remove(entry.Id) > 0;
        }

        return removed;
    }

    /// <summary>
    /// The identity key of a card.
    /// </summary>
    /// <param name="kind">The kind ("Movie" or "Series").</param>
    /// <param name="tmdbId">The TMDB id.</param>
    /// <returns>The key.</returns>
    public static string Key(string kind, int tmdbId) => kind + ":" + tmdbId.ToString(CultureInfo.InvariantCulture);

    private static string? Key(TodoEntry entry)
    {
        var tmdb = entry.ProviderIds.FirstOrDefault(p => string.Equals(p.Key, ProviderIds.Tmdb, StringComparison.OrdinalIgnoreCase)).Value;
        return int.TryParse(tmdb, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0
            && (entry.TargetKindName == "Movie" || entry.TargetKindName == "Series")
            ? Key(entry.TargetKindName, id)
            : null;
    }

    /// <summary>
    /// Adds a gap to the list, unless the same title is already on it under another surface's id.
    /// </summary>
    /// <param name="gap">The gap.</param>
    /// <returns><see langword="true"/> when the title was not on the list before.</returns>
    public bool Add(GapItem gap)
    {
        ArgumentNullException.ThrowIfNull(gap);

        var key = Key(gap.TargetKind == BaseItemKind.Movie ? "Movie" : "Series", MissingTitleBuilder.TmdbId(gap) ?? 0);
        if (WantedKeys().Contains(key))
        {
            return false;
        }

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
