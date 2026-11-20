using Jellyfin.Plugin.MindTheGaps.Services.Http;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.MindTheGaps.Providers;

/// <summary>
/// The OpenLibrary author key on a person, so it can be entered and corrected in the metadata editor. The
/// bibliography reads an author's works by it before it tries the name, so setting it fixes a wrong namesake.
/// </summary>
public sealed class OpenLibraryAuthorExternalId : IExternalId
{
    private readonly ProviderPrecedence _precedence;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenLibraryAuthorExternalId"/> class.
    /// </summary>
    /// <param name="precedence">Steps this aside when another plugin provides OpenLibrary.</param>
    public OpenLibraryAuthorExternalId(ProviderPrecedence precedence)
    {
        _precedence = precedence;
    }

    /// <inheritdoc />
    public string ProviderName => ServiceNames.OpenLibrary;

    /// <inheritdoc />
    public string Key => ProviderIds.OpenLibrary;

    /// <inheritdoc />
    public ExternalIdMediaType? Type => ExternalIdMediaType.Person;

    /// <inheritdoc />
    public bool Supports(IHasProviderIds item) => item is Person && !_precedence.ClaimedElsewhere(Key);
}
