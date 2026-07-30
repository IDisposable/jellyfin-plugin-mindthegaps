namespace Jellyfin.Plugin.MindTheGaps.Services.Tvdb;

/// <summary>
/// The envelope TheTVDB wraps the account's favourites in.
/// </summary>
internal sealed class TvdbFavoritesResponse
{
    /// <summary>Gets or sets the favourites.</summary>
    public TvdbFavorites? Data { get; set; }
}
