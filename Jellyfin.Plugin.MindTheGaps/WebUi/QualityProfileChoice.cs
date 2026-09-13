namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// One quality profile an acquisition target offers, as the person page lets an administrator pick it.
/// </summary>
public sealed class QualityProfileChoice
{
    /// <summary>
    /// Gets or sets the profile id in Radarr/Sonarr.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the profile name.
    /// </summary>
    public string Name { get; set; } = string.Empty;
}
