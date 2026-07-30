namespace Jellyfin.Plugin.MindTheGaps.Services.Imdb;

/// <summary>
/// What a list holds. IMDb types its lists, so a source can tell a list of films from a list of people
/// without inspecting the entries.
/// </summary>
internal sealed class ImdbListType
{
    /// <summary>
    /// Gets or sets the type id: "TITLES", "PEOPLE", or "IMAGES".
    /// </summary>
    public string? Id { get; set; }
}
