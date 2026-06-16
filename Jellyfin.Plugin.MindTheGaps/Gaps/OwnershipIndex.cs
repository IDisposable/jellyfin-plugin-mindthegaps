using System.Collections.Generic;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// A domain-agnostic snapshot of what the library owns, keyed by (item kind, provider, id).
/// Any gap source asks "do I own this?" the same way regardless of media domain.
/// </summary>
public sealed class OwnershipIndex
{
    private readonly IReadOnlyDictionary<string, BaseItem> _byKey;

    /// <summary>
    /// Initializes a new instance of the <see cref="OwnershipIndex"/> class.
    /// </summary>
    /// <param name="byKey">Owned items keyed via <see cref="MakeKey"/>.</param>
    public OwnershipIndex(IReadOnlyDictionary<string, BaseItem> byKey)
    {
        _byKey = byKey;
    }

    /// <summary>
    /// Gets the number of indexed provider-id keys.
    /// </summary>
    public int Count => _byKey.Count;

    /// <summary>
    /// Builds the composite key for a (kind, provider, id) triple.
    /// </summary>
    /// <param name="kind">The item kind.</param>
    /// <param name="provider">The provider name (e.g. "Tmdb").</param>
    /// <param name="id">The provider id value.</param>
    /// <returns>A normalized key.</returns>
    public static string MakeKey(BaseItemKind kind, string provider, string id)
        => string.Concat(kind.ToString(), "|", provider, "|", id).ToLowerInvariant();

    /// <summary>
    /// Determines whether an item of the given kind with the given provider id is owned.
    /// </summary>
    /// <param name="kind">The item kind.</param>
    /// <param name="provider">The provider name.</param>
    /// <param name="id">The provider id value.</param>
    /// <returns><see langword="true"/> if owned.</returns>
    public bool Owns(BaseItemKind kind, string provider, string id)
        => _byKey.ContainsKey(MakeKey(kind, provider, id));

    /// <summary>
    /// Determines whether an item of the given kind is owned under ANY of the supplied provider ids.
    /// </summary>
    /// <param name="kind">The item kind.</param>
    /// <param name="providerIds">Candidate provider ids (e.g. {"Tmdb":"603","Imdb":"tt0133093"}).</param>
    /// <returns><see langword="true"/> if any id matches an owned item.</returns>
    public bool OwnsAny(BaseItemKind kind, IReadOnlyDictionary<string, string> providerIds)
    {
        foreach (var providerId in providerIds)
        {
            if (!string.IsNullOrEmpty(providerId.Value) && Owns(kind, providerId.Key, providerId.Value))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Finds the owned item matching a (kind, provider, id), if any.
    /// </summary>
    /// <param name="kind">The item kind.</param>
    /// <param name="provider">The provider name.</param>
    /// <param name="id">The provider id value.</param>
    /// <returns>The owned item, or <see langword="null"/>.</returns>
    public BaseItem? Find(BaseItemKind kind, string provider, string id)
        => _byKey.TryGetValue(MakeKey(kind, provider, id), out var item) ? item : null;
}
