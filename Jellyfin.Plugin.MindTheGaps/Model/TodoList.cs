using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// The personal todo list returned to the dashboard, with the web-search URL template the dashboard builds
/// each row's search link from.
/// </summary>
public sealed class TodoList
{
    /// <summary>
    /// Gets or sets the todo entries.
    /// </summary>
    public IReadOnlyList<TodoEntry> Items { get; set; } = Array.Empty<TodoEntry>();

    /// <summary>
    /// Gets or sets the web-search URL template (with a {0} query placeholder) from the plugin config.
    /// </summary>
    public string SearchUrlTemplate { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when this response was produced (ISO 8601 UTC string).
    /// </summary>
    public string GeneratedUtc { get; set; } = string.Empty;
}
