using System.Globalization;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Builds the set key that identifies which curated set a <see cref="GapSourceKeys.Curated"/> gap came from,
/// so the id reads "curated:company:41077:12345". Part of the gap id, so part of the persistence contract
/// (ADR-0008); pinned by <c>GapIdPrefixTests</c>.
/// </summary>
internal static class CuratedSetKeys
{
    /// <summary>
    /// Builds the set key for a TMDB company (studio).
    /// </summary>
    /// <param name="companyId">The TMDB company id.</param>
    /// <returns>The set key.</returns>
    public static string Company(int companyId)
        => string.Create(CultureInfo.InvariantCulture, $"company:{companyId}");

    /// <summary>
    /// Builds the set key for a TMDB keyword.
    /// </summary>
    /// <param name="keywordId">The TMDB keyword id.</param>
    /// <returns>The set key.</returns>
    public static string Keyword(int keywordId)
        => string.Create(CultureInfo.InvariantCulture, $"keyword:{keywordId}");

    /// <summary>
    /// Builds the set key for a TMDB list.
    /// </summary>
    /// <param name="listId">The TMDB list id.</param>
    /// <returns>The set key.</returns>
    public static string List(int listId)
        => string.Create(CultureInfo.InvariantCulture, $"list:{listId}");
}
