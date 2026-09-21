using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// A todo entry together with whose list it is on, for the administrator's view of everyone's lists.
/// </summary>
public sealed class OwnedTodoEntry : TodoEntry
{
    /// <summary>
    /// Gets or sets the id of the user whose list the entry is on.
    /// </summary>
    public Guid OwnerId { get; set; }

    /// <summary>
    /// Gets or sets the name of the user whose list the entry is on.
    /// </summary>
    public string OwnerName { get; set; } = string.Empty;

    /// <summary>
    /// Copies an entry and names its owner.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="ownerId">The owner's id.</param>
    /// <param name="ownerName">The owner's name.</param>
    /// <returns>The entry with its owner.</returns>
    public static OwnedTodoEntry From(TodoEntry entry, Guid ownerId, string ownerName)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new OwnedTodoEntry
        {
            Id = entry.Id,
            Name = entry.Name,
            Year = entry.Year,
            DomainName = entry.DomainName,
            TargetKindName = entry.TargetKindName,
            PatternName = entry.PatternName,
            Creator = entry.Creator,
            ImageUrl = entry.ImageUrl,
            ReleaseDate = entry.ReleaseDate,
            ProviderIds = new Dictionary<string, string>(entry.ProviderIds, StringComparer.OrdinalIgnoreCase),
            Links = entry.Links,
            Done = entry.Done,
            AddedUtc = entry.AddedUtc,
            DoneUtc = entry.DoneUtc,
            OwnerId = ownerId,
            OwnerName = ownerName
        };
    }
}
