using System;

namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// One user's todo list in brief, for the administrator's list of whose lists there are.
/// </summary>
public sealed class TodoOwnerSummary
{
    /// <summary>
    /// Gets or sets the user's id.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the user's name.
    /// </summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets how many entries the list holds.
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// Gets or sets how many of them are not done.
    /// </summary>
    public int Open { get; set; }
}
