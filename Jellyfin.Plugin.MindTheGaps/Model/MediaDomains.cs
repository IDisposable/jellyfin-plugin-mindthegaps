using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// Which <see cref="MediaDomain"/> members a source actually produces gaps for, in the order the dashboard
/// offers them. <see cref="MediaDomain"/> is the model's vocabulary and may name a domain nothing implements
/// yet; this is the subset worth showing, so the Type selector does not offer a domain that can only ever be
/// empty. Adding an enum member without listing it here is caught by a test rather than passing silently.
/// </summary>
public static class MediaDomains
{
    /// <summary>
    /// Gets the domains with a working source, in display order.
    /// </summary>
    public static IReadOnlyList<MediaDomain> Implemented { get; } =
    [
        MediaDomain.Movies,
        MediaDomain.Shows,
        MediaDomain.Music,
        MediaDomain.Books
    ];

    /// <summary>
    /// Gets the domains named by <see cref="MediaDomain"/> that no source fills yet. Listed explicitly so
    /// that the pair covers the enum: a new member has to be classified one way or the other.
    /// </summary>
    public static IReadOnlyList<MediaDomain> NotYetImplemented { get; } = [MediaDomain.MusicVideos];
}
