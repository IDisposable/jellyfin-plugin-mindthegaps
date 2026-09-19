using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// One streaming service a gap is on, without the deeplink or quality an <see cref="AvailabilityOffer"/>
/// carries. The report list needs only this much to filter by provider and to show a service icon, and the
/// deeplinks are the bulk of an offer.
/// </summary>
public class AvailabilityBadge
{
    /// <summary>
    /// Gets or sets the provider/service name (for example "Netflix").
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets how the title is offered (flatrate, rent, buy, free, ads), when known.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MonetizationType { get; set; }

    /// <summary>
    /// Gets or sets the URL of the service logo, when known, for the row's service icons.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LogoUrl { get; set; }
}
