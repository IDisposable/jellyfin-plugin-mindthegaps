using System.Collections.Generic;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.MindTheGaps.Providers;

/// <summary>
/// Links a book or an author carrying an OpenLibrary key to its OpenLibrary page.
/// </summary>
public sealed class OpenLibraryExternalUrlProvider : IExternalUrlProvider
{
    private readonly ProviderPrecedence _precedence;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenLibraryExternalUrlProvider"/> class.
    /// </summary>
    /// <param name="precedence">Steps this aside when another plugin provides OpenLibrary.</param>
    public OpenLibraryExternalUrlProvider(ProviderPrecedence precedence)
    {
        _precedence = precedence;
    }

    /// <inheritdoc />
    public string Name => ServiceNames.OpenLibrary;

    /// <inheritdoc />
    public IEnumerable<string> GetExternalUrls(BaseItem item)
    {
        if (item is not (Book or Person)
            || item.GetProviderId(ProviderIds.OpenLibrary) is not { } id
            || !ProviderUrls.IsOpenLibraryKey(id)
            || _precedence.ClaimedElsewhere(ProviderIds.OpenLibrary))
        {
            yield break;
        }

        yield return ProviderUrls.OpenLibrary(id)!;
    }
}
