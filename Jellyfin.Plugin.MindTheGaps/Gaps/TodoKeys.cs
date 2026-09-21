using System;
using System.Collections.Generic;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// The identity of the title a todo entry is about, built the way <see cref="GapTargetKey"/> builds a gap's, so
/// "is this title on the list" means what it means everywhere else: a shared provider id under the same kind,
/// or for an album the artist-and-title name. The same title reaches a list under different gap ids (a
/// filmography, a recommendation, a collection), so the entry's id cannot answer it.
/// </summary>
internal static class TodoKeys
{
    /// <summary>
    /// Builds the identity keys for an entry, empty when its kind is not recognized.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>Its keys.</returns>
    public static IEnumerable<string> For(TodoEntry entry)
    {
        if (entry is null || !Enum.TryParse<BaseItemKind>(entry.TargetKindName, ignoreCase: false, out var kind))
        {
            yield break;
        }

        foreach (var pair in entry.ProviderIds)
        {
            if (!string.IsNullOrEmpty(pair.Value))
            {
                yield return OwnershipIndex.MakeKey(kind, pair.Key, pair.Value);
            }
        }

        if (kind == BaseItemKind.MusicAlbum && !string.IsNullOrEmpty(entry.Name))
        {
            yield return OwnershipIndex.MakeKey(kind, OwnershipIndex.NameKeyProvider, OwnershipIndex.NameKey(entry.Creator, entry.Name));
        }
    }
}
