namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// A chip-pickable explore kind, as offered to the dashboard's "Explore a source" dropdown: the kind token,
/// its human label, and whether it has a type-ahead (a searchable kind shows a chip picker; one without is
/// entered by raw id, like a TMDB list). Derived from the registered sources, so the dropdown is not a
/// hand-maintained list.
/// </summary>
public sealed class ExploreKindInfo
{
    /// <summary>
    /// Gets or sets the kind token (case-insensitive), for example "studio" or "mdblist".
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the human label shown in the dropdown, for example "Studio".
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this kind has a name type-ahead (a chip picker); when false it
    /// is entered by raw id.
    /// </summary>
    public bool Searchable { get; set; }
}
