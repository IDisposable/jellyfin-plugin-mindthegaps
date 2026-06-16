namespace Jellyfin.Plugin.MindTheGaps.Services.Tvdb;

/// <summary>
/// A TheTVDB <c>/login</c> response.
/// </summary>
public class TvdbLoginResponse
{
    /// <summary>Gets or sets the data payload carrying the token.</summary>
    public TvdbToken? Data { get; set; }
}
