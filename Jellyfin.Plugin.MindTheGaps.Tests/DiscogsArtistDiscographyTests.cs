using System.Linq;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Discogs;
using Jellyfin.Plugin.MindTheGaps.Services.Discogs;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class DiscogsArtistDiscographyTests
{
    private static DiscogsRelease Release(string title) => new() { Title = title };

    [Fact]
    public void ExcludingTitles_RemovesReleasesCoveredByTheExcludedTitles()
    {
        var releases = new[] { Release("The Wall"), Release("Animals"), Release("Wish You Were Here") };

        var kept = DiscogsArtistDiscography.ExcludingTitles(releases, new string?[] { "The Wall", "Wish You Were Here" });

        Assert.Equal(new[] { "Animals" }, kept.Select(r => r.Title));
    }

    [Fact]
    public void ExcludingTitles_MatchesIgnoringCaseAndPunctuation()
    {
        var releases = new[] { Release("Animals") };

        // TextKey.Normalize folds case and punctuation, so a differently-cased exclude title still matches.
        var kept = DiscogsArtistDiscography.ExcludingTitles(releases, new string?[] { "animals" });

        Assert.Empty(kept);
    }

    [Fact]
    public void ExcludingTitles_KeepsEverythingWhenNoTitleOverlaps()
    {
        var releases = new[] { Release("A"), Release("B") };

        var kept = DiscogsArtistDiscography.ExcludingTitles(releases, new string?[] { "C" });

        Assert.Equal(2, kept.Count);
    }
}
