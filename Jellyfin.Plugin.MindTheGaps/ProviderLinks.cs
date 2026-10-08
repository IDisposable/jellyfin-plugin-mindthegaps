using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Providers;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;

namespace Jellyfin.Plugin.MindTheGaps;

/// <summary>
/// Builds external links for a gap from whatever provider ids it carries, so any known external
/// id on an item becomes a clickable link without each source hand-rolling URLs. A link is labeled with the
/// service's canonical name (<see cref="ServiceNames"/>) where it is a service the plugin knows.
/// </summary>
internal static class ProviderLinks
{
    /// <summary>
    /// A JustWatch search for a title in a region, for a title that carries no JustWatch id of its own. The
    /// region is a two-letter country code (the same <c>MetadataCountryCode</c> the availability lookups use);
    /// anything else falls back to <c>us</c>, since JustWatch answers an unknown region with an error page.
    /// </summary>
    /// <param name="title">The title to search for.</param>
    /// <param name="country">The ISO 3166-1 alpha-2 country code, or null.</param>
    /// <returns>The search URL, or <see langword="null"/> when there is no title to search for.</returns>
    public static string? JustWatchSearchUrl(string? title, string? country)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var region = country is { Length: 2 } && char.IsAsciiLetter(country[0]) && char.IsAsciiLetter(country[1])
            ? country.ToLowerInvariant()
            : "us";
        return "https://www.justwatch.com/" + region + "/search?q=" + Uri.EscapeDataString(title.Trim());
    }

    /// <summary>
    /// Builds the external links implied by a set of provider ids.
    /// </summary>
    /// <param name="targetKind">The gap's target kind (Movie, Series, Episode, ...).</param>
    /// <param name="providerIds">The provider ids.</param>
    /// <param name="trakt">Whether to add a Trakt link for a movie or series with an IMDb id. Set when the
    /// Trakt sources are on, so a server that does not use Trakt does not gain a link on every title.</param>
    /// <returns>The links for known providers (unknown providers are skipped).</returns>
    public static IReadOnlyList<ExternalLink> Build(BaseItemKind targetKind, IReadOnlyDictionary<string, string> providerIds, bool trakt = false)
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
                    var tmdbUrl = TmdbLinks.TitleUrl(targetKind, id);
                    if (tmdbUrl is not null)
                    {
                        links.Add(new ExternalLink(ServiceNames.Tmdb, tmdbUrl));
                    }

                    break;
                case "imdb":
                    links.Add(new ExternalLink("IMDb", string.Create(CultureInfo.InvariantCulture, $"https://www.imdb.com/title/{id}/")));
                    break;
                case "tvdb":
                    links.Add(new ExternalLink(ServiceNames.Tvdb, string.Create(CultureInfo.InvariantCulture, $"https://thetvdb.com/dereferrer/{TvdbType(targetKind)}/{id}")));
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
                    links.Add(new ExternalLink(ServiceNames.MusicBrainz, string.Create(CultureInfo.InvariantCulture, $"https://musicbrainz.org/release-group/{id}")));
                    break;
                case "discogs":
                    // The id is a Discogs release id (the label source, and the artist source's canonical
                    // main_release), so it resolves to a real release page.
                    links.Add(new ExternalLink(ServiceNames.Discogs, ProviderUrls.Discogs("release", id)!));
                    break;
                case "openlibrary":
                    // The id is a bare OpenLibrary work key (for example "OL45804W").
                    links.Add(new ExternalLink(ServiceNames.OpenLibrary, ProviderUrls.OpenLibrary(id)!));
                    break;
            }
        }

        if (trakt && TraktUrl(targetKind, providerIds) is { } traktUrl)
        {
            links.Add(new ExternalLink(ServiceNames.Trakt, traktUrl));
        }

        return links;
    }

    // Trakt resolves a movie or show page from its IMDb id, which is how the Trakt plugin links too, so no
    // Trakt id of its own is needed.
    private static string? TraktUrl(BaseItemKind targetKind, IReadOnlyDictionary<string, string> providerIds)
    {
        var path = targetKind switch
        {
            BaseItemKind.Movie => "movies",
            BaseItemKind.Series => "shows",
            _ => null
        };
        if (path is null)
        {
            return null;
        }

        foreach (var pair in providerIds)
        {
            if (string.Equals(pair.Key, ProviderIds.Imdb, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(pair.Value))
            {
                return string.Create(CultureInfo.InvariantCulture, $"https://trakt.tv/{path}/{pair.Value}");
            }
        }

        return null;
    }

    // TheTVDB's dereferrer needs the right object type for the id; an episode id under "series" 404s.
    private static string TvdbType(BaseItemKind targetKind) => targetKind switch
    {
        BaseItemKind.Series => "series",
        BaseItemKind.Season => "season",
        BaseItemKind.Episode => "episode",
        _ => "movie"
    };
}
