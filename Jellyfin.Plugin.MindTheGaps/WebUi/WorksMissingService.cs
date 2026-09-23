using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Books;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services;
using Jellyfin.Plugin.MindTheGaps.Services.OpenLibrary;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// Answers "what else did this artist or author make that I don't have?" for one owned artist, book or author
/// on demand. It runs the same per-owner sources the report's re-check runs (the music discography and works
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
    private readonly IOpenLibraryWorkDescriptions _openLibrary;
    private readonly IMemoryCache _cache;
    private readonly ILogger<WorksMissingService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorksMissingService"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="sources">Every registered gap source; the ones that re-run for one owner are used.</param>
    /// <param name="ownershipIndexBuilder">Indexes the owned albums or books.</param>
    /// <param name="resolutions">The dismissals, so a gap hidden on the report is hidden here too.</param>
    /// <param name="openLibrary">Fetches a book's description for the detail dialog.</param>
    /// <param name="cache">The memory cache.</param>
    /// <param name="logger">The logger.</param>
    public WorksMissingService(
        ILibraryManager libraryManager,
        IEnumerable<IGapSource> sources,
        OwnershipIndexBuilder ownershipIndexBuilder,
        ResolutionStore resolutions,
        IOpenLibraryWorkDescriptions openLibrary,
        IMemoryCache cache,
        ILogger<WorksMissingService> logger)
    {
        _libraryManager = libraryManager;
        _sources = sources;
        _ownershipIndexBuilder = ownershipIndexBuilder;
        _resolutions = resolutions;
        _openLibrary = openLibrary;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Computes the works the library lacks for an owned artist, book, or an author with an owned book.
    /// </summary>
    /// <param name="itemId">The Jellyfin item id.</param>
    /// <param name="wanted">The identity keys of the titles on the caller's want-to-watch list.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result, or <see langword="null"/> when the id is not an artist, a book, or an author, or no
    /// enabled source handles it.</returns>
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
            ItemName = _libraryManager.GetItemById(itemId)?.Name ?? owner.Name,
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
        result.Works = WorksMissingBuilder.Build(gaps, dismissed.ContainsKey, wanted, Plugin.Instance?.Configuration.SearchUrlTemplate);
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

    /// <summary>
    /// Fetches the richer detail for one missing work's dialog: today, a book's description from
    /// OpenLibrary. An album gap answers with nothing, since MusicBrainz carries no description for a
    /// release-group and a tracklist is not fetched here. Rehydrates the gap the same way
    /// <see cref="FindGapAsync"/> does, so the provider id comes from the server's own last computation,
    /// never trusted from the client.
    /// </summary>
    /// <param name="itemId">The Jellyfin item id.</param>
    /// <param name="gapId">The gap id the page showed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The detail, or <see langword="null"/> when the item or the gap is not there.</returns>
    public async Task<MissingWorkDetail?> GetDetailAsync(Guid itemId, string gapId, CancellationToken cancellationToken)
    {
        var gap = await FindGapAsync(itemId, gapId, cancellationToken).ConfigureAwait(false);
        if (gap is null)
        {
            return null;
        }

        var detail = new MissingWorkDetail();
        if (gap.TargetKind == BaseItemKind.Book
            && gap.ProviderIds.TryGetValue(ProviderIds.OpenLibrary, out var workKey)
            && !string.IsNullOrEmpty(workKey))
        {
            detail.Overview = await _openLibrary.GetWorkDescriptionAsync(workKey, cancellationToken).ConfigureAwait(false);
        }

        return detail;
    }

    // The owner and the enabled sources that produce gaps for it, or null when the id is not an artist, a book
    // or an author, or no enabled source claims it. Only these kinds: the same claim test also matches a
    // BoxSet, which the collection sources own and this surface does not present.
    private (BaseItem Owner, List<ISetContentSource> Claimants)? Prepare(Guid itemId)
    {
        var config = Plugin.RequireConfiguration();
        var owner = _libraryManager.GetItemById(itemId) switch
        {
            Person person => AuthoredBook(person, config),
            BaseItem item when item.GetBaseItemKind() is BaseItemKind.MusicArtist or BaseItemKind.Book => item,
            _ => null
        };
        if (owner is null)
        {
            return null;
        }

        var claimants = _sources.OfType<ISetContentSource>()
            .Where(s => ((IGapSource)s).IsEnabled(config) && s.Claims(owner))
            .ToList();
        return claimants.Count == 0 ? null : (owner, claimants);
    }

    // An author's page is read through one of their owned books, the way a scan reads an author: the
    // bibliography source diffs the author of the book it is handed. A co-authored book is read for its first
    // author, so only a book this person is that author of stands in for them. Looked for only when a book
    // source is on, since every actor's page asks too.
    private BaseItem? AuthoredBook(Person person, PluginConfiguration config)
    {
        var booksOn = _sources.OfType<ISetContentSource>()
            .Any(s => ((IGapSource)s).IsEnabled(config) && ((IGapSource)s).OwnedKinds.Contains(BaseItemKind.Book));
        if (!booksOn)
        {
            return null;
        }

        var books = _libraryManager.GetItemList(new InternalItemsQuery
        {
            DtoOptions = LibraryQueryOptions.WithProviderIds(),
            IncludeItemTypes = new[] { BaseItemKind.Book },
            PersonIds = new[] { person.Id },
            Recursive = true
        });
        return books.FirstOrDefault(b => string.Equals(BookAuthor.Resolve(_libraryManager.GetPeople(b)), person.Name, StringComparison.OrdinalIgnoreCase));
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
