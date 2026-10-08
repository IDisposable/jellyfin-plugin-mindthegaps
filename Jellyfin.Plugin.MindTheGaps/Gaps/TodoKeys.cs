using System;
using System.Collections.Generic;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// The identity of the title a per-user list entry is about (a todo entry, or a title the user is not
/// interested in), built the way <see cref="GapTargetKey"/> builds a gap's, so "is this title on the list"
/// means what it means everywhere else: a shared provider id under the same kind, or for an album the
/// artist-and-title name. The same title reaches a list under different gap ids (a filmography, a
/// recommendation, a collection), so the entry's id cannot answer it.
/// </summary>
internal static class TodoKeys
{
    /// <summary>
    /// Builds the identity keys for a todo entry, empty when its kind is not recognized.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>Its keys.</returns>
    public static IEnumerable<string> For(TodoEntry entry)
        => entry is null ? [] : For(entry.TargetKindName, entry.ProviderIds, entry.Name, entry.Creator);

    /// <summary>
    /// Builds the identity keys for a not-interested entry, empty when its kind is not recognized.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>Its keys.</returns>
    public static IEnumerable<string> For(NotInterestedEntry entry)
        => entry is null ? [] : For(entry.TargetKindName, entry.ProviderIds, entry.Name, entry.Creator);

    private static IEnumerable<string> For(string targetKindName, IReadOnlyDictionary<string, string> providerIds, string name, string? creator)
    {
        if (!Enum.TryParse<BaseItemKind>(targetKindName, ignoreCase: false, out var kind))
        {
            yield break;
        }

        foreach (var pair in providerIds)
        {
            if (!string.IsNullOrEmpty(pair.Value))
            {
                yield return OwnershipIndex.MakeKey(kind, pair.Key, pair.Value);
            }
        }

        if (kind == BaseItemKind.MusicAlbum && !string.IsNullOrEmpty(name))
        {
            yield return OwnershipIndex.MakeKey(kind, OwnershipIndex.NameKeyProvider, OwnershipIndex.NameKey(creator, name));
        }
    }
}
