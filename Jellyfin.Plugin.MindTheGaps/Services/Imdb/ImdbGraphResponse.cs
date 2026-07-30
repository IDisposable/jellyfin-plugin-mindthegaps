namespace Jellyfin.Plugin.MindTheGaps.Services.Imdb;

/// <summary>
/// The GraphQL envelope. IMDb answers a partly-failed query with HTTP 200, a null <c>data</c> field, and an
/// <c>errors</c> array, so a caller checks the payload rather than the status code.
/// </summary>
internal sealed class ImdbGraphResponse
{
    /// <summary>
    /// Gets or sets the payload, null when the query failed.
    /// </summary>
    public ImdbListData? Data { get; set; }
}
