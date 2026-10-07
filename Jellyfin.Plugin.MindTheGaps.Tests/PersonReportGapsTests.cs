using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.WebUi;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// Which report gaps the person page folds in alongside its live TMDB lookup.
public class PersonReportGapsTests
{
    private const string PersonKey = "0123456789abcdef0123456789abcdef";
    private static readonly string _imdbOwner = PersonReportGaps.ImdbOwner("nm0000123")!;

    private static GapItem Gap(
        string? owner = PersonKey,
        GapPattern pattern = GapPattern.CreatorWorks,
        string sourceType = SourceItemTypes.Person,
        bool adhoc = false)
        => new()
        {
            Id = "filmography:movie:1",
            Name = "Some Title",
            Pattern = pattern,
            TargetKind = BaseItemKind.Movie,
            SourceItemId = owner,
            SourceItemType = sourceType,
            Adhoc = adhoc
        };

    [Fact]
    public void TakesAGapTheLibraryPersonOwns()
        => Assert.True(PersonReportGaps.IsForPerson(Gap(), PersonKey, null));

    [Fact]
    public void MatchesTheOwnerIdWhateverItsCase()
        => Assert.True(PersonReportGaps.IsForPerson(Gap(owner: PersonKey.ToUpperInvariant()), PersonKey, null));

    // A person followed from an IMDb people list owns their gaps by IMDb id, not by their library id.
    [Fact]
    public void TakesAGapFromAnImdbPeopleListByTheSameImdbId()
    {
        Assert.Equal(GapSourceKeys.ImdbPerson.Owner("nm0000123"), _imdbOwner);
        Assert.True(PersonReportGaps.IsForPerson(Gap(owner: _imdbOwner), PersonKey, _imdbOwner));
        Assert.False(PersonReportGaps.IsForPerson(Gap(owner: _imdbOwner), PersonKey, null));
    }

    [Fact]
    public void APersonWithNoImdbIdHasNoImdbOwner()
    {
        Assert.Null(PersonReportGaps.ImdbOwner(null));
        Assert.Null(PersonReportGaps.ImdbOwner(" "));
    }

    [Fact]
    public void LeavesOutAnotherPersonsGaps()
        => Assert.False(PersonReportGaps.IsForPerson(Gap(owner: "fedcba9876543210fedcba9876543210"), PersonKey, _imdbOwner));

    // A recommendation made from a movie the person is in is not the person's work, nor is an artist's.
    [Theory]
    [InlineData(GapPattern.Recommendation, SourceItemTypes.Person)]
    [InlineData(GapPattern.CreatorWorks, SourceItemTypes.MusicArtist)]
    public void LeavesOutWhatIsNotAPersonsCreatorWork(GapPattern pattern, string sourceType)
        => Assert.False(PersonReportGaps.IsForPerson(Gap(pattern: pattern, sourceType: sourceType), PersonKey, null));

    // An Explore pass is a one-off look the administrator asked for, not what the scan found.
    [Fact]
    public void LeavesOutAdhocGaps()
        => Assert.False(PersonReportGaps.IsForPerson(Gap(adhoc: true), PersonKey, null));
}
