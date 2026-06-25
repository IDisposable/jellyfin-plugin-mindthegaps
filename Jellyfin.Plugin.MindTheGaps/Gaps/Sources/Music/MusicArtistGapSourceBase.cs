using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Music;

/// <summary>
/// The scan loop shared by the music artist sources (MusicBrainz and Discogs). It walks owned
/// <see cref="BaseItemKind.MusicArtist"/> items, paces and caps the per-run work the way the people source
/// does (stopping once the provider's circuit opens or the artist cap is reached), and asks the subclass to
/// resolve, fetch, and map each artist's discography. The provider-specific parts (which service, and how to
/// process one artist) are abstract, so the providers share this loop rather than each re-implementing it.
/// </summary>
internal abstract class MusicArtistGapSourceBase : IGapSource
{
    // Each artist costs at least one paced request, so cap the artists scanned per run the way the people
    // source caps people. The cap counts artists for which an API call is spent, not artists skipped early.
    private const int MaxArtists = 200;

    /// <summary>
    /// Initializes a new instance of the <see cref="MusicArtistGapSourceBase"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="logger">The logger.</param>
    protected MusicArtistGapSourceBase(ILibraryManager libraryManager, ILogger logger)
    {
        LibraryManager = libraryManager;
        Logger = logger;
    }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public IReadOnlyCollection<BaseItemKind> OwnedKinds { get; } = new[] { BaseItemKind.MusicAlbum };

    /// <summary>
    /// Gets the library manager, so a subclass can classify an artist against the owned library.
    /// </summary>
    protected ILibraryManager LibraryManager { get; }

    /// <summary>
    /// Gets the logger, for a subclass to report a provider failure for one artist.
    /// </summary>
    protected ILogger Logger { get; }

    /// <summary>
    /// Gets the HTTP service this source calls, so the loop can stop once that service's circuit opens (it
    /// has been given up on for the run).
    /// </summary>
    protected abstract string ServiceName { get; }

    /// <inheritdoc />
    public abstract bool IsEnabled(PluginConfiguration config);

    /// <inheritdoc />
    public async IAsyncEnumerable<GapItem> FindGapsAsync(
        GapScanContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var artists = LibraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.MusicArtist },
            Recursive = true
        });

        var processed = 0;
        var index = 0;
        foreach (var artist in artists)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.ReportProgress((double)index++ / Math.Max(1, artists.Count));

            // The service has been given up on for this run (its circuit is open); each remaining artist would
            // only fast-fail, so stop here rather than churn through them. Next run starts fresh.
            if (ServiceCircuit.IsOpen(ServiceName))
            {
                Logger.LogWarning("{Source}: {Service} is unavailable this run; skipping the remaining artists", Name, ServiceName);
                break;
            }

            if (processed >= MaxArtists)
            {
                Logger.LogInformation("{Source}: reached artist cap ({Cap}); some artists not scanned this run", Name, MaxArtists);
                break;
            }

            var (gaps, callSpent) = await ProcessArtistAsync(artist, context, cancellationToken).ConfigureAwait(false);

            // The cap bounds API calls, not artists examined, so only an artist a call was spent on counts.
            if (callSpent)
            {
                processed++;
            }

            foreach (var gap in gaps)
            {
                yield return gap;
            }
        }
    }

    /// <summary>
    /// Processes one owned artist: resolve it against the provider, fetch its discography, and map the
    /// unowned releases to gaps. Returns the gaps and whether an API call was spent (so the per-run cap
    /// bounds calls, not artists skipped before any call). An artist this source does not handle, or that
    /// has no usable id, returns no gaps and <see langword="false"/>.
    /// </summary>
    /// <param name="artist">The owned library artist.</param>
    /// <param name="context">The scan context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The gaps for this artist, and whether an API call was spent.</returns>
    protected abstract Task<(IReadOnlyList<GapItem> Gaps, bool CallSpent)> ProcessArtistAsync(
        BaseItem artist,
        GapScanContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reports whether the library owns at least one album whose album artist is the given artist, which
    /// is what separates an artist you collect from one you only have the odd track by.
    /// </summary>
    /// <param name="artist">The owned library artist.</param>
    /// <returns><see langword="true"/> if an owned album credits the artist as an album artist.</returns>
    protected bool OwnsAlbumByArtist(BaseItem artist)
        => LibraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.MusicAlbum },
            AlbumArtistIds = new[] { artist.Id },
            Limit = 1,
            Recursive = true
        }).Count > 0;
}
