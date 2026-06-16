namespace Jellyfin.Plugin.MindTheGaps.Services.Tvdb;

/// <summary>
/// The data payload of a TheTVDB login response.
/// </summary>
public class TvdbToken
{
    /// <summary>Gets or sets the bearer token.</summary>
    public string? Token { get; set; }
}
