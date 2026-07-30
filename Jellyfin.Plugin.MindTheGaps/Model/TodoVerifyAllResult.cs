using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// The outcome of verifying the whole todo list against the library: how much was checked, how much the
/// library now holds, and the list with every entry's done state brought up to date.
/// </summary>
public class TodoVerifyAllResult
{
    /// <summary>
    /// Gets or sets how many entries were checked.
    /// </summary>
    public int Checked { get; set; }

    /// <summary>
    /// Gets or sets how many of them the library now holds, and which are therefore marked done.
    /// </summary>
    public int Owned { get; set; }

    /// <summary>
    /// Gets or sets the todo list after the pass, so the dashboard can re-render without a second fetch.
    /// </summary>
    public IReadOnlyList<TodoEntry> Items { get; set; } = [];
}
