namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// A curated-set reference (a TMDB studio, a Trakt list, an OpenLibrary subject, ...) as an id paired with
/// its display name, so the settings page can show the name on a chip while still storing only the id. Used
/// by the type-ahead search and the id-to-name resolution the settings page calls. The id is a string
/// because the picker kinds do not agree on a shape: most are TMDB/Discogs/MDBList numeric ids, but a Trakt
/// list can be a slug and an OpenLibrary subject always is.
/// </summary>
public sealed class CuratedSetRef
{
    /// <summary>
    /// Gets or sets the picker id (a TMDB company/keyword/list id, a Discogs label id, an MDBList list id,
    /// a Trakt list id or slug, or an OpenLibrary subject slug, depending on the kind).
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;
}
