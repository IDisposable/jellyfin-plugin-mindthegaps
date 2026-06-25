using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;

namespace Jellyfin.Plugin.MindTheGaps;

/// <summary>
/// Builds links to a gap's source (the owned creator or set that surfaced it) own page: an actor or director
/// to their TMDB/IMDb page, an author to their OpenLibrary page, a music artist to MusicBrainz or Discogs, a
/// studio, keyword, label, or collection to its provider page. The source's <c>SourceItemType</c>
/// disambiguates the ids whose page depends on it: a TMDB id is a person, company, keyword, collection, or
/// title page, and a Discogs id is an artist or a label. A link is labeled with the service's canonical name
/// (<see cref="ServiceNames"/>) where it is a service the plugin knows.
/// </summary>
internal static class CreatorLinks
{
    /// <summary>
    /// Builds the links to the source's own page from the source's provider ids.
    /// </summary>
    /// <param name="sourceItemType">The owning item's type ("Person", "MusicArtist", "MusicLabel", "BoxSet", "Studio", "Keyword", "Movie", "Series", "Book", ...).</param>
    /// <param name="sourceProviderIds">The source's provider ids (the creator/set, not the missing item).</param>
    /// <returns>The links for known source providers (unknown providers are skipped).</returns>
    public static IReadOnlyList<ExternalLink> Build(string? sourceItemType, IReadOnlyDictionary<string, string>? sourceProviderIds)
    {
        if (sourceProviderIds is null || sourceProviderIds.Count == 0)
        {
            return [];
        }

        var links = new List<ExternalLink>();
        foreach (var providerId in sourceProviderIds)
        {
            if (string.IsNullOrEmpty(providerId.Value))
            {
                continue;
            }

            var id = providerId.Value;
            switch (providerId.Key.ToLowerInvariant())
            {
                case "tmdb":
                    var tmdbUrl = TmdbLinks.SourceUrl(sourceItemType, id);
                    if (tmdbUrl is not null)
                    {
                        links.Add(new ExternalLink(ServiceNames.Tmdb, tmdbUrl));
                    }

                    break;
                case "imdb":
                    // Only a person source carries an IMDb name id ("nm..."); a title id ("tt...") is not a creator.
                    if (id.StartsWith("nm", StringComparison.OrdinalIgnoreCase))
                    {
                        links.Add(new ExternalLink("IMDb", string.Create(CultureInfo.InvariantCulture, $"https://www.imdb.com/name/{id}/")));
                    }

                    break;
                case "trakt":
                    links.Add(new ExternalLink(ServiceNames.Trakt, string.Create(CultureInfo.InvariantCulture, $"https://trakt.tv/people/{id}")));
                    break;
                case "musicbrainzartist":
                    links.Add(new ExternalLink(ServiceNames.MusicBrainz, string.Create(CultureInfo.InvariantCulture, $"https://musicbrainz.org/artist/{id}")));
                    break;
                case "openlibrary":
                    // The source's OpenLibrary id is an author key (for example "OL79034A").
                    links.Add(new ExternalLink(ServiceNames.OpenLibrary, string.Create(CultureInfo.InvariantCulture, $"https://openlibrary.org/authors/{id}")));
                    break;
                case "discogs":
                    var discogsPath = string.Equals(sourceItemType, "MusicLabel", StringComparison.Ordinal) ? "label" : "artist";
                    links.Add(new ExternalLink(ServiceNames.Discogs, string.Create(CultureInfo.InvariantCulture, $"https://www.discogs.com/{discogsPath}/{id}")));
                    break;
            }
        }

        return links;
    }
}
