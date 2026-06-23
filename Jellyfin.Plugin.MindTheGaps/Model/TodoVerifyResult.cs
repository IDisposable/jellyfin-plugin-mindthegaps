namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// The outcome of verifying a todo entry against the library: whether a real (non-virtual) item of the
/// entry's kind carrying one of its provider ids is now owned, and the entry with its done state updated to
/// match.
/// </summary>
public sealed class TodoVerifyResult
{
    /// <summary>
    /// Gets or sets a value indicating whether the library now owns the entry.
    /// </summary>
    public bool Owned { get; set; }

    /// <summary>
    /// Gets or sets the entry, with its done state set to match ownership. Null when the id is unknown.
    /// </summary>
    public TodoEntry? Entry { get; set; }
}
