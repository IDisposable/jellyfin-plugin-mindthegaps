using Jellyfin.Plugin.MindTheGaps.Services.Http;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.MindTheGaps.Providers;

/// <summary>
/// The Discogs id on an album (a release id) or an artist, so it can be entered and corrected in the metadata
/// editor. The music sources own an album by it and read an artist's discography through it, and core ships no
/// provider for it.
/// </summary>
public sealed class DiscogsExternalId : IExternalId
{
    private readonly ProviderPrecedence _precedence;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiscogsExternalId"/> class.
    /// </summary>
    /// <param name="precedence">Steps this aside when another plugin provides Discogs.</param>
    public DiscogsExternalId(ProviderPrecedence precedence)
    {
        _precedence = precedence;
    }

    /// <inheritdoc />
    public string ProviderName => ServiceNames.Discogs;

    /// <inheritdoc />
    public string Key => ProviderIds.Discogs;

    /// <inheritdoc />
    public ExternalIdMediaType? Type => null;

    /// <inheritdoc />
    public bool Supports(IHasProviderIds item)
        => item is MusicAlbum or MusicArtist && !_precedence.ClaimedElsewhere(Key);
}
