using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps;

/// <summary>
/// Builds external links for a gap from whatever provider ids it carries, so any known external
/// id on an item becomes a clickable link without each source hand-rolling URLs.
/// </summary>
public static class ProviderLinks
{
    /// <summary>
    /// Builds the external links implied by a set of provider ids.
    /// </summary>
    /// <param name="targetKind">The gap's target kind (Movie, Series, Episode, ...).</param>
    /// <param name="providerIds">The provider ids.</param>
    /// <returns>The links for known providers (unknown providers are skipped).</returns>
    public static IReadOnlyList<ExternalLink> Build(BaseItemKind targetKind, IReadOnlyDictionary<string, string> providerIds)
    {
        var isTv = targetKind is BaseItemKind.Series or BaseItemKind.Season or BaseItemKind.Episode;
        var links = new List<ExternalLink>();

        foreach (var providerId in providerIds)
        {
            if (string.IsNullOrEmpty(providerId.Value))
            {
                continue;
            }

            var id = providerId.Value;
            switch (providerId.Key.ToLowerInvariant())
            {
                case "tmdb":
                    links.Add(new ExternalLink("TMDB", string.Create(CultureInfo.InvariantCulture, $"https://www.themoviedb.org/{(isTv ? "tv" : "movie")}/{id}")));
                    break;
                case "imdb":
                    links.Add(new ExternalLink("IMDb", string.Create(CultureInfo.InvariantCulture, $"https://www.imdb.com/title/{id}/")));
                    break;
                case "tvdb":
                    links.Add(new ExternalLink("TheTVDB", string.Create(CultureInfo.InvariantCulture, $"https://thetvdb.com/dereferrer/{(isTv ? "series" : "movie")}/{id}")));
                    break;
                case "justwatch":
                    // A JustWatch provider id is a fullPath (e.g. "/us/movie/the-matrix") or absolute URL.
                    // The JustWatch plugin stamps it; we just render it (no dependency on that plugin).
                    var justWatchUrl = id.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? id
                        : "https://www.justwatch.com" + (id[0] == '/' ? id : "/" + id);
                    links.Add(new ExternalLink("JustWatch", justWatchUrl));
                    break;
            }
        }

        return links;
    }
}
