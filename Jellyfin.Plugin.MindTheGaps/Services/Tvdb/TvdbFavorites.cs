using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.Services.Tvdb;

/// <summary>
/// The account's favourites, as bare ids per kind. Every field is null when the account has none of that
/// kind, so each is optional rather than an empty array.
/// </summary>
internal sealed class TvdbFavorites
{
    /// <summary>Gets or sets the favourite series ids.</summary>
    public IReadOnlyList<long>? Series { get; set; }

    /// <summary>Gets or sets the favourite movie ids.</summary>
    public IReadOnlyList<long>? Movies { get; set; }

    /// <summary>Gets or sets the favourite list ids.</summary>
    public IReadOnlyList<long>? Lists { get; set; }
}
