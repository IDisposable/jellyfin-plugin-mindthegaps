using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using Jellyfin.Plugin.MindTheGaps.Services.MusicBrainz;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Music;

/// <summary>
/// Shared logic for the two music sources, which both walk owned <see cref="BaseItemKind.MusicArtist"/>
/// items and diff each artist's MusicBrainz studio-album discography against the library. They differ only
/// in which artists they handle and which pattern they emit: an album artist you collect is a
/// <see cref="GapPattern.SetCompletion"/> discography, an artist you only own tracks by is a
/// <see cref="GapPattern.CreatorWorks"/> body of work to discover. Splitting on that keeps "complete a set
/// I'm collecting" distinct from "discover more by an artist I have a song from".
/// </summary>
public abstract class MusicArtistGapSourceBase : IGapSource
{
    // MusicBrainz asks for one request per second; each artist is at least one browse call, so cap the
    // artists scanned per run the way PeopleGapSource caps people.
    private const int MaxArtists = 200;

    private readonly MusicBrainzClient _musicBrainz;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MusicArtistGapSourceBase"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="musicBrainz">The MusicBrainz client.</param>
    /// <param name="logger">The logger.</param>
    protected MusicArtistGapSourceBase(
        ILibraryManager libraryManager,
        MusicBrainzClient musicBrainz,
        ILogger logger)
    {
        LibraryManager = libraryManager;
        _musicBrainz = musicBrainz;
        _logger = logger;
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
    /// Gets the pattern this source tags its gaps with.
    /// </summary>
    protected abstract GapPattern Pattern { get; }

    /// <summary>
    /// Gets the stable-id prefix that distinguishes this source's gaps from the other music source's.
    /// </summary>
    protected abstract string IdPrefix { get; }

    /// <inheritdoc />
    public abstract bool IsEnabled(PluginConfiguration config);

    /// <summary>
    /// Decides whether this source handles the given owned artist (an album artist for the discography
    /// source, a track-only artist for the works source).
    /// </summary>
    /// <param name="artist">The owned library artist.</param>
    /// <returns><see langword="true"/> if this source should scan the artist.</returns>
    protected abstract bool Handles(BaseItem artist);

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

            // MusicBrainz has been given up on for this run (its circuit is open); each remaining artist would
            // only fast-fail, so stop here rather than churn through them. Next run starts fresh.
            if (ServiceCircuit.IsOpen("MusicBrainz"))
            {
                _logger.LogInformation("{Source}: MusicBrainz is unavailable this run; skipping the remaining artists", Name);
                break;
            }

            if (processed >= MaxArtists)
            {
                _logger.LogInformation(
                    "{Source}: reached artist cap ({Cap}); {Remaining} artists not scanned this run",
                    Name,
                    MaxArtists,
                    artists.Count - processed);
                break;
            }

            if (!artist.TryGetProviderId(MetadataProvider.MusicBrainzArtist, out var artistMbid)
                || string.IsNullOrEmpty(artistMbid))
            {
                continue;
            }

            // Classify against the library (a cheap indexed lookup) before spending a MusicBrainz call.
            if (!Handles(artist))
            {
                continue;
            }

            IReadOnlyList<MusicBrainzReleaseGroup> albums;
            try
            {
                albums = await _musicBrainz.GetArtistAlbumsAsync(artistMbid, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "{Source}: failed to fetch MusicBrainz albums for {Name} ({Mbid})", Name, artist.Name, artistMbid);
                processed++;
                continue;
            }

            processed++;

            var gaps = MusicBrainzMapper.Build(
                artistMbid,
                albums,
                artist.Id.ToString("N", CultureInfo.InvariantCulture),
                artist.Name,
                context.Ownership,
                Pattern,
                IdPrefix);

            foreach (var gap in gaps)
            {
                yield return gap;
            }
        }
    }

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
