using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
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
public sealed class CollectionGapSource : IGapSource
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
    public bool IsEnabled(PluginConfiguration config) => config.ScanCollections;

    /// <inheritdoc />
    public async IAsyncEnumerable<GapItem> FindGapsAsync(
        GapScanContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var language = context.Config.MetadataLanguage;
        var country = context.Config.MetadataCountryCode;

        var boxSets = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.BoxSet },
            Recursive = true
        });

        var index = 0;
        foreach (var boxSet in boxSets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.ReportProgress((double)index++ / Math.Max(1, boxSets.Count));

            if (!boxSet.TryGetProviderId(ProviderIds.Tmdb, out var idStr)
                || !int.TryParse(idStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var collectionId))
            {
                continue;
            }

            Collection? collection = null;
            try
            {
                collection = await _tmdb
                    .GetCollectionAsync(collectionId, language, country, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch TMDB collection {CollectionId} for {Name}", collectionId, boxSet.Name);
            }

            if (collection?.Parts is null)
            {
                continue;
            }

            var gaps = CollectionGapMapper.Build(
                collectionId,
                collection.Parts,
                boxSet.Id.ToString("N", CultureInfo.InvariantCulture),
                boxSet.Name,
                context.Ownership,
                _tmdb.GetPosterUrl);

            foreach (var gap in gaps)
            {
                yield return gap;
            }
        }
    }
}
