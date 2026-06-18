namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// A reference to an owned item that surfaced a gap. Used to list every owned title that recommends the
/// same target, beyond the primary one carried on the gap itself.
/// </summary>
public class GapSourceRef
{
    /// <summary>
    /// Gets or sets the owning item's id (N-format guid), if it is a real library item.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the owning item's name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the owning item's type label (for example "Movie" or "Series").
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// Gets or sets the owning item's release year, if known.
    /// </summary>
    public int? Year { get; set; }
}
