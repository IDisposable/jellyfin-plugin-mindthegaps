using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Discogs;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Discogs;
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
/// <see cref="GapPattern.CreatorWorks"/> body of work to discover. When Discogs is configured it also runs a
/// completeness pass: the albums Discogs lists that MusicBrainz misses, added without changing the MusicBrainz
/// gaps' ids.
/// </summary>
internal abstract class MusicBrainzArtistGapSourceBase : MusicArtistGapSourceBase
{
    // Cap the supplementary Discogs-completeness gaps for one artist; MusicBrainz is usually comprehensive,
    // so this is smaller than the standalone Discogs source's per-artist cap.
    private const int MaxCompletenessGaps = 50;

    private readonly MusicBrainzClient _musicBrainz;
    private readonly DiscogsClient _discogs;

    /// <summary>
    /// Initializes a new instance of the <see cref="MusicBrainzArtistGapSourceBase"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="musicBrainz">The MusicBrainz client.</param>
    /// <param name="discogs">The Discogs client, for the optional cross-provider completeness pass.</param>
    /// <param name="logger">The logger.</param>
    protected MusicBrainzArtistGapSourceBase(
        ILibraryManager libraryManager,
        MusicBrainzClient musicBrainz,
        DiscogsClient discogs,
        ILogger logger)
        : base(libraryManager, logger)
    {
        _musicBrainz = musicBrainz;
        _discogs = discogs;
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
        if (!artist.TryGetProviderId(ProviderIds.MusicBrainzArtist, out var artistMbid)
            || string.IsNullOrEmpty(artistMbid))
        {
            return ([], false);
        }

        // Classify against the library (a cheap indexed lookup) before spending a MusicBrainz call.
        if (!Handles(artist))
        {
            return ([], false);
        }

        IReadOnlyList<MusicBrainzReleaseGroup> albums;
        try
        {
            albums = await _musicBrainz.GetArtistAlbumsAsync(artistMbid, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning(ex, "{Source}: failed to fetch MusicBrainz albums for {Name} ({Mbid})", Name, artist.Name, artistMbid);
            return ([], true);
        }

        var gaps = MusicBrainzMapper.Build(
            artistMbid,
            albums,
            artist.Id.ToString("N", CultureInfo.InvariantCulture),
            artist.Name,
            context.Ownership,
            Pattern,
            IdPrefix).ToList();

        gaps.AddRange(await CompletenessGapsAsync(artist, albums, context, cancellationToken).ConfigureAwait(false));
        return (gaps, true);
    }

    // Opt-in widening: for an artist MusicBrainz covers, also consult Discogs and surface the albums Discogs
    // lists that the MusicBrainz album list misses (matched by normalized title). These are additive and keyed
    // by Discogs id, so the MusicBrainz gaps keep their ids (a persisted-id contract, ADR-0008). Skipped unless
    // Discogs is configured and its circuit is closed, and when the artist has no resolvable Discogs id.
    private async Task<IReadOnlyList<GapItem>> CompletenessGapsAsync(
        BaseItem artist,
        IReadOnlyList<MusicBrainzReleaseGroup> musicBrainzAlbums,
        GapScanContext context,
        CancellationToken cancellationToken)
    {
        if (!context.Config.ScanDiscogs
            || string.IsNullOrEmpty(context.Config.DiscogsToken)
            || string.IsNullOrEmpty(artist.Name)
            || ServiceCircuit.IsOpen(ServiceNames.Discogs))
        {
            return [];
        }

        var discogsId = await DiscogsArtistDiscography.ResolveIdAsync(artist, _discogs, cancellationToken).ConfigureAwait(false);
        if (discogsId is null)
        {
            return [];
        }

        IReadOnlyList<DiscogsRelease> releases;
        try
        {
            releases = await _discogs.GetArtistReleasesAsync(discogsId.Value, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning(ex, "{Source}: Discogs completeness fetch failed for {Name} (Discogs {ArtistId})", Name, artist.Name, discogsId.Value);
            return [];
        }

        var extra = DiscogsArtistDiscography.ExcludingTitles(releases, musicBrainzAlbums.Select(a => a.Title));
        return DiscogsArtistMapper.Build(
            discogsId.Value,
            artist.Name,
            extra,
            artist.Id.ToString("N", CultureInfo.InvariantCulture),
            context.Ownership,
            Pattern,
            MaxCompletenessGaps).ToList();
    }
}
