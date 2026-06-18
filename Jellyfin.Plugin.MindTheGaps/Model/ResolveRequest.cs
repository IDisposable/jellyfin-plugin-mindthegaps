namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// Request body to mark a gap resolved with a note.
/// </summary>
public class ResolveRequest
{
    /// <summary>
    /// Gets or sets the gap id to resolve.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the note explaining why the gap is not really missing.
    /// </summary>
    public string Note { get; set; } = string.Empty;
}
