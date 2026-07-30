using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.JustWatch;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.JustWatch;

/// <summary>
/// Turns the entries of a JustWatch account list into discovery (<see cref="GapPattern.Recommendation"/>)
/// gaps for the titles the library does not own. Each entry carries the TMDB and IMDb ids JustWatch records,
/// so the gap keys on those directly. The entry's object type routes a show to the Shows domain and a movie
/// to the Movies domain.
/// </summary>
internal static class JustWatchListMapper
{
    /// <summary>
    /// Builds gaps for a list's unowned entries, de-duplicated by their strongest id and capped.
    /// </summary>
    /// <param name="listType">The list type, a value from <see cref="JustWatchListType.All"/>.</param>
    /// <param name="listName">The list's display name (the gap's source).</param>
    /// <param name="titles">The list's entries.</param>
    /// <param name="ownership">The library ownership index.</param>
    /// <param name="maxResults">The most gaps to emit for this list.</param>
    /// <returns>The discovery gaps for unowned entries.</returns>
    public static IEnumerable<GapItem> Build(
        string listType,
        string listName,
        IEnumerable<JustWatchTitle> titles,
        OwnershipIndex ownership,
        int maxResults)
    {
        ArgumentNullException.ThrowIfNull(titles);
        ArgumentNullException.ThrowIfNull(ownership);

        var emitted = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in titles)
        {
            if (emitted >= maxResults)
            {
                break;
            }

            var content = entry.Content;
            var name = content?.Title;
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var isShow = string.Equals(entry.ObjectType, "SHOW", StringComparison.OrdinalIgnoreCase);
            var kind = isShow ? BaseItemKind.Series : BaseItemKind.Movie;
            var domain = isShow ? MediaDomain.Shows : MediaDomain.Movies;

            var providerIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(content?.ExternalIds?.TmdbId))
            {
                providerIds[ProviderIds.Tmdb] = content.ExternalIds.TmdbId;
            }

            if (!string.IsNullOrEmpty(content?.ExternalIds?.ImdbId))
            {
                providerIds[ProviderIds.Imdb] = content.ExternalIds.ImdbId;
            }

            // Nothing to diff against or key on without at least one external id.
            var idKey = providerIds.TryGetValue(ProviderIds.Tmdb, out var tmdb) ? tmdb
                : providerIds.TryGetValue(ProviderIds.Imdb, out var imdb) ? imdb
                : null;
            if (idKey is null || !seen.Add(idKey))
            {
                continue;
            }

            if (ownership.OwnsAny(kind, providerIds))
            {
                continue;
            }

            var titleUrl = JustWatchClient.TitleUrl(content?.FullPath);
            emitted++;
            yield return GapItemFactory.Create(
                id: string.Create(CultureInfo.InvariantCulture, $"{GapIdPrefixes.JustWatch}{listType.ToLowerInvariant()}:{idKey}"),
                pattern: GapPattern.Recommendation,
                domain: domain,
                targetKind: kind,
                name: name,
                providerIds: providerIds,
                sourceItemId: SourceItemIds.JustWatchList(listType.ToLowerInvariant()),
                sourceItemName: listName,
                sourceItemType: SourceItemTypes.JustWatchList,
                releaseDate: content?.OriginalReleaseYear is > 0
                    ? new DateTime(content.OriginalReleaseYear.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    : null,
                imageUrl: JustWatchClient.PosterUrl(content?.PosterUrl),
                extraLinks: titleUrl is null ? null : [new ExternalLink("JustWatch", titleUrl)]);
        }
    }
}
