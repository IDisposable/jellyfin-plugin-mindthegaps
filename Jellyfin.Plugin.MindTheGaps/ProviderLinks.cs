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
                    var tmdbUrl = TmdbUrl(targetKind, id);
                    if (tmdbUrl is not null)
                    {
                        links.Add(new ExternalLink("TMDB", tmdbUrl));
                    }

                    break;
                case "imdb":
                    links.Add(new ExternalLink("IMDb", string.Create(CultureInfo.InvariantCulture, $"https://www.imdb.com/title/{id}/")));
                    break;
                case "tvdb":
                    links.Add(new ExternalLink("TheTVDB", string.Create(CultureInfo.InvariantCulture, $"https://thetvdb.com/dereferrer/{TvdbType(targetKind)}/{id}")));
                    break;
                case "justwatch":
                    // A JustWatch provider id is a fullPath (e.g. "/us/movie/the-matrix") or absolute URL.
                    // The JustWatch plugin stamps it; we just render it (no dependency on that plugin).
                    var justWatchUrl = id.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? id
                        : "https://www.justwatch.com" + (id[0] == '/' ? id : "/" + id);
                    links.Add(new ExternalLink("JustWatch", justWatchUrl));
                    break;
                case "musicbrainzreleasegroup":
                    links.Add(new ExternalLink("MusicBrainz", string.Create(CultureInfo.InvariantCulture, $"https://musicbrainz.org/release-group/{id}")));
                    break;
                case "discogs":
                    // The id is a Discogs release id (the label source, and the artist source's canonical
                    // main_release), so it resolves to a real release page.
                    links.Add(new ExternalLink("Discogs", string.Create(CultureInfo.InvariantCulture, $"https://www.discogs.com/release/{id}")));
                    break;
                case "openlibrary":
                    // The id is a bare OpenLibrary work key (for example "OL45804W").
                    links.Add(new ExternalLink("OpenLibrary", string.Create(CultureInfo.InvariantCulture, $"https://openlibrary.org/works/{id}")));
                    break;
            }
        }

        return links;
    }

    // A TMDB id is per-kind: movie and tv pages key on the title id, but a season/episode page is built
    // from the series id plus numbers (which a gap's id alone cannot give), so those get no TMDB link.
    private static string? TmdbUrl(BaseItemKind targetKind, string id) => targetKind switch
    {
        BaseItemKind.Series => string.Create(CultureInfo.InvariantCulture, $"https://www.themoviedb.org/tv/{id}"),
        BaseItemKind.Movie => string.Create(CultureInfo.InvariantCulture, $"https://www.themoviedb.org/movie/{id}"),
        _ => null
    };

    // TheTVDB's dereferrer needs the right object type for the id; an episode id under "series" 404s.
    private static string TvdbType(BaseItemKind targetKind) => targetKind switch
    {
        BaseItemKind.Series => "series",
        BaseItemKind.Season => "season",
        BaseItemKind.Episode => "episode",
        _ => "movie"
    };
}
