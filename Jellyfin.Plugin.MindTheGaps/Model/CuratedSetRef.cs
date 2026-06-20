using System.Globalization;

namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// A curated-set reference (a TMDB studio or keyword) as an id paired with its display name, so the
/// settings page can show the name on a chip while still storing only the id. Used by the type-ahead
/// search and the id-to-name resolution the settings page calls.
/// </summary>
public sealed class CuratedSetRef
{
    /// <summary>
    /// Gets or sets the provider id (a TMDB company id for a studio, a TMDB keyword id for a keyword).
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets the id as an invariant string, for keying a chip on the settings page.
    /// </summary>
    public string IdText => Id.ToString(CultureInfo.InvariantCulture);
}
