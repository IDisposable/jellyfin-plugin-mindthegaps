using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// Every user's todo list, for an administrator: whose lists there are and all their entries, with the
/// web-search URL template the dashboard builds each row's search link from.
/// </summary>
public sealed class TodoEveryone
{
    /// <summary>
    /// Gets or sets the id of the administrator asking, whose own list is always among the owners.
    /// </summary>
    public Guid CallerId { get; set; }

    /// <summary>
    /// Gets or sets the users that have a list, the caller first and the others by name.
    /// </summary>
    public IReadOnlyList<TodoOwnerSummary> Owners { get; set; } = [];

    /// <summary>
    /// Gets or sets every entry on every list, each with its owner.
    /// </summary>
    public IReadOnlyList<OwnedTodoEntry> Items { get; set; } = [];

    /// <summary>
    /// Gets or sets the web-search URL template (with a {0} query placeholder) from the plugin config.
    /// </summary>
    public string SearchUrlTemplate { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when this response was produced (ISO 8601 UTC string).
    /// </summary>
    public string GeneratedUtc { get; set; } = string.Empty;
}
