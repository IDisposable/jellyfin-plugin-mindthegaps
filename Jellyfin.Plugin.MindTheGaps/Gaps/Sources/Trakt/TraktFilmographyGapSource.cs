using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using Jellyfin.Plugin.MindTheGaps.Services.Trakt;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Trakt;

/// <summary>
/// Independent filmography cross-check via Trakt. Emits the same gap ids as the TMDB source
/// (<c>filmography:movie:{tmdbId}</c>), so the engine de-dupes; Trakt only contributes movies
/// TMDB missed. Opt-in: requires <see cref="PluginConfiguration.TraktEnabled"/> and a client id.
/// </summary>
internal sealed class TraktFilmographyGapSource : IGapSource
{
    // Trakt costs ~2 calls/person and is rate-limited (1000/5min), so cap lower than TMDB.
    private const int MaxPeople = 200;

    private readonly ILibraryManager _libraryManager;
    private readonly TraktClient _traktClient;
    private readonly ILogger<TraktFilmographyGapSource> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TraktFilmographyGapSource"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="traktClient">The Trakt client.</param>
    /// <param name="logger">The logger.</param>
    public TraktFilmographyGapSource(
        ILibraryManager libraryManager,
        TraktClient traktClient,
        ILogger<TraktFilmographyGapSource> logger)
    {
        _libraryManager = libraryManager;
        _traktClient = traktClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Filmography (Trakt)";

    /// <inheritdoc />
    public IReadOnlyCollection<BaseItemKind> OwnedKinds { get; } = new[] { BaseItemKind.Movie };

    /// <inheritdoc />
    public bool IsEnabled(PluginConfiguration config)
        => config.ScanPeople && config.TraktEnabled && !string.IsNullOrWhiteSpace(config.TraktClientId);

    /// <inheritdoc />
    public async IAsyncEnumerable<GapItem> FindGapsAsync(
        GapScanContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var clientId = context.Config.TraktClientId;

        var people = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Person },
            Recursive = true
        });

        var processed = 0;
        foreach (var person in people)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ServiceCircuit.IsOpen(ServiceNames.Trakt))
            {
                _logger.LogWarning("Trakt filmography: Trakt is unavailable this run; skipping the remaining people");
                break;
            }

            if (processed >= MaxPeople)
            {
                _logger.LogInformation("Trakt filmography: reached people cap ({Cap}); remaining not scanned this run", MaxPeople);
                break;
            }

            var traktId = await ResolveTraktIdAsync(clientId, person, cancellationToken).ConfigureAwait(false);
            if (traktId is null)
            {
                continue;
            }

            processed++;

            var credits = await _traktClient.GetPersonMovieCreditsAsync(clientId, traktId, cancellationToken).ConfigureAwait(false);
            if (credits is null)
            {
                continue;
            }

            var gaps = TraktFilmographyMapper.Build(
                credits,
                person.Id.ToString("N", CultureInfo.InvariantCulture),
                person.Name,
                context.Ownership);

            foreach (var gap in gaps)
            {
                yield return gap;
            }
        }
    }

    private async Task<string?> ResolveTraktIdAsync(string clientId, BaseItem person, CancellationToken cancellationToken)
    {
        if (person.TryGetProviderId(ProviderIds.Tmdb, out var tmdb) && !string.IsNullOrEmpty(tmdb))
        {
            var id = await _traktClient.FindPersonTraktIdAsync(clientId, "tmdb", tmdb, cancellationToken).ConfigureAwait(false);
            if (id is not null)
            {
                return id;
            }
        }

        if (person.TryGetProviderId(ProviderIds.Imdb, out var imdb) && !string.IsNullOrEmpty(imdb))
        {
            return await _traktClient.FindPersonTraktIdAsync(clientId, "imdb", imdb, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }
}
