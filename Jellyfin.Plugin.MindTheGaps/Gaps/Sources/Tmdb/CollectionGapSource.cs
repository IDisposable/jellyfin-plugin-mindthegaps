using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using TMDbLib.Objects.Collections;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Tmdb;

/// <summary>
/// Finds movies that belong to an owned collection/franchise (per TMDB) but are missing from the library.
/// </summary>
/// <remarks>
/// Intentionally movie-franchise only. A Jellyfin <c>BoxSet</c> can hold mixed content (movies,
/// series, anything; there's no child-type restriction), but this source never inspects the BoxSet's
/// children. It keys off the BoxSet's TMDB *collection* id and diffs the collection's <c>Parts</c>,
/// which TMDB models as movies only (TMDB "collections" are movie franchises; shows have no equivalent
/// container). Series in a mixed collection are left alone. Missing shows within a franchise is handled
/// by the TVDB/TVMaze/Trakt sources (e.g. Wikidata P179 "part of the series" or Trakt lists).
/// </remarks>
internal sealed class CollectionGapSource : IGapSource, ISetContentSource
{
    private readonly ILibraryManager _libraryManager;
    private readonly TmdbClient _tmdb;
    private readonly ILogger<CollectionGapSource> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CollectionGapSource"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="tmdb">The TMDB client.</param>
    /// <param name="logger">The logger.</param>
    public CollectionGapSource(
        ILibraryManager libraryManager,
        TmdbClient tmdb,
        ILogger<CollectionGapSource> logger)
    {
        _libraryManager = libraryManager;
        _tmdb = tmdb;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Collections";

    /// <inheritdoc />
    public IReadOnlyCollection<BaseItemKind> OwnedKinds { get; } = new[] { BaseItemKind.Movie };

    /// <inheritdoc />
    public string GapIdPrefix => GapSourceKeys.Collection.GapPrefix;

    /// <inheritdoc />
    public bool IsEnabled(PluginConfiguration config) => config.ScanCollections;

    /// <inheritdoc />
    public bool Claims(BaseItem owner)
        => owner is not null && owner.GetBaseItemKind() == BaseItemKind.BoxSet;

    /// <inheritdoc />
    public async IAsyncEnumerable<GapItem> FindGapsAsync(
        GapScanContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var boxSets = _libraryManager.GetItemList(new InternalItemsQuery
        {
            DtoOptions = LibraryQueryOptions.WithProviderIds(),
            IncludeItemTypes = new[] { BaseItemKind.BoxSet },
            Recursive = true
        });

        var index = 0;
        foreach (var boxSet in boxSets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.ReportProgress((double)index++ / Math.Max(1, boxSets.Count));

            var gaps = await CheckOneAsync(boxSet, context, cancellationToken).ConfigureAwait(false);
            if (gaps is null)
            {
                // Nothing determined for this collection (no TMDB id, or the fetch failed). Skip it and
                // keep scanning: the scan's carry-forward keeps whatever it already had.
                continue;
            }

            foreach (var gap in gaps)
            {
                yield return gap;
            }
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GapItem>?> CheckOneAsync(BaseItem owner, GapScanContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(context);

        if (!owner.TryGetProviderId(ProviderIds.Tmdb, out var idStr)
            || !int.TryParse(idStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var collectionId))
        {
            // No TMDB id to diff against: undetermined, not "complete".
            return null;
        }

        Collection? collection;
        try
        {
            collection = await _tmdb
                .GetCollectionAsync(collectionId, context.Config.MetadataLanguage, context.Config.MetadataCountryCode, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Cancellation is deliberately not caught: a shutdown mid-batch must abort the run, not report
            // this collection as merely unresolvable and let the batch carry on.
            _logger.LogWarning(ex, "Failed to fetch TMDB collection {CollectionId} for {Name}", collectionId, owner.Name);
            return null;
        }

        if (collection?.Parts is null)
        {
            return null;
        }

        return CollectionGapMapper.Build(
            collectionId,
            collection.Parts,
            owner.Id.ToString("N", CultureInfo.InvariantCulture),
            owner.Name,
            context.Ownership,
            _tmdb.GetPosterUrl).ToList();
    }
}
