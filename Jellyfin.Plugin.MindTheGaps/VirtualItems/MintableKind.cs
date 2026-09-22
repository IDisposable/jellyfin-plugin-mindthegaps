using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.MindTheGaps.VirtualItems;

/// <summary>
/// The per-kind lookup tables <see cref="VirtualItemMinter"/> mints against: which kinds can be minted at
/// all, what each is keyed on, and what to attach to it. Pure and standalone, like
/// <see cref="Gaps.StaleOwnerPruner"/>: every answer here depends only on the kind or the gap handed in,
/// never on the library, which is what makes it unit-testable without one (unlike the minter itself, which
/// mutates the library and so has no unit test of its own).
/// </summary>
internal static class MintableKind
{
    /// <summary>
    /// Gets the kinds that can be minted, each mapped to the provider id a gap must carry to be mintable as
    /// that kind. Served to the dashboard so a row's Mint button appears exactly when this would accept it,
    /// rather than the page keeping its own transcription of the kind switch this class keeps.
    /// </summary>
    public static IReadOnlyDictionary<string, string> All { get; } =
        Enum.GetValues<BaseItemKind>()
            .Select(kind => (Kind: kind, Provider: PrimaryProvider(kind)))
            .Where(pair => pair.Provider is not null)
            .ToDictionary(pair => pair.Kind.ToString(), pair => pair.Provider!, StringComparer.Ordinal);

    /// <summary>
    /// The provider-id key an item of this kind is minted under (its primary id), or null when the kind
    /// cannot be minted. Movies and series key on TMDB, albums on the MusicBrainz release-group, books on
    /// OpenLibrary (a plugin-provided key, not a core MetadataProvider enum member).
    /// </summary>
    /// <param name="kind">The item kind.</param>
    /// <returns>The primary provider key, or null when the kind cannot be minted.</returns>
    public static string? PrimaryProvider(BaseItemKind kind) => kind switch
    {
        BaseItemKind.Movie => ProviderIds.Tmdb,
        BaseItemKind.Series => ProviderIds.Tmdb,
        BaseItemKind.MusicAlbum => ProviderIds.MusicBrainzReleaseGroup,
        BaseItemKind.Book => ProviderIds.OpenLibrary,
        _ => null
    };

    /// <summary>
    /// The runtime entity type for a kind, for <c>GetNewItemId</c>'s deterministic id derivation.
    /// </summary>
    /// <param name="kind">The item kind.</param>
    /// <returns>The runtime entity type.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The kind cannot be minted.</exception>
    public static Type EntityType(BaseItemKind kind) => kind switch
    {
        BaseItemKind.Movie => typeof(Movie),
        BaseItemKind.Series => typeof(Series),
        BaseItemKind.MusicAlbum => typeof(MusicAlbum),
        BaseItemKind.Book => typeof(Book),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a mintable kind")
    };

    /// <summary>
    /// A fresh entity instance for a kind. Kept separate from <see cref="EntityType"/> so the typed
    /// properties are set on a concrete instance the host can persist.
    /// </summary>
    /// <param name="kind">The item kind.</param>
    /// <returns>A fresh entity instance.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The kind cannot be minted.</exception>
    public static BaseItem CreateEntity(BaseItemKind kind) => kind switch
    {
        BaseItemKind.Movie => new Movie(),
        BaseItemKind.Series => new Series(),
        BaseItemKind.MusicAlbum => new MusicAlbum(),
        BaseItemKind.Book => new Book(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a mintable kind")
    };

    /// <summary>
    /// The id-key prefix that scopes a minted item's deterministic id by kind, so two kinds that happened to
    /// share a primary id value cannot collide.
    /// </summary>
    /// <param name="kind">The item kind.</param>
    /// <returns>The id-key prefix.</returns>
    public static string IdKeyPrefix(BaseItemKind kind) => kind switch
    {
        BaseItemKind.Movie => "mindthegaps-virtual-movie-",
        BaseItemKind.Series => "mindthegaps-virtual-series-",
        BaseItemKind.MusicAlbum => "mindthegaps-virtual-album-",
        BaseItemKind.Book => "mindthegaps-virtual-book-",
        _ => "mindthegaps-virtual-"
    };

    /// <summary>
    /// The person to attach to a minted item, with the right role: the owned actor or director for a film or
    /// show (the filmography case), or the author for a book (the bibliography case, whose source name is
    /// the author). Null when there is no person to attach. A music album carries its artist a different
    /// way, via <c>AlbumArtists</c>.
    /// </summary>
    /// <param name="gap">The gap being minted.</param>
    /// <returns>The person's name and role, or null when there is no person to attach.</returns>
    public static (string Name, PersonKind Kind)? PersonAttachment(GapItem gap)
    {
        if (string.IsNullOrEmpty(gap.SourceItemName))
        {
            return null;
        }

        if (gap.TargetKind is BaseItemKind.Movie or BaseItemKind.Series
            && string.Equals(gap.SourceItemType, "Person", StringComparison.Ordinal))
        {
            return (gap.SourceItemName, PersonKind.Actor);
        }

        if (gap.TargetKind == BaseItemKind.Book
            && string.Equals(gap.SourceItemType, "Book", StringComparison.Ordinal))
        {
            return (gap.SourceItemName, PersonKind.Author);
        }

        return null;
    }
}
