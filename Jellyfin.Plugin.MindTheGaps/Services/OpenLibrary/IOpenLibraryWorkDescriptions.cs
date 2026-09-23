using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.MindTheGaps.Services.OpenLibrary;

/// <summary>
/// The one OpenLibrary capability the Web UI's book detail dialog needs directly, rather than through a
/// gap source: a work's description. A public seam so <see cref="OpenLibraryClient"/> itself, and the
/// internal types the rest of its surface uses, do not have to become public just to satisfy the
/// public <see cref="WebUi.WorksMissingService"/>'s constructor.
/// </summary>
public interface IOpenLibraryWorkDescriptions
{
    /// <summary>
    /// Reads a work's description. See <see cref="OpenLibraryClient.GetWorkDescriptionAsync"/>.
    /// </summary>
    /// <param name="workKey">The work id, with or without the "/works/" prefix.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The description, or <see langword="null"/>.</returns>
    Task<string?> GetWorkDescriptionAsync(string workKey, CancellationToken cancellationToken);
}
