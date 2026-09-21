using System;
using System.Collections.Generic;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// Marks which cards are already on a user's want-to-watch list.
/// </summary>
internal static class WantedMarker
{
    /// <summary>
    /// Sets <see cref="MissingTitle.OnList"/> on every card whose title is among the wanted keys.
    /// </summary>
    /// <param name="titles">The cards.</param>
    /// <param name="wanted">The identity keys of the titles on the list (<see cref="TodoStore.WantedKeys"/>).</param>
    public static void Mark(IEnumerable<MissingTitle> titles, IReadOnlySet<string> wanted)
    {
        ArgumentNullException.ThrowIfNull(titles);
        ArgumentNullException.ThrowIfNull(wanted);

        foreach (var title in titles)
        {
            var kind = string.Equals(title.Kind, "Series", StringComparison.Ordinal) ? BaseItemKind.Series : BaseItemKind.Movie;
            title.OnList = wanted.Contains(OwnershipIndex.MakeKey(kind, ProviderIds.Tmdb, title.TmdbId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
    }
}
