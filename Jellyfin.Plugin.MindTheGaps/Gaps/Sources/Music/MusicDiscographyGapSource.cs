using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.MusicBrainz;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Music;

/// <summary>
/// Finds studio albums in an owned music artist's MusicBrainz discography that are missing from the
/// library, emitting a <see cref="GapPattern.SetCompletion"/> gap per unowned release-group.
/// </summary>
public sealed class MusicDiscographyGapSource : IGapSource
{
    // MusicBrainz asks for one request per second; each artist is at least one browse call, so cap the
    // artists scanned per run the way PeopleGapSource caps people.
    private const int MaxArtists = 200;

    private readonly ILibraryManager _libraryManager;
    private readonly MusicBrainzClient _musicBrainz;
    private readonly ILogger<MusicDiscographyGapSource> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MusicDiscographyGapSource"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="musicBrainz">The MusicBrainz client.</param>
    /// <param name="logger">The logger.</param>
    public MusicDiscographyGapSource(
        ILibraryManager libraryManager,
        MusicBrainzClient musicBrainz,
        ILogger<MusicDiscographyGapSource> logger)
    {
        _libraryManager = libraryManager;
        _musicBrainz = musicBrainz;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Discography";

    /// <inheritdoc />
    public IReadOnlyCollection<BaseItemKind> OwnedKinds { get; } = new[] { BaseItemKind.MusicAlbum };

    /// <inheritdoc />
    public bool IsEnabled(PluginConfiguration config) => config.ScanMusic;

    /// <inheritdoc />
    public async IAsyncEnumerable<GapItem> FindGapsAsync(
        GapScanContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var artists = _libraryManager.GetItemList(new InternalItemsQuery
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

            if (processed >= MaxArtists)
            {
                _logger.LogInformation(
                    "Discography: reached artist cap ({Cap}); {Remaining} artists not scanned this run",
                    MaxArtists,
                    artists.Count - processed);
                break;
            }

            if (!artist.TryGetProviderId(MetadataProvider.MusicBrainzArtist, out var artistMbid)
                || string.IsNullOrEmpty(artistMbid))
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
                _logger.LogWarning(ex, "Discography: failed to fetch MusicBrainz albums for {Name} ({Mbid})", artist.Name, artistMbid);
                processed++;
                continue;
            }

            processed++;

            var gaps = MusicBrainzMapper.Build(
                artistMbid,
                albums,
                artist.Id.ToString("N", CultureInfo.InvariantCulture),
                artist.Name,
                context.Ownership);

            foreach (var gap in gaps)
            {
                yield return gap;
            }
        }
    }
}
