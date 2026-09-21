using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// Answers "what else did this artist or author make that I don't have?" for one owned artist or book on
/// demand. It runs the same per-owner sources the report's re-check runs (the music discography and works
/// sources, the Discogs artist source, the book bibliography) against a fresh ownership index of the kind
/// they diff, and drops what the report has dismissed. Nothing is written to the report, so it is independent
/// of the scan and identical to what a scan or re-check would have produced for that owner.
/// </summary>
public sealed class WorksMissingService
{
    private readonly ILibraryManager _libraryManager;
    private readonly IEnumerable<IGapSource> _sources;
    private readonly OwnershipIndexBuilder _ownershipIndexBuilder;
    private readonly ResolutionStore _resolutions;
    private readonly IMemoryCache _cache;
    private readonly ILogger<WorksMissingService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorksMissingService"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="sources">Every registered gap source; the ones that re-run for one owner are used.</param>
    /// <param name="ownershipIndexBuilder">Indexes the owned albums or books.</param>
    /// <param name="resolutions">The dismissals, so a gap hidden on the report is hidden here too.</param>
    /// <param name="cache">The memory cache.</param>
    /// <param name="logger">The logger.</param>
    public WorksMissingService(
        ILibraryManager libraryManager,
        IEnumerable<IGapSource> sources,
        OwnershipIndexBuilder ownershipIndexBuilder,
        ResolutionStore resolutions,
        IMemoryCache cache,
        ILogger<WorksMissingService> logger)
    {
        _libraryManager = libraryManager;
        _sources = sources;
        _ownershipIndexBuilder = ownershipIndexBuilder;
        _resolutions = resolutions;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Computes the works the library lacks for an owned artist or book.
    /// </summary>
    /// <param name="itemId">The Jellyfin item id.</param>
    /// <param name="wanted">The identity keys of the titles on the caller's want-to-watch list.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result, or <see langword="null"/> when the id is not an artist or book, or no enabled
    /// source handles it.</returns>
    public async Task<WorksMissingResult?> GetAsync(Guid itemId, IReadOnlySet<string> wanted, CancellationToken cancellationToken)
    {
        var lookup = Prepare(itemId);
        if (lookup is null)
        {
            return null;
        }

        var (owner, claimants) = lookup.Value;
        var kind = owner.GetBaseItemKind() == BaseItemKind.Book ? "Book" : "MusicAlbum";
        var result = new WorksMissingResult
        {
            ItemId = itemId,
            ItemName = owner.Name,
            Kind = kind
        };

        var gaps = await RunAsync(owner, claimants, cancellationToken).ConfigureAwait(false);
        if (gaps is null)
        {
            result.Reason = kind == "Book"
                ? "This book's author could not be looked up: the book carries no OpenLibrary id or the provider did not answer."
                : "This artist's albums could not be looked up: the artist carries no MusicBrainz or Discogs id or the provider did not answer.";
            return result;
        }

        var dismissed = _resolutions.GetAll();
        result.Works = WorksMissingBuilder.Build(gaps, dismissed.ContainsKey, wanted);
        return result;
    }

    /// <summary>
    /// Rehydrates one of the item's missing works by id, for a todo add. Recomputed server-side from the same
    /// inputs the page listed.
    /// </summary>
    /// <param name="itemId">The Jellyfin item id.</param>
    /// <param name="gapId">The gap id the page showed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The gap, or <see langword="null"/> when the item or the gap is not there.</returns>
    public async Task<GapItem?> FindGapAsync(Guid itemId, string gapId, CancellationToken cancellationToken)
    {
        var lookup = Prepare(itemId);
        if (lookup is null || string.IsNullOrEmpty(gapId))
        {
            return null;
        }

        var (owner, claimants) = lookup.Value;
        var gaps = await RunAsync(owner, claimants, cancellationToken).ConfigureAwait(false);
        return gaps?.FirstOrDefault(g => string.Equals(g.Id, gapId, StringComparison.Ordinal));
    }

    // The owner and the enabled sources that produce gaps for it, or null when the id is not an artist or a
    // book or no enabled source claims it. Only these two kinds: the same claim test also matches a BoxSet,
    // which the collection sources own and this surface does not present.
    private (BaseItem Owner, List<ISetContentSource> Claimants)? Prepare(Guid itemId)
    {
        if (_libraryManager.GetItemById(itemId) is not BaseItem owner
            || owner.GetBaseItemKind() is not (BaseItemKind.MusicArtist or BaseItemKind.Book))
        {
            return null;
        }

        var config = Plugin.RequireConfiguration();
        var claimants = _sources.OfType<ISetContentSource>()
            .Where(s => ((IGapSource)s).IsEnabled(config) && s.Claims(owner))
            .ToList();
        return claimants.Count == 0 ? null : (owner, claimants);
    }

    // Runs every claiming source and merges by gap id. Null when none of them could determine an answer, so
    // an outage reads as "could not look it up" rather than "nothing is missing".
    private async Task<List<GapItem>?> RunAsync(BaseItem owner, List<ISetContentSource> claimants, CancellationToken cancellationToken)
    {
        var config = Plugin.RequireConfiguration();
        var kinds = claimants.SelectMany(c => ((IGapSource)c).OwnedKinds).Distinct().ToArray();
        var cacheKey = OwnershipCache.KeyFor(kinds);
        if (!_cache.TryGetValue(cacheKey, out OwnershipIndex? ownership) || ownership is null)
        {
            ownership = _ownershipIndexBuilder.Build(kinds);
            _cache.Set(cacheKey, ownership, OwnershipCache.Ttl);
        }

        var context = new GapScanContext(config, ownership);
        var byId = new Dictionary<string, GapItem>(StringComparer.Ordinal);
        var answered = false;
        foreach (var source in claimants)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<GapItem>? found;
            try
            {
                found = await source.CheckOneAsync(owner, context, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Works: {Source} failed for {Owner}", ((IGapSource)source).Name, owner.Name);
                continue;
            }

            if (found is null)
            {
                continue;
            }

            answered = true;
            foreach (var gap in found)
            {
                byId.TryAdd(gap.Id, gap);
            }
        }

        _logger.LogDebug("Works: {Count} missing for '{Owner}' from {Sources} source(s)", byId.Count, owner.Name, claimants.Count);
        return answered ? byId.Values.ToList() : null;
    }
}
