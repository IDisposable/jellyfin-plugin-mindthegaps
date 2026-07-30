namespace Jellyfin.Plugin.MindTheGaps.Services.JustWatch;

/// <summary>
/// The GraphQL envelope. JustWatch answers an expired or missing token with HTTP 200, a null <c>data</c>
/// field, and an <c>errors</c> array, so a caller checks the payload rather than the status code.
/// </summary>
internal sealed class JustWatchGraphResponse
{
    /// <summary>
    /// Gets or sets the payload, null when the query failed.
    /// </summary>
    public JustWatchListData? Data { get; set; }
}
