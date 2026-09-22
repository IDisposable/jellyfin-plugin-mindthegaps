using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.VirtualItems;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// MintableKind was split out of VirtualItemMinter specifically so these per-kind lookup tables could be
// exercised without a library; VirtualItemMinter itself has none, for the same reason the live HTTP
// clients do not (it mutates the library).
public class MintableKindTests
{
    [Fact]
    public void PrimaryProvider_MatchesTheMintableKindsTable()
    {
        Assert.Equal(ProviderIds.Tmdb, MintableKind.PrimaryProvider(BaseItemKind.Movie));
        Assert.Equal(ProviderIds.Tmdb, MintableKind.PrimaryProvider(BaseItemKind.Series));
        Assert.Equal(ProviderIds.MusicBrainzReleaseGroup, MintableKind.PrimaryProvider(BaseItemKind.MusicAlbum));
        Assert.Equal(ProviderIds.OpenLibrary, MintableKind.PrimaryProvider(BaseItemKind.Book));
    }

    [Fact]
    public void PrimaryProvider_IsNullForAnUnmintableKind()
        => Assert.Null(MintableKind.PrimaryProvider(BaseItemKind.Episode));

    [Fact]
    public void All_ListsExactlyTheFourMintableKinds()
    {
        Assert.Equal(4, MintableKind.All.Count);
        Assert.Equal(ProviderIds.Tmdb, MintableKind.All["Movie"]);
        Assert.Equal(ProviderIds.Tmdb, MintableKind.All["Series"]);
        Assert.Equal(ProviderIds.MusicBrainzReleaseGroup, MintableKind.All["MusicAlbum"]);
        Assert.Equal(ProviderIds.OpenLibrary, MintableKind.All["Book"]);
    }

    [Fact]
    public void IdKeyPrefix_IsDistinctPerKind_SoSharedPrimaryIdsCannotCollide()
    {
        var prefixes = new[]
        {
            MintableKind.IdKeyPrefix(BaseItemKind.Movie),
            MintableKind.IdKeyPrefix(BaseItemKind.Series),
            MintableKind.IdKeyPrefix(BaseItemKind.MusicAlbum),
            MintableKind.IdKeyPrefix(BaseItemKind.Book)
        };

        Assert.Equal(prefixes.Length, new System.Collections.Generic.HashSet<string>(prefixes).Count);
    }

    [Fact]
    public void PersonAttachment_AttachesAnActorForAFilmographyGap()
    {
        var gap = new GapItem { TargetKind = BaseItemKind.Movie, SourceItemType = "Person", SourceItemName = "Some Actor" };

        var person = MintableKind.PersonAttachment(gap);

        Assert.Equal(("Some Actor", PersonKind.Actor), person);
    }

    [Fact]
    public void PersonAttachment_AttachesAnAuthorForABibliographyGap()
    {
        var gap = new GapItem { TargetKind = BaseItemKind.Book, SourceItemType = "Book", SourceItemName = "Some Author" };

        var person = MintableKind.PersonAttachment(gap);

        Assert.Equal(("Some Author", PersonKind.Author), person);
    }

    [Fact]
    public void PersonAttachment_IsNullForAnAlbum_WhichCarriesItsArtistViaAlbumArtistsInstead()
    {
        var gap = new GapItem { TargetKind = BaseItemKind.MusicAlbum, SourceItemType = "MusicArtist", SourceItemName = "Some Artist" };

        Assert.Null(MintableKind.PersonAttachment(gap));
    }

    [Fact]
    public void PersonAttachment_IsNullWhenThereIsNoSourceItemName()
    {
        var gap = new GapItem { TargetKind = BaseItemKind.Movie, SourceItemType = "Person", SourceItemName = null };

        Assert.Null(MintableKind.PersonAttachment(gap));
    }
}
