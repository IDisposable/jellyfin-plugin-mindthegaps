using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Model;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// A source that can re-run for one owning library item on its own, so the report can re-check a single
/// collection, artist, or author the way <see cref="ISeriesContentSource"/> re-checks a single series
/// (ADR-0013). Unlike the report's verify action, which only drops what the library has since been given,
/// a re-check asks the provider again and so also picks up members added to the set since the last scan.
/// </summary>
/// <remarks>
/// Implemented by the sources whose per-entity step is a bounded, cached provider call: the TMDB
/// collections, the music artist walks, and the book bibliography. The filmography and recommendation
/// sources stay out deliberately, since re-running one of those needs a library-wide ownership index.
/// </remarks>
internal interface ISetContentSource
{
    /// <summary>
    /// Gets the prefix this source stamps on the gap ids it produces, so a re-check swaps out exactly its
    /// own gaps for the owning item and leaves another source's alone (see
    /// <see cref="GapStore.ReplaceSourceGaps"/>).
    /// </summary>
    string GapIdPrefix { get; }

    /// <summary>
    /// Determines, without any network call, whether this source is the one that produces gaps for the given
    /// owning library item (a BoxSet for the collections source, a MusicArtist for the music sources, ...).
    /// </summary>
    /// <param name="owner">The owning library item the report is re-checking.</param>
    /// <returns><see langword="true"/> when this source should be re-run for that item.</returns>
    bool Claims(BaseItem owner);

    /// <summary>
    /// Re-checks one owning item for what it is still missing.
    /// </summary>
    /// <param name="owner">The owning library item.</param>
    /// <param name="context">The scan context (config and ownership).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// That item's current gaps, empty when it is genuinely missing nothing, or <see langword="null"/> when
    /// the answer could not be determined (the provider failed, or the item carries no id to resolve by).
    /// The distinction matters: a re-check replaces the item's gaps, so returning an empty list for a failed
    /// lookup would delete a collection's real gaps on a transient outage. Null means "leave them alone".
    /// </returns>
    Task<IReadOnlyList<GapItem>?> CheckOneAsync(BaseItem owner, GapScanContext context, CancellationToken cancellationToken);
}
