using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Music;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Discogs;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Discogs;

/// <summary>
/// Widens the music gap sources with Discogs: it scans owned music-artist items the
/// MusicBrainz sources cannot (those with no MusicBrainz artist id) and diffs each artist's Discogs
/// discography against the library. Because it handles exactly the artists MusicBrainz skips, the two never
/// report the same album twice. The pattern follows the same split as the MusicBrainz sources: an album
/// artist you collect yields a <see cref="GapPattern.SetCompletion"/> discography, an artist you only own a
/// track by yields <see cref="GapPattern.CreatorWorks"/>. Opt-in; needs a Discogs token. It shares the
/// owned-artist walk, the cap, and the circuit handling with <see cref="MusicArtistGapSourceBase"/>.
/// </summary>
internal sealed class DiscogsArtistGapSource : MusicArtistGapSourceBase
{
    // Cap the gaps emitted for one artist so a prolific discography does not flood the list.
    private const int MaxGapsPerArtist = 150;

    private readonly DiscogsClient _discogs;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiscogsArtistGapSource"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="discogs">The Discogs client.</param>
    /// <param name="logger">The logger.</param>
    public DiscogsArtistGapSource(ILibraryManager libraryManager, DiscogsClient discogs, ILogger<DiscogsArtistGapSource> logger)
        : base(libraryManager, logger)
    {
        _discogs = discogs;
    }

    /// <inheritdoc />
    public override string Name => "Discogs artists";

    /// <inheritdoc />
    protected override string ServiceName => ServiceNames.Discogs;

    /// <inheritdoc />
    public override bool IsEnabled(PluginConfiguration config)
        => config.ScanDiscogs && !string.IsNullOrEmpty(config.DiscogsToken);

    /// <inheritdoc />
    protected override async Task<(IReadOnlyList<GapItem> Gaps, bool CallSpent)> ProcessArtistAsync(
        BaseItem artist,
        GapScanContext context,
        CancellationToken cancellationToken)
    {
        // The MusicBrainz sources already cover artists with a MusicBrainz id; Discogs widens coverage to the
        // artists they cannot scan, so the two never report the same album twice.
        if (artist.TryGetProviderId(ProviderIds.MusicBrainzArtist, out var mbid) && !string.IsNullOrEmpty(mbid))
        {
            return ([], false);
        }

        if (string.IsNullOrEmpty(artist.Name))
        {
            return ([], false);
        }

        // Prefer a Discogs id already on the item; otherwise resolve the artist by name (conservatively).
        var artistId = await DiscogsArtistDiscography.ResolveIdAsync(artist, _discogs, cancellationToken).ConfigureAwait(false);

        // A Discogs call was spent resolving the artist (and another is about to be spent fetching), so this
        // counts toward the cap whether or not the name resolved.
        if (artistId is null)
        {
            return ([], true);
        }

        IReadOnlyList<DiscogsRelease> releases;
        try
        {
            releases = await _discogs.GetArtistReleasesAsync(artistId.Value, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning(ex, "Discogs artists: failed to fetch releases for {Name} (Discogs {ArtistId})", artist.Name, artistId.Value);
            return ([], true);
        }

        var pattern = OwnsAlbumByArtist(artist) ? GapPattern.SetCompletion : GapPattern.CreatorWorks;
        var gaps = DiscogsArtistMapper.Build(
            artistId.Value,
            artist.Name,
            releases,
            artist.Id.ToString("N", CultureInfo.InvariantCulture),
            context.Ownership,
            pattern,
            MaxGapsPerArtist).ToList();

        return (gaps, true);
    }
}
