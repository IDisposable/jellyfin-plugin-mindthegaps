namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Provider-agnostic safety caps shared across sources. Caps whose value is dictated by a specific
/// provider's API budget (e.g. how many people a Trakt vs TMDB scan can afford) live with that
/// source, not here.
/// </summary>
internal static class GapScanLimits
{
    /// <summary>
    /// Maximum credits emitted per person (shared by the filmography sources).
    /// </summary>
    public const int MaxCreditsPerPerson = 100;

    /// <summary>
    /// Maximum owned titles used as recommendation seeds (per media type).
    /// </summary>
    public const int MaxRecommendationSeeds = 200;
}
