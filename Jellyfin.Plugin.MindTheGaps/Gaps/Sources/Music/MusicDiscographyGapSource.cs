using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Discogs;
using Jellyfin.Plugin.MindTheGaps.Services.MusicBrainz;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Music;

/// <summary>
/// Finds studio albums missing from the discography of an album artist you collect (one the library owns
/// at least one album by), emitting a <see cref="GapPattern.SetCompletion"/> gap per unowned release-group.
/// Artists you only own the odd track by are left to <see cref="MusicArtistWorksGapSource"/>.
/// </summary>
public sealed class MusicDiscographyGapSource : MusicBrainzArtistGapSourceBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MusicDiscographyGapSource"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="musicBrainz">The MusicBrainz client.</param>
    /// <param name="discogs">The Discogs client, for the cross-provider completeness pass.</param>
    /// <param name="logger">The logger.</param>
    public MusicDiscographyGapSource(
        ILibraryManager libraryManager,
        MusicBrainzClient musicBrainz,
        DiscogsClient discogs,
        ILogger<MusicDiscographyGapSource> logger)
        : base(libraryManager, musicBrainz, discogs, logger)
    {
    }

    /// <inheritdoc />
    public override string Name => "Discography";

    /// <inheritdoc />
    protected override GapPattern Pattern => GapPattern.SetCompletion;

    /// <inheritdoc />
    protected override string IdPrefix => "discography";

    /// <inheritdoc />
    public override bool IsEnabled(PluginConfiguration config) => config.ScanMusic;

    /// <inheritdoc />
    protected override bool Handles(BaseItem artist) => OwnsAlbumByArtist(artist);
}
