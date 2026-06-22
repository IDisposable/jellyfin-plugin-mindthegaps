using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Discogs;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Discogs;

/// <summary>
/// Widens the music gap sources with Discogs: it scans owned <see cref="BaseItemKind.MusicArtist"/> items the
/// MusicBrainz sources cannot (those with no MusicBrainz artist id) and diffs each artist's Discogs
/// discography against the library. Because it handles exactly the artists MusicBrainz skips, the two never
/// report the same album twice. The pattern follows the same split as the MusicBrainz sources: an album
/// artist you collect yields a <see cref="GapPattern.SetCompletion"/> discography, an artist you only own a
/// track by yields <see cref="GapPattern.CreatorWorks"/>. Opt-in and experimental; needs a Discogs token.
/// </summary>
public sealed class DiscogsArtistGapSource : IGapSource
{
    // Discogs is paced (one request a second) and each artist costs a search plus a browse, so cap the
    // artists scanned per run the way the MusicBrainz and people sources do.
    private const int MaxArtists = 200;

    // Cap the gaps emitted for one artist so a prolific discography does not flood the list.
    private const int MaxGapsPerArtist = 150;

    private readonly ILibraryManager _libraryManager;
    private readonly DiscogsClient _discogs;
    private readonly ILogger<DiscogsArtistGapSource> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiscogsArtistGapSource"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="discogs">The Discogs client.</param>
    /// <param name="logger">The logger.</param>
    public DiscogsArtistGapSource(ILibraryManager libraryManager, DiscogsClient discogs, ILogger<DiscogsArtistGapSource> logger)
    {
        _libraryManager = libraryManager;
        _discogs = discogs;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Discogs artists";

    /// <inheritdoc />
    public IReadOnlyCollection<BaseItemKind> OwnedKinds { get; } = new[] { BaseItemKind.MusicAlbum };

    /// <inheritdoc />
    public bool IsEnabled(PluginConfiguration config)
        => config.ScanDiscogs && !string.IsNullOrEmpty(config.DiscogsToken);

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

            if (ServiceCircuit.IsOpen(ServiceNames.Discogs))
            {
                _logger.LogInformation("Discogs artists: Discogs is unavailable this run; skipping the remaining artists");
                break;
            }

            if (processed >= MaxArtists)
            {
                _logger.LogInformation(
                    "Discogs artists: reached artist cap ({Cap}); some artists not scanned this run",
                    MaxArtists);
                break;
            }

            // The MusicBrainz sources already cover artists with a MusicBrainz id; Discogs widens coverage to
            // the artists they cannot scan, so the two never report the same album twice.
            if (artist.TryGetProviderId(MetadataProvider.MusicBrainzArtist, out var mbid) && !string.IsNullOrEmpty(mbid))
            {
                continue;
            }

            if (string.IsNullOrEmpty(artist.Name))
            {
                continue;
            }

            // Prefer a Discogs id already on the item; otherwise resolve the artist by name (conservatively).
            long? artistId = null;
            if (artist.TryGetProviderId("Discogs", out var tagged)
                && long.TryParse(tagged, NumberStyles.Integer, CultureInfo.InvariantCulture, out var taggedId)
                && taggedId > 0)
            {
                artistId = taggedId;
            }

            artistId ??= await _discogs.SearchArtistAsync(artist.Name, cancellationToken).ConfigureAwait(false);

            // Count the artist as processed once a Discogs call has been spent resolving it, so the cap (and
            // the pacing behind it) bounds the run whether or not the name resolved.
            processed++;
            if (artistId is null)
            {
                continue;
            }

            IReadOnlyList<DiscogsRelease> releases;
            try
            {
                releases = await _discogs.GetArtistReleasesAsync(artistId.Value, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Discogs artists: failed to fetch releases for {Name} (Discogs {ArtistId})", artist.Name, artistId.Value);
                continue;
            }

            var pattern = OwnsAlbumByArtist(artist) ? GapPattern.SetCompletion : GapPattern.CreatorWorks;
            foreach (var gap in DiscogsArtistMapper.Build(
                artistId.Value,
                artist.Name,
                releases,
                artist.Id.ToString("N", CultureInfo.InvariantCulture),
                context.Ownership,
                pattern,
                MaxGapsPerArtist))
            {
                yield return gap;
            }
        }
    }

    // Whether the library owns at least one album crediting this artist as album artist, which separates an
    // artist you collect (a discography to complete) from one you only own the odd track by (works to find).
    private bool OwnsAlbumByArtist(BaseItem artist)
        => _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.MusicAlbum },
            AlbumArtistIds = new[] { artist.Id },
            Limit = 1,
            Recursive = true
        }).Count > 0;
}
