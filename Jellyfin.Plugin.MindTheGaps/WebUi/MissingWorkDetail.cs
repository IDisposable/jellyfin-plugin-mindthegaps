namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// The richer detail for a missing work's dialog, fetched on demand once the dialog opens. Today this is
/// only a book's description (from OpenLibrary): an album answers with nothing, since MusicBrainz has no
/// description for a release-group and a tracklist is not fetched here.
/// </summary>
public sealed class MissingWorkDetail
{
    /// <summary>
    /// Gets or sets the book's description, or <see langword="null"/> when there is none (always null for
    /// an album).
    /// </summary>
    public string? Overview { get; set; }
}
