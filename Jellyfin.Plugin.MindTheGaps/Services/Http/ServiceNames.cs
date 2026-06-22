namespace Jellyfin.Plugin.MindTheGaps.Services.Http;

/// <summary>
/// The canonical name of each external service, defined once and used as the single key across the HTTP
/// stack: the <see cref="HttpRetry"/> log/service argument, the <see cref="ServicePacer"/> interval, and the
/// <see cref="ServiceCircuit"/> state. The gap sources reference the same constants when they skip a service
/// that has been given up on, so a name is never spelled as a bare literal in two places. These are HTTP
/// service names, distinct from the provider-id keys an owned item carries (for example
/// <c>DiscogsLabelMapper.DiscogsProvider</c>), even where the text happens to match.
/// </summary>
public static class ServiceNames
{
    /// <summary>TheMovieDb (the availability fetch that goes through <see cref="HttpRetry"/>).</summary>
    public const string Tmdb = "TMDB";

    /// <summary>Trakt.</summary>
    public const string Trakt = "Trakt";

    /// <summary>TVmaze.</summary>
    public const string TvMaze = "TVmaze";

    /// <summary>TheTVDB.</summary>
    public const string Tvdb = "TheTVDB";

    /// <summary>MusicBrainz.</summary>
    public const string MusicBrainz = "MusicBrainz";

    /// <summary>OpenLibrary.</summary>
    public const string OpenLibrary = "OpenLibrary";

    /// <summary>Discogs.</summary>
    public const string Discogs = "Discogs";

    /// <summary>MDBList.</summary>
    public const string MdbList = "MDBList";
}
