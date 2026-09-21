using System;
using Jellyfin.Data.Enums;

namespace Jellyfin.Plugin.MindTheGaps.Services.Tmdb;

/// <summary>
/// Builds links to themoviedb.org pages from a TMDB id. The page path depends on what the id identifies, so
/// the per-kind and per-source-type mapping lives here, in one TMDB place, rather than in the generic link
/// builders.
/// </summary>
internal static class TmdbLinks
{
    private const string Base = "https://www.themoviedb.org/";

    /// <summary>
    /// The themoviedb.org page for a title id, or null for a kind whose page a bare id cannot form (a season
    /// or episode page needs the series id plus numbers).
    /// </summary>
    /// <param name="targetKind">The gap's target kind.</param>
    /// <param name="id">The TMDB id.</param>
    /// <returns>The page URL, or null.</returns>
    public static string? TitleUrl(BaseItemKind targetKind, string id) => targetKind switch
    {
        BaseItemKind.Series => Base + "tv/" + id,
        BaseItemKind.Movie => Base + "movie/" + id,
        _ => null
    };

    /// <summary>
    /// The themoviedb.org page for a source id, keyed by the owning item's type (a person, collection,
    /// company, keyword, list, or title), or null when the type has no TMDB page.
    /// </summary>
    /// <param name="sourceItemType">The owning item's type.</param>
    /// <param name="id">The TMDB id.</param>
    /// <returns>The page URL, or null.</returns>
    public static string? SourceUrl(string? sourceItemType, string id) => sourceItemType switch
    {
        "Person" => Base + "person/" + id,
        "BoxSet" => Base + "collection/" + id,
        "Studio" => Base + "company/" + id,
        "Keyword" => Base + "keyword/" + id,
        "List" => Base + "list/" + id,
        "Movie" => Base + "movie/" + id,
        "Series" => Base + "tv/" + id,
        _ => null
    };

    /// <summary>
    /// Whether a link is a page on themoviedb.org over https, which is what a title's "where to watch" link
    /// from TMDB's watch/providers answer is. A link that is anything else is not passed on to a browser.
    /// </summary>
    /// <param name="url">The link.</param>
    /// <returns><see langword="true"/> for an https URL on themoviedb.org.</returns>
    public static bool IsWatchUrl(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && (uri.Host.Equals("themoviedb.org", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".themoviedb.org", StringComparison.OrdinalIgnoreCase));
}
