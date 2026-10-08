using System;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// Whether a user's parental rating limit allows a title the library does not hold, decided by core itself: a
/// throwaway movie or series carrying the title's certification as its official rating is asked
/// <see cref="BaseItem.IsParentalAllowed"/>, so the score and sub-score comparison, the rating table for each
/// country, and the user's own "block items with no rating" choice for movies and for series are exactly what
/// the library applies to the titles it holds. The throwaway's country is the one the certification came from,
/// which a library item would otherwise take from its library and the server: on 12.0 that names the rating
/// table core reads it with, and 10.11 goes by the rating's own country prefix, as it does for a library item.
/// Allowed tags are not checked: a title from TMDB carries none of the library's.
/// </summary>
internal static class RatingGate
{
    /// <summary>
    /// Determines whether the user may be shown the title.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="isSeries">Whether the title is a series rather than a movie, which decides which of the
    /// user's "block items with no rating" choices applies.</param>
    /// <param name="certification">The title's certification, or null when it has none.</param>
    /// <returns><see langword="true"/> when the user's limit allows it.</returns>
    public static bool Allows(User user, bool isSeries, TmdbCertification? certification)
    {
        ArgumentNullException.ThrowIfNull(user);

        BaseItem probe = isSeries ? new Series() : new Movie();
        probe.OfficialRating = certification?.Rating;
        probe.PreferredMetadataCountryCode = certification?.Country ?? "US";
        return probe.IsParentalAllowed(user, true);
    }
}
