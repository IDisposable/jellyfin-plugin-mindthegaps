using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.MindTheGaps;

/// <summary>
/// The provider-id keys (the keys of an item's <c>ProviderIds</c> map) as a single set of strings, so a key
/// is written in one place instead of being re-derived everywhere. The providers core knows are taken from
/// the <see cref="MetadataProvider"/> enum so they stay in step with it; the plugin-only providers (TVmaze,
/// Discogs, OpenLibrary), which are not in the core enum, are plain literals. Lives in the root namespace so
/// it is in scope everywhere without a using.
/// </summary>
internal static class ProviderIds
{
    /// <summary>TVmaze (a plugin provider, not in the core enum).</summary>
    public const string TVmaze = "TVmaze";

    /// <summary>Discogs (a plugin provider, not in the core enum).</summary>
    public const string Discogs = "Discogs";

    /// <summary>OpenLibrary (a plugin provider, not in the core enum).</summary>
    public const string OpenLibrary = "OpenLibrary";

    /// <summary>JustWatch: a title's page path, as the JustWatch plugin stamps it (not in the core enum).</summary>
    public const string JustWatch = "JustWatch";

    /// <summary>Trakt: a person's id on a filmography source (not in the core enum).</summary>
    public const string Trakt = "Trakt";

    /// <summary>TheMovieDb.</summary>
    public static readonly string Tmdb = MetadataProvider.Tmdb.ToString();

    /// <summary>TheTVDB.</summary>
    public static readonly string Tvdb = MetadataProvider.Tvdb.ToString();

    /// <summary>IMDb.</summary>
    public static readonly string Imdb = MetadataProvider.Imdb.ToString();

    /// <summary>MusicBrainz release group.</summary>
    public static readonly string MusicBrainzReleaseGroup = MetadataProvider.MusicBrainzReleaseGroup.ToString();

    /// <summary>MusicBrainz artist.</summary>
    public static readonly string MusicBrainzArtist = MetadataProvider.MusicBrainzArtist.ToString();
}
