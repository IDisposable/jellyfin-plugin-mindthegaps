using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// The fulfillment queue: every title on any user's todo list, folded to one row per title and sorted by
/// demand, with the web-search URL template the dashboard builds each row's search link from.
/// </summary>
public sealed class TodoDemandList
{
    /// <summary>
    /// Gets or sets the rows, sorted by outstanding demand.
    /// </summary>
    public IReadOnlyList<TodoDemandRow> Items { get; set; } = [];

    /// <summary>
    /// Gets or sets the web-search URL template (with a {0} query placeholder) from the plugin config.
    /// </summary>
    public string SearchUrlTemplate { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when this response was produced (ISO 8601 UTC string).
    /// </summary>
    public string GeneratedUtc { get; set; } = string.Empty;
}
