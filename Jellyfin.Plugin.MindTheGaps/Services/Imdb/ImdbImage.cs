namespace Jellyfin.Plugin.MindTheGaps.Services.Imdb;

/// <summary>
/// A title's primary image.
/// </summary>
internal sealed class ImdbImage
{
    /// <summary>
    /// Gets or sets the absolute image URL.
    /// </summary>
    public string? Url { get; set; }
}
