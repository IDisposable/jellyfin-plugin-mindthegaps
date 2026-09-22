namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// The outcome of marking a fulfillment queue row fetched: how many requester entries were named and how
/// many were actually found and flipped (a requester who removed the entry in the meantime is not an error).
/// </summary>
public sealed class TodoMarkFetchedResult
{
    /// <summary>
    /// Gets or sets how many entry refs were posted.
    /// </summary>
    public int Requested { get; set; }

    /// <summary>
    /// Gets or sets how many were found and marked done.
    /// </summary>
    public int Updated { get; set; }
}
