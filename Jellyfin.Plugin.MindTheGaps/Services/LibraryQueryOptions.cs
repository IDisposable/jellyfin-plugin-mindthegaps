using MediaBrowser.Controller.Dto;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.MindTheGaps.Services;

/// <summary>
/// The <see cref="DtoOptions"/> this plugin attaches to every library query.
/// </summary>
/// <remarks>
/// The default is all fields, images, and user data, which the server turns into eager loads of the
/// provider-id rows, the image rows, and every user's playstate row, per item returned.
/// </remarks>
internal static class LibraryQueryOptions
{
    /// <summary>
    /// Options for a query whose results are read for their provider ids (ownership, resolution, minting).
    /// </summary>
    /// <returns>Fresh options per call.</returns>
    internal static DtoOptions WithProviderIds() => new(false)
    {
        Fields = new[] { ItemFields.ProviderIds },
        EnableImages = false,
        EnableUserData = false
    };

    /// <summary>
    /// Options for a query whose results are read for their own columns only, or not read at all (a count).
    /// Loads no navigations.
    /// </summary>
    /// <returns>Fresh options per call.</returns>
    internal static DtoOptions Minimal() => new(false)
    {
        EnableImages = false,
        EnableUserData = false
    };
}
