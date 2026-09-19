using System.Text.Json;

namespace Jellyfin.Plugin.MindTheGaps.Services.Availability;

/// <summary>
/// How TMDB's watch/providers responses are read: snake_case names onto the plugin's own models.
/// </summary>
internal static class TmdbWatchJson
{
    /// <summary>
    /// Gets the serializer options for a watch/providers response or provider catalog.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };
}
