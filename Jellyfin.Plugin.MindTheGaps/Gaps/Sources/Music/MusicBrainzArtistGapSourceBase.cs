using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using Jellyfin.Plugin.MindTheGaps.Services.MusicBrainz;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Music;

/// <summary>
/// The MusicBrainz half of the music artist sources: it resolves an owned artist by its MusicBrainz id and
/// diffs that artist's MusicBrainz studio-album discography against the library. The two MusicBrainz leaves
/// split on which artists they handle and which pattern they emit: an album artist you collect is a
/// <see cref="GapPattern.SetCompletion"/> discography, an artist you only own a track by is a
/// <see cref="GapPattern.CreatorWorks"/> body of work to discover.
/// </summary>
public abstract class MusicBrainzArtistGapSourceBase : MusicArtistGapSourceBase
{
    private readonly MusicBrainzClient _musicBrainz;

    /// <summary>
    /// Initializes a new instance of the <see cref="MusicBrainzArtistGapSourceBase"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="musicBrainz">The MusicBrainz client.</param>
    /// <param name="logger">The logger.</param>
    protected MusicBrainzArtistGapSourceBase(
        ILibraryManager libraryManager,
        MusicBrainzClient musicBrainz,
        ILogger logger)
        : base(libraryManager, logger)
    {
        _musicBrainz = musicBrainz;
    }

    /// <inheritdoc />
    protected override string ServiceName => ServiceNames.MusicBrainz;

    /// <summary>
    /// Gets the pattern this source tags its gaps with.
    /// </summary>
    protected abstract GapPattern Pattern { get; }

    /// <summary>
    /// Gets the stable-id prefix that distinguishes this source's gaps from the other music source's.
    /// </summary>
    protected abstract string IdPrefix { get; }

    /// <summary>
    /// Decides whether this source handles the given owned artist (an album artist for the discography
    /// source, a track-only artist for the works source).
    /// </summary>
    /// <param name="artist">The owned library artist.</param>
    /// <returns><see langword="true"/> if this source should scan the artist.</returns>
    protected abstract bool Handles(BaseItem artist);

    /// <inheritdoc />
    protected override async Task<(IReadOnlyList<GapItem> Gaps, bool CallSpent)> ProcessArtistAsync(
        BaseItem artist,
        GapScanContext context,
        CancellationToken cancellationToken)
    {
        if (!artist.TryGetProviderId(MetadataProvider.MusicBrainzArtist, out var artistMbid)
            || string.IsNullOrEmpty(artistMbid))
        {
            return (Array.Empty<GapItem>(), false);
        }

        // Classify against the library (a cheap indexed lookup) before spending a MusicBrainz call.
        if (!Handles(artist))
        {
            return (Array.Empty<GapItem>(), false);
        }

        IReadOnlyList<MusicBrainzReleaseGroup> albums;
        try
        {
            albums = await _musicBrainz.GetArtistAlbumsAsync(artistMbid, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning(ex, "{Source}: failed to fetch MusicBrainz albums for {Name} ({Mbid})", Name, artist.Name, artistMbid);
            return (Array.Empty<GapItem>(), true);
        }

        var gaps = MusicBrainzMapper.Build(
            artistMbid,
            albums,
            artist.Id.ToString("N", CultureInfo.InvariantCulture),
            artist.Name,
            context.Ownership,
            Pattern,
            IdPrefix).ToList();

        return (gaps, true);
    }
}
