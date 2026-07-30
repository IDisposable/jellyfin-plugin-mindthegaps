namespace Jellyfin.Plugin.MindTheGaps.Services.Imdb;

/// <summary>
/// A localizable string in IMDb's GraphQL schema. A list's name carries it as <c>originalText</c> and a
/// title's as <c>text</c>, so both spellings live on one type rather than two near-identical ones.
/// </summary>
internal sealed class ImdbText
{
    /// <summary>
    /// Gets or sets the text, as a title carries it.
    /// </summary>
    public string? Text { get; set; }

    /// <summary>
    /// Gets or sets the text, as a list name carries it.
    /// </summary>
    public string? OriginalText { get; set; }

    /// <summary>
    /// Gets the value under whichever spelling the response used.
    /// </summary>
    public string? Value => Text ?? OriginalText;
}
