using Jellyfin.Plugin.MindTheGaps.Services.Http;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.MindTheGaps.Providers;

/// <summary>
/// The OpenLibrary work id on a book, so it can be entered and corrected in the metadata editor. The book
/// sources own a book by it and resolve its author through it, and core ships no provider for it.
/// </summary>
public sealed class OpenLibraryExternalId : IExternalId
{
    private readonly ProviderPrecedence _precedence;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenLibraryExternalId"/> class.
    /// </summary>
    /// <param name="precedence">Steps this aside when another plugin provides OpenLibrary.</param>
    public OpenLibraryExternalId(ProviderPrecedence precedence)
    {
        _precedence = precedence;
    }

    /// <inheritdoc />
    public string ProviderName => ServiceNames.OpenLibrary;

    /// <inheritdoc />
    public string Key => ProviderIds.OpenLibrary;

    /// <inheritdoc />
    public ExternalIdMediaType? Type => ExternalIdMediaType.Book;

    /// <inheritdoc />
    public bool Supports(IHasProviderIds item) => item is Book && !_precedence.ClaimedElsewhere(Key);
}
