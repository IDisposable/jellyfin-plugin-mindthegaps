namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// A single streaming-availability offer for a gap title.
/// </summary>
public class AvailabilityOffer
{
    /// <summary>
    /// Gets or sets the provider/service name (e.g. "Netflix").
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the monetization type (e.g. flatrate, rent, buy, free, ads).
    /// </summary>
    public string? MonetizationType { get; set; }

    /// <summary>
    /// Gets or sets the presentation/quality (e.g. SD, HD, 4K).
    /// </summary>
    public string? Quality { get; set; }

    /// <summary>
    /// Gets or sets a deep-link/web URL to the offer.
    /// </summary>
    public string? Url { get; set; }
}
