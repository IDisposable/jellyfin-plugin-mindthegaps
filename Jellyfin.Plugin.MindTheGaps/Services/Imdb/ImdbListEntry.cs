namespace Jellyfin.Plugin.MindTheGaps.Services.Imdb;

/// <summary>
/// One thing on an IMDb list. The list item is a union (a Title, a Name, or an Image), and the query spreads
/// the Title and Name fragments onto it, so one type carries both shapes and <see cref="TypeName"/> says which
/// arrived. An Image entry deserializes with everything null and is dropped.
/// </summary>
internal sealed class ImdbListEntry
{
    /// <summary>
    /// Gets or sets the GraphQL type name, "Title" or "Name".
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("__typename")]
    public string? TypeName { get; set; }

    /// <summary>
    /// Gets or sets the IMDb id: "tt0133093" for a title, "nm0000229" for a person.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the display title, on a Title entry.
    /// </summary>
    public ImdbText? TitleText { get; set; }

    /// <summary>
    /// Gets or sets the display name, on a Name entry.
    /// </summary>
    public ImdbText? NameText { get; set; }

    /// <summary>
    /// Gets or sets the release year, on a Title entry.
    /// </summary>
    public ImdbYear? ReleaseYear { get; set; }

    /// <summary>
    /// Gets or sets the title kind, on a Title entry.
    /// </summary>
    public ImdbTitleType? TitleType { get; set; }

    /// <summary>
    /// Gets or sets the poster or headshot.
    /// </summary>
    public ImdbImage? PrimaryImage { get; set; }

    /// <summary>
    /// Gets a value indicating whether this entry is a title.
    /// </summary>
    public bool IsTitle => TitleText?.Value is { Length: > 0 };

    /// <summary>
    /// Gets a value indicating whether this entry is a person.
    /// </summary>
    public bool IsName => NameText?.Value is { Length: > 0 };
}
