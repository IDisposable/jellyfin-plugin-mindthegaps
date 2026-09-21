using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class GapRowProjectorTests
{
    private static readonly ExternalLink[] _studioLinks = [new("TMDB", "https://www.themoviedb.org/company/1")];

    // What a row deliberately leaves to GapDetail (or replaces with a compact form), so a property added to
    // GapItem later has to be placed on the list or listed here on purpose rather than dropped by accident.
    private static readonly HashSet<string> _notOnRow = new(System.StringComparer.Ordinal)
    {
        nameof(GapItem.Pattern),
        nameof(GapItem.Domain),
        nameof(GapItem.TargetKind),
        nameof(GapItem.TargetKindName),
        nameof(GapItem.Overview),
        nameof(GapItem.Links),
        nameof(GapItem.SourceLinks),
        nameof(GapItem.Adhoc)
    };

    private static GapItem Gap(string id) => new()
    {
        Id = id,
        Pattern = GapPattern.SetCompletion,
        Domain = MediaDomain.Movies,
        TargetKind = BaseItemKind.Movie,
        Name = "Heat",
        Year = 1995,
        ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "949" },
        SourceItemName = "Studio",
        SourceLinks = _studioLinks
    };

    [Fact]
    public void Project_LeavesOffWhatOnlyAnOpenedRowShows()
    {
        var gap = Gap("g1");
        gap.Overview = "A long overview.";
        gap.Links = [new ExternalLink("TMDB", "https://www.themoviedb.org/movie/949")];

        var rows = GapRowProjector.Project([gap]).Items;

        var json = JsonSerializer.Serialize(rows[0]);
        Assert.True(rows[0].HasOverview);
        Assert.DoesNotContain("A long overview.", json, System.StringComparison.Ordinal);
        Assert.DoesNotContain("themoviedb.org/movie", json, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Project_SendsASetsSourceLinksOnceNoMatterHowManyGapsShareThem()
    {
        var gaps = Enumerable.Range(0, 500).Select(i => Gap("g" + i)).ToArray();

        var report = GapRowProjector.Project(gaps);
        var rows = report.Items;
        var sets = report.SourceLinkSets;

        Assert.Single(sets);
        Assert.All(rows, r => Assert.Equal(0, r.SourceLinksRef));
        Assert.Equal("https://www.themoviedb.org/company/1", sets[0][0].Url);
    }

    [Fact]
    public void Project_KeepsDifferentSourceLinksApart()
    {
        var a = Gap("a");
        var b = Gap("b");
        b.SourceLinks = [new ExternalLink("TMDB", "https://www.themoviedb.org/company/2")];
        var none = Gap("c");
        none.SourceLinks = [];

        var report = GapRowProjector.Project([a, b, none]);
        var rows = report.Items;
        var sets = report.SourceLinkSets;

        Assert.Equal(2, sets.Count);
        Assert.Equal(0, rows[0].SourceLinksRef);
        Assert.Equal(1, rows[1].SourceLinksRef);
        Assert.Null(rows[2].SourceLinksRef);
    }

    [Fact]
    public void Project_ReducesOffersToDistinctProviderAndMonetization()
    {
        var gap = Gap("g1");
        gap.Availability =
        [
            new AvailabilityOffer { Provider = "Netflix", MonetizationType = "flatrate", Quality = "4K", Url = "https://a" },
            new AvailabilityOffer { Provider = "Netflix", MonetizationType = "flatrate", Quality = "HD", Url = "https://b" },
            new AvailabilityOffer { Provider = "Netflix", MonetizationType = "buy", Url = "https://c" }
        ];

        var rows = GapRowProjector.Project([gap]).Items;

        var badges = rows[0].Availability!;
        Assert.Equal(2, badges.Count);
        Assert.DoesNotContain("https://", JsonSerializer.Serialize(rows[0]), System.StringComparison.Ordinal);
    }

    [Fact]
    public void Project_StatesASharedPatternAndDomainOnceOnTheReport()
    {
        var report = GapRowProjector.Project([Gap("a"), Gap("b"), Gap("c")]);

        Assert.Equal("SetCompletion", report.PatternName);
        Assert.Equal("Movies", report.DomainName);
        Assert.All(report.Items, r =>
        {
            Assert.Null(r.PatternName);
            Assert.Null(r.DomainName);
        });
        var json = JsonSerializer.Serialize(report.Items[0]);
        Assert.DoesNotContain("PatternName", json, System.StringComparison.Ordinal);
        Assert.DoesNotContain("DomainName", json, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Project_KeepsPatternAndDomainOnEachRowWhenTheyMix()
    {
        var other = Gap("b");
        other.Pattern = GapPattern.Recommendation;
        other.Domain = MediaDomain.Shows;

        var report = GapRowProjector.Project([Gap("a"), other]);

        Assert.Null(report.PatternName);
        Assert.Null(report.DomainName);
        Assert.Equal("SetCompletion", report.Items[0].PatternName);
        Assert.Equal("Recommendation", report.Items[1].PatternName);
        Assert.Equal("Movies", report.Items[0].DomainName);
        Assert.Equal("Shows", report.Items[1].DomainName);
    }

    [Fact]
    public void Project_IndexesEachDistinctTargetKindOnce()
    {
        var series = Gap("b");
        series.TargetKind = BaseItemKind.Series;

        var report = GapRowProjector.Project([Gap("a"), series, Gap("c")]);

        Assert.Equal(["Movie", "Series"], report.TargetKinds);
        Assert.Equal([0, 1, 0], report.Items.Select(r => r.TargetKindRef).ToArray());
    }

    [Fact]
    public void Project_OfNothingHoistsNothing()
    {
        var report = GapRowProjector.Project([]);

        Assert.Empty(report.Items);
        Assert.Null(report.PatternName);
        Assert.Null(report.DomainName);
    }

    [Fact]
    public void Project_KeepsTheServiceLogoOnTheBadge()
    {
        var gap = Gap("g1");
        gap.Availability = [new AvailabilityOffer { Provider = "Netflix", MonetizationType = "flatrate", LogoUrl = "https://image.tmdb.org/t/p/w45/n.jpg" }];

        var rows = GapRowProjector.Project([gap]).Items;

        Assert.Equal("https://image.tmdb.org/t/p/w45/n.jpg", rows[0].Availability![0].LogoUrl);
    }

    [Fact]
    public void Project_OmitsEmptyThingsFromTheJson()
    {
        var gap = Gap("g1");
        gap.ProviderIds = new Dictionary<string, string>();
        gap.SourceLinks = [];

        var rows = GapRowProjector.Project([gap]).Items;
        var json = JsonSerializer.Serialize(rows[0]);

        foreach (var absent in new[] { "ProviderIds", "SourceLinksRef", "Availability", "OtherSources", "SetOwnedCount", "SortScore", "ImageUrl", "IsUpcoming", "HasOverview" })
        {
            Assert.DoesNotContain("\"" + absent + "\"", json, System.StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Row_CoversEveryGapItemPropertyItDoesNotDeliberatelyDrop()
    {
        var rowProps = typeof(GapRow).GetProperties().Select(p => p.Name).ToHashSet(System.StringComparer.Ordinal);
        var missing = typeof(GapItem).GetProperties()
            .Select(p => p.Name)
            .Where(n => !_notOnRow.Contains(n) && !rowProps.Contains(n))
            .ToArray();

        Assert.Empty(missing);
    }

    [Fact]
    public void Project_CarriesTheFieldsTheListGroupsAndSortsOn()
    {
        var gap = Gap("g1");
        gap.SetOwnedCount = 3;
        gap.SetTotalCount = 9;
        gap.SortScore = 12.5;
        gap.Season = 2;
        gap.WatchTmdbId = "77";
        gap.AvailabilityChecked = true;

        var report = GapRowProjector.Project([gap]);
        var row = report.Items[0];

        Assert.Equal("Movie", report.TargetKinds[row.TargetKindRef]);
        Assert.Equal(3, row.SetOwnedCount);
        Assert.Equal(9, row.SetTotalCount);
        Assert.Equal(12.5, row.SortScore);
        Assert.Equal(2, row.Season);
        Assert.Equal("77", row.WatchTmdbId);
        Assert.True(row.AvailabilityChecked);
        Assert.Equal("949", row.ProviderIds!["Tmdb"]);
    }

    [Fact]
    public void Project_CarriesTheTitlesWatchPageOnce()
    {
        var gap = Gap("g1");
        gap.Availability = new[]
        {
            new AvailabilityOffer { Provider = "Netflix", Url = "https://www.themoviedb.org/movie/949-heat/watch?locale=US" },
            new AvailabilityOffer { Provider = "Max", Url = "https://www.themoviedb.org/movie/949-heat/watch?locale=US" }
        };

        var row = GapRowProjector.Project([gap]).Items[0];

        Assert.Equal("https://www.themoviedb.org/movie/949-heat/watch?locale=US", row.WatchUrl);
    }

    [Fact]
    public void Project_SkipsAnOfferWhoseLinkIsNotOnTmdb_AndHasNoWatchPageWithoutOffers()
    {
        var gap = Gap("g1");
        gap.Availability = new[]
        {
            new AvailabilityOffer { Provider = "A", Url = "javascript:alert(1)" },
            new AvailabilityOffer { Provider = "B", Url = null },
            new AvailabilityOffer { Provider = "C", Url = "https://evil.example/watch" },
            new AvailabilityOffer { Provider = "D", Url = "https://www.themoviedb.org/movie/1/watch" }
        };

        Assert.Equal("https://www.themoviedb.org/movie/1/watch", GapRowProjector.Project([gap]).Items[0].WatchUrl);
        Assert.Null(GapRowProjector.Project([Gap("none")]).Items[0].WatchUrl);
    }
}
