namespace Jellyfin.Plugin.MindTheGaps.Services.OpenLibrary;

/// <summary>
/// One entry in a reader's public reading log.
/// </summary>
internal sealed class OpenLibraryReadingLogEntry
{
    /// <summary>
    /// Gets or sets the work the entry is about.
    /// </summary>
    public OpenLibraryReadingLogWork? Work { get; set; }
}
