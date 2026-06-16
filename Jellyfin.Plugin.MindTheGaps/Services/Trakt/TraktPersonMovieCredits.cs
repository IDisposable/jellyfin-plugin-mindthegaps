using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.Services.Trakt;

/// <summary>
/// A person's movie credits on Trakt.
/// </summary>
public class TraktPersonMovieCredits
{
    /// <summary>Gets or sets the acting credits.</summary>
    public IReadOnlyList<TraktMovieCastCredit>? Cast { get; set; }

    /// <summary>Gets or sets the crew credits.</summary>
    public TraktCrew? Crew { get; set; }
}
