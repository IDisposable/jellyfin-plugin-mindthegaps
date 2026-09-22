using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Folds every user's todo entries into one row per title, for the fulfillment queue: an administrator
/// without a Radarr/Sonarr/Jellyseerr setup wants to see what is actually in demand across the household,
/// not comb through everyone's list separately.
/// </summary>
/// <remarks>
/// Two users can add "the same title" from different gaps that carry different provider ids (one from a
/// collection gap with only a TMDB id, another from a recommendation that has since had its IMDb id
/// resolved), so grouping is the same identity rule <see cref="GapTargetKey"/> uses for the report's
/// clear-down: a shared provider id under the same item kind, or for an album the artist-and-title name key.
/// Matching is transitive (a union-find over the entries), for the same reason it is there: a title's rows do
/// not all carry the same ids.
/// </remarks>
internal static class TodoDemandAggregator
{
    /// <summary>
    /// Builds the fulfillment queue rows from every user's flattened todo entries.
    /// </summary>
    /// <param name="entries">Every user's todo entries, each carrying its owner.</param>
    /// <returns>One row per title, sorted by outstanding demand.</returns>
    public static IReadOnlyList<TodoDemandRow> Build(IReadOnlyList<OwnedTodoEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            return [];
        }

        var parent = new int[entries.Count];
        for (var i = 0; i < parent.Length; i++)
        {
            parent[i] = i;
        }

        // Path-halving find, same technique as a textbook union-find: cheap, and the entry count here is a
        // household's todo lists, not the whole report, so there is no need for rank/size bookkeeping too.
        int Find(int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }

            return i;
        }

        void Union(int a, int b)
        {
            a = Find(a);
            b = Find(b);
            if (a != b)
            {
                parent[a] = b;
            }
        }

        // The first entry seen carrying a key becomes that key's representative; every later entry carrying
        // the same key unions onto it, which is what chains a TMDB-only entry to an IMDb-only one through a
        // third entry that carries both.
        var firstByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < entries.Count; i++)
        {
            foreach (var key in Keys(entries[i]))
            {
                if (firstByKey.TryGetValue(key, out var first))
                {
                    Union(first, i);
                }
                else
                {
                    firstByKey[key] = i;
                }
            }
        }

        var groups = new Dictionary<int, List<int>>();
        for (var i = 0; i < entries.Count; i++)
        {
            var root = Find(i);
            if (!groups.TryGetValue(root, out var members))
            {
                members = [];
                groups[root] = members;
            }

            members.Add(i);
        }

        return groups.Values
            .Select(members => BuildRow(members.Select(i => entries[i]).ToList()))
            .OrderByDescending(r => r.OpenCount)
            .ThenByDescending(r => r.RequestCount)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // Mirrors GapTargetKey.For, but for a todo entry (a string kind name and no SourceItemName; Creator is
    // its equivalent) rather than a live GapItem.
    private static IEnumerable<string> Keys(TodoEntry entry)
    {
        if (!Enum.TryParse<BaseItemKind>(entry.TargetKindName, out var kind))
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
            yield return OwnershipIndex.MakeKey(
                kind,
                OwnershipIndex.NameKeyProvider,
                OwnershipIndex.NameKey(entry.Creator, entry.Name));
        }
    }

    private static TodoDemandRow BuildRow(IReadOnlyList<OwnedTodoEntry> members)
    {
        // The entry with the most provider ids is the most complete snapshot of the title to render from;
        // ties fall to whichever was added first, then to its id, so the choice is deterministic run to run.
        var template = members
            .OrderByDescending(e => e.ProviderIds.Count)
            .ThenBy(e => e.AddedUtc, StringComparer.Ordinal)
            .ThenBy(e => e.Id, StringComparer.Ordinal)
            .First();

        var providerIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in members.SelectMany(e => e.ProviderIds))
        {
            if (!string.IsNullOrEmpty(pair.Value))
            {
                providerIds.TryAdd(pair.Key, pair.Value);
            }
        }

        var links = new List<ExternalLink>();
        var seenLinkUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in members.SelectMany(e => e.Links))
        {
            if (link is not null && !string.IsNullOrEmpty(link.Url) && seenLinkUrls.Add(link.Url))
            {
                links.Add(link);
            }
        }

        var byOwner = members
            .GroupBy(e => e.OwnerId)
            .Select(g => (g.Key, Name: g.First().OwnerName, Open: g.Any(e => !e.Done)))
            .OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var entryRefs = members
            .Select(e => new TodoDemandEntryRef { OwnerId = e.OwnerId, GapId = e.Id })
            .OrderBy(r => r.OwnerId)
            .ThenBy(r => r.GapId, StringComparer.Ordinal)
            .ToList();

        return new TodoDemandRow
        {
            Id = members.Select(e => e.Id).OrderBy(id => id, StringComparer.Ordinal).First(),
            Name = template.Name,
            Year = template.Year,
            DomainName = template.DomainName,
            TargetKindName = template.TargetKindName,
            PatternName = template.PatternName,
            Creator = template.Creator,
            ImageUrl = template.ImageUrl,
            ReleaseDate = template.ReleaseDate,
            ProviderIds = providerIds,
            Links = links,
            RequestCount = byOwner.Count,
            OpenCount = byOwner.Count(o => o.Open),
            RequestedBy = byOwner.Select(o => o.Name).ToList(),
            Entries = entryRefs
        };
    }
}
