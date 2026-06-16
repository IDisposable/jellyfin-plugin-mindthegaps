using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.Services.Trakt;

/// <summary>
/// The crew block, keyed by department.
/// </summary>
public class TraktCrew
{
    /// <summary>Gets or sets directing credits.</summary>
    public IReadOnlyList<TraktMovieCrewCredit>? Directing { get; set; }

    /// <summary>Gets or sets writing credits.</summary>
    public IReadOnlyList<TraktMovieCrewCredit>? Writing { get; set; }
}
