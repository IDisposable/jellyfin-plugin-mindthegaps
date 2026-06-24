using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.MindTheGaps.Services.Http;

namespace Jellyfin.Plugin.MindTheGaps.Services;

/// <summary>
/// The catalog of external providers and their name kinship (id key, Jellyfin fetcher name, display label,
/// service label), built over the id keys in <see cref="ProviderIds"/>. The single place that knows, for
/// example, that "TheMovieDb", "Tmdb", and "TMDB" are the same provider, so the rest of the code resolves a
/// name through here instead of re-listing the aliases.
/// </summary>
public static class KnownProviders
{
    /// <summary>Gets TheMovieDb.</summary>
    public static KnownProvider Tmdb { get; } = new(ProviderIds.Tmdb, "TheMovieDb", "TheMovieDb", ServiceNames.Tmdb);

    /// <summary>Gets TheTVDB.</summary>
    public static KnownProvider Tvdb { get; } = new(ProviderIds.Tvdb, "TheTVDB", "TheTVDB", ServiceNames.Tvdb);

    /// <summary>Gets IMDb.</summary>
    public static KnownProvider Imdb { get; } = new(ProviderIds.Imdb, "Imdb", "IMDb", "IMDb");

    /// <summary>Gets TVmaze.</summary>
    public static KnownProvider TvMaze { get; } = new(ProviderIds.TVmaze, "TVmaze", "TVmaze", ServiceNames.TvMaze);

    /// <summary>Gets Discogs.</summary>
    public static KnownProvider Discogs { get; } = new(ProviderIds.Discogs, "Discogs", "Discogs", ServiceNames.Discogs);

    /// <summary>Gets OpenLibrary.</summary>
    public static KnownProvider OpenLibrary { get; } = new(ProviderIds.OpenLibrary, "OpenLibrary", "OpenLibrary", ServiceNames.OpenLibrary);

    /// <summary>Gets MusicBrainz, keyed by release group (an owned album's id).</summary>
    public static KnownProvider MusicBrainzReleaseGroup { get; } = new(ProviderIds.MusicBrainzReleaseGroup, "MusicBrainz", "MusicBrainz", ServiceNames.MusicBrainz);

    /// <summary>Gets MusicBrainz, keyed by artist.</summary>
    public static KnownProvider MusicBrainzArtist { get; } = new(ProviderIds.MusicBrainzArtist, "MusicBrainz", "MusicBrainz", ServiceNames.MusicBrainz);

    /// <summary>Gets every cataloged provider.</summary>
    public static IReadOnlyList<KnownProvider> All { get; } = new[]
    {
        Tmdb, Tvdb, Imdb, TvMaze, Discogs, OpenLibrary, MusicBrainzReleaseGroup, MusicBrainzArtist
    };

    /// <summary>
    /// Finds the cataloged provider that goes by the given name, matching any of its names, or null when the
    /// name is not one the catalog knows.
    /// </summary>
    /// <param name="name">A provider name from any context (an id key, a fetcher name, a label).</param>
    /// <returns>The matching provider, or null.</returns>
    public static KnownProvider? ForName(string? name) => name is null ? null : All.FirstOrDefault(p => p.Matches(name));
}
