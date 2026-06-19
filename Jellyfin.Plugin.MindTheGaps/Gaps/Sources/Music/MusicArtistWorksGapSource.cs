using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.MusicBrainz;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Music;

/// <summary>
/// Finds studio albums to discover from an artist you only own the odd track by (no owned album credits
/// them as the album artist), emitting a <see cref="GapPattern.CreatorWorks"/> gap per unowned
/// release-group. The audio analogue of an actor's filmography: a featured or guest artist's wider body of
/// work. Album artists you collect are left to <see cref="MusicDiscographyGapSource"/>.
/// </summary>
public sealed class MusicArtistWorksGapSource : MusicArtistGapSourceBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MusicArtistWorksGapSource"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="musicBrainz">The MusicBrainz client.</param>
    /// <param name="logger">The logger.</param>
    public MusicArtistWorksGapSource(
        ILibraryManager libraryManager,
        MusicBrainzClient musicBrainz,
        ILogger<MusicArtistWorksGapSource> logger)
        : base(libraryManager, musicBrainz, logger)
    {
    }

    /// <inheritdoc />
    public override string Name => "Artist works";

    /// <inheritdoc />
    protected override GapPattern Pattern => GapPattern.CreatorWorks;

    /// <inheritdoc />
    protected override string IdPrefix => "artistworks";

    /// <inheritdoc />
    public override bool IsEnabled(PluginConfiguration config) => config.ScanMusic;

    /// <inheritdoc />
    protected override bool Handles(BaseItem artist) => !OwnsAlbumByArtist(artist);
}
