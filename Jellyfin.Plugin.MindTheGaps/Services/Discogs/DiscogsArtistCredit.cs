using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Services.Discogs;

/// <summary>
/// One credited artist on a wantlist entry. The label and artist release endpoints flatten the credit to a
/// single "artist" string; the wantlist gives the full array instead.
/// </summary>
internal sealed class DiscogsArtistCredit
{
    /// <summary>Gets or sets the artist name, which may carry a disambiguating suffix ("Rainbow (11)").</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Gets or sets the "artist name variation", the spelling used on that release when it differs.</summary>
    [JsonPropertyName("anv")]
    public string? NameVariation { get; set; }

    /// <summary>Gets or sets how this credit joins the next one ("&amp;", "Feat.", ...).</summary>
    [JsonPropertyName("join")]
    public string? Join { get; set; }

    /// <summary>Gets or sets the Discogs artist id.</summary>
    [JsonPropertyName("id")]
    public long Id { get; set; }
}
