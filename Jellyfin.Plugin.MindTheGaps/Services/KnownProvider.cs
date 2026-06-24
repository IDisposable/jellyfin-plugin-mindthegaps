using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.Services;

/// <summary>
/// One external provider and the several names it goes by, so the kinship between (for example)
/// "TheMovieDb" the Jellyfin metadata-fetcher name, "Tmdb" the provider-id key, and "TMDB" the service
/// label lives in one place instead of being re-correlated wherever a name from one context must be
/// matched to a name from another.
/// </summary>
public sealed class KnownProvider
{
    private readonly HashSet<string> _aliases;

    internal KnownProvider(string idKey, string fetcherName, string label, string serviceName)
    {
        IdKey = idKey;
        FetcherName = fetcherName;
        Label = label;
        ServiceName = serviceName;
        _aliases = new HashSet<string>(new[] { idKey, fetcherName, label, serviceName }, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Gets the provider-id key: the ProviderIds dictionary key and the Jellyfin MetadataProvider name, for example "Tmdb".</summary>
    public string IdKey { get; }

    /// <summary>Gets the Jellyfin metadata-fetcher name, as it appears in a library's MetadataFetcherOrder, for example "TheMovieDb".</summary>
    public string FetcherName { get; }

    /// <summary>Gets the human display label, for example "TheMovieDb".</summary>
    public string Label { get; }

    /// <summary>Gets the short service label: the circuit and link-badge name, for example "TMDB".</summary>
    public string ServiceName { get; }

    /// <summary>
    /// Determines whether the given name is one this provider goes by (its id key, fetcher name, label, or
    /// service label), case-insensitively.
    /// </summary>
    /// <param name="name">A provider name from any context.</param>
    /// <returns><see langword="true"/> when the name refers to this provider.</returns>
    public bool Matches(string? name) => name is not null && _aliases.Contains(name);
}
