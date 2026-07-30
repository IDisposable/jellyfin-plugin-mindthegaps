namespace Jellyfin.Plugin.MindTheGaps.Services.Tvdb;

/// <summary>
/// The envelope TheTVDB wraps the account's favorites in.
/// </summary>
internal sealed class TvdbFavoritesResponse
{
    /// <summary>Gets or sets the favorites.</summary>
    public TvdbFavorites? Data { get; set; }
}
