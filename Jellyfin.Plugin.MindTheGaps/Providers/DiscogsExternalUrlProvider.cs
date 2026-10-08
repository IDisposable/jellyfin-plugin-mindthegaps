using System.Collections.Generic;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.MindTheGaps.Providers;

/// <summary>
/// Links an album carrying a Discogs release id, or an artist carrying a Discogs artist id, to its Discogs page.
/// </summary>
public sealed class DiscogsExternalUrlProvider : IExternalUrlProvider
{
    private readonly ProviderPrecedence _precedence;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiscogsExternalUrlProvider"/> class.
    /// </summary>
    /// <param name="precedence">Steps this aside when another plugin provides Discogs.</param>
    public DiscogsExternalUrlProvider(ProviderPrecedence precedence)
    {
        _precedence = precedence;
    }

    /// <inheritdoc />
    public string Name => ServiceNames.Discogs;

    /// <inheritdoc />
    public IEnumerable<string> GetExternalUrls(BaseItem item)
    {
        var kind = item switch
        {
            MusicAlbum => "release",
            MusicArtist => "artist",
            _ => null
        };
        if (kind is null
            || item.GetProviderId(ProviderIds.Discogs) is not { } id
            || !ProviderUrls.IsDiscogsId(id)
            || _precedence.ClaimedElsewhere(ProviderIds.Discogs))
        {
            yield break;
        }

        yield return ProviderUrls.Discogs(kind, id)!;
    }
}
