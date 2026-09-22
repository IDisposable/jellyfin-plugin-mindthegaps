using System;

namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// One requester's copy of a title on the fulfillment queue: whose list it is on and the entry id on that
/// list, which is what <see cref="TodoDemandRow.Entries"/> carries so marking a title fetched can flip every
/// requester's entry, not just the one the row happened to be built from.
/// </summary>
public sealed class TodoDemandEntryRef
{
    /// <summary>
    /// Gets or sets the id of the user whose list the entry is on.
    /// </summary>
    public Guid OwnerId { get; set; }

    /// <summary>
    /// Gets or sets the entry's id on that user's list.
    /// </summary>
    public string GapId { get; set; } = string.Empty;
}
