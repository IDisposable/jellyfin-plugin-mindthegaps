using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// GapBackfill was split out of GapEngine specifically so this logic could be exercised without a scan;
// see the class remarks for why GapEngine itself has no direct tests of its own.
public class GapBackfillTests
{
    private static GapItem Gap(
        string id,
        GapPattern pattern = GapPattern.Recommendation,
        MediaDomain domain = MediaDomain.Movies,
        BaseItemKind targetKind = BaseItemKind.Movie,
        string? sourceItemId = null,
        string? sourceItemName = null,
        bool adhoc = false,
        IReadOnlyDictionary<string, string>? providerIds = null)
        => new()
        {
            Id = id,
            Name = id,
            Pattern = pattern,
            Domain = domain,
            TargetKind = targetKind,
            SourceItemId = sourceItemId,
            SourceItemName = sourceItemName,
            Adhoc = adhoc,
            ProviderIds = providerIds ?? new Dictionary<string, string>()
        };

    private static OwnershipIndex Owns(BaseItemKind kind, string provider, string id)
        => new(new HashSet<string>(StringComparer.Ordinal) { OwnershipIndex.MakeKey(kind, provider, id) });

    private static OwnershipIndex OwnsNothing() => new(new HashSet<string>(StringComparer.Ordinal));

    // ---- CarryForward ----

    [Fact]
    public void CarryForward_ReadoptsAProviderIdTheFreshGapDoesNotHave()
    {
        var fresh = Gap("a", providerIds: new Dictionary<string, string> { ["Tmdb"] = "603" });
        var before = Gap("a", providerIds: new Dictionary<string, string> { ["Tmdb"] = "603", ["Imdb"] = "tt0133093" });

        GapBackfill.CarryForward([fresh], [before]);

        Assert.Equal("tt0133093", fresh.ProviderIds["Imdb"]);
    }

    [Fact]
    public void CarryForward_NeverOverwritesAProviderIdTheFreshGapAlreadyHas()
    {
        var fresh = Gap("a", providerIds: new Dictionary<string, string> { ["Tmdb"] = "999" });
        var before = Gap("a", providerIds: new Dictionary<string, string> { ["Tmdb"] = "603" });

        GapBackfill.CarryForward([fresh], [before]);

        Assert.Equal("999", fresh.ProviderIds["Tmdb"]);
    }

    [Fact]
    public void CarryForward_CarriesTheWatchTmdbIdWhenTheFreshGapHasNone()
    {
        var fresh = Gap("a");
        var before = Gap("a");
        before.WatchTmdbId = "603";

        GapBackfill.CarryForward([fresh], [before]);

        Assert.Equal("603", fresh.WatchTmdbId);
    }

    [Fact]
    public void CarryForward_CarriesAvailabilityCheckedAndOffersWhenTheFreshGapHasNeither()
    {
        var fresh = Gap("a");
        var before = Gap("a");
        before.AvailabilityChecked = true;
        before.Availability = [new AvailabilityOffer { Provider = "Netflix" }];

        GapBackfill.CarryForward([fresh], [before]);

        Assert.True(fresh.AvailabilityChecked);
        Assert.Single(fresh.Availability);
    }

    [Fact]
    public void CarryForward_IgnoresAGapWithNoMatchingPriorId()
    {
        var fresh = Gap("a");

        GapBackfill.CarryForward([fresh], [Gap("b")]);

        Assert.False(fresh.AvailabilityChecked);
        Assert.Empty(fresh.ProviderIds);
    }

    [Fact]
    public void CarryForward_IsANoOpWhenThereIsNoPriorReport()
    {
        var fresh = Gap("a");

        // Must not throw over an empty prior list; a first-ever scan has nothing to carry forward.
        GapBackfill.CarryForward([fresh], []);

        Assert.Empty(fresh.ProviderIds);
    }

    // ---- AccumulateUnowned ----

    [Fact]
    public void AccumulateUnowned_CarriesAnUnownedPriorGapOfTheSamePatternForward()
    {
        var gaps = new List<GapItem>();
        var byId = new Dictionary<string, GapItem>(StringComparer.Ordinal);
        var prior = new[] { Gap("a", GapPattern.CreatorWorks) };

        var result = GapBackfill.AccumulateUnowned(gaps, byId, prior, OwnsNothing(), GapPattern.CreatorWorks, new HashSet<string>(StringComparer.Ordinal));

        Assert.Equal(1, result.Carried);
        Assert.False(result.CappedOut);
        Assert.Same(prior[0], Assert.Single(gaps));
        Assert.True(byId.ContainsKey("a"));
    }

    [Fact]
    public void AccumulateUnowned_SkipsAGapAlreadyReemittedThisRun()
    {
        var reemitted = Gap("a", GapPattern.CreatorWorks);
        var gaps = new List<GapItem> { reemitted };
        var byId = new Dictionary<string, GapItem>(StringComparer.Ordinal) { ["a"] = reemitted };
        var prior = new[] { Gap("a", GapPattern.CreatorWorks) };

        var result = GapBackfill.AccumulateUnowned(gaps, byId, prior, OwnsNothing(), GapPattern.CreatorWorks, new HashSet<string>(StringComparer.Ordinal));

        Assert.Equal(0, result.Carried);
        Assert.Single(gaps);
    }

    [Fact]
    public void AccumulateUnowned_SkipsAGapOfADifferentPattern()
    {
        var gaps = new List<GapItem>();
        var byId = new Dictionary<string, GapItem>(StringComparer.Ordinal);
        var prior = new[] { Gap("a", GapPattern.Recommendation) };

        var result = GapBackfill.AccumulateUnowned(gaps, byId, prior, OwnsNothing(), GapPattern.CreatorWorks, new HashSet<string>(StringComparer.Ordinal));

        Assert.Equal(0, result.Carried);
        Assert.Empty(gaps);
    }

    [Fact]
    public void AccumulateUnowned_SkipsAnAdhocGap()
    {
        var gaps = new List<GapItem>();
        var byId = new Dictionary<string, GapItem>(StringComparer.Ordinal);
        var prior = new[] { Gap("a", GapPattern.CreatorWorks, adhoc: true) };

        var result = GapBackfill.AccumulateUnowned(gaps, byId, prior, OwnsNothing(), GapPattern.CreatorWorks, new HashSet<string>(StringComparer.Ordinal));

        Assert.Equal(0, result.Carried);
    }

    [Fact]
    public void AccumulateUnowned_SkipsAGapWhoseSourceWasDismissedWholesale()
    {
        var gaps = new List<GapItem>();
        var byId = new Dictionary<string, GapItem>(StringComparer.Ordinal);
        var prior = new[] { Gap("a", GapPattern.CreatorWorks, sourceItemId: "person-1") };
        var dismissed = new HashSet<string>(StringComparer.Ordinal) { "person-1" };

        var result = GapBackfill.AccumulateUnowned(gaps, byId, prior, OwnsNothing(), GapPattern.CreatorWorks, dismissed);

        Assert.Equal(0, result.Carried);
    }

    [Fact]
    public void AccumulateUnowned_SkipsAGapNowOwned()
    {
        var gaps = new List<GapItem>();
        var byId = new Dictionary<string, GapItem>(StringComparer.Ordinal);
        var prior = new[] { Gap("a", GapPattern.CreatorWorks, providerIds: new Dictionary<string, string> { ["Tmdb"] = "603" }) };

        var result = GapBackfill.AccumulateUnowned(gaps, byId, prior, Owns(BaseItemKind.Movie, "Tmdb", "603"), GapPattern.CreatorWorks, new HashSet<string>(StringComparer.Ordinal));

        Assert.Equal(0, result.Carried);
    }

    [Fact]
    public void AccumulateUnowned_ReportsCappedOutOnceTheLimitIsReached()
    {
        var gaps = new List<GapItem>();
        var byId = new Dictionary<string, GapItem>(StringComparer.Ordinal);
        var prior = new[] { Gap("a", GapPattern.CreatorWorks), Gap("b", GapPattern.CreatorWorks) };

        var result = GapBackfill.AccumulateUnowned(gaps, byId, prior, OwnsNothing(), GapPattern.CreatorWorks, new HashSet<string>(StringComparer.Ordinal), maxAccumulated: 1);

        Assert.Equal(1, result.Carried);
        Assert.True(result.CappedOut);
    }

    // ---- AccumulateSetCompletion ----

    [Fact]
    public void AccumulateSetCompletion_ReturnsNothingWhenNoDomainIsEnabled()
    {
        var gaps = new List<GapItem>();
        var byId = new Dictionary<string, GapItem>(StringComparer.Ordinal);
        var prior = new[] { Gap("a", GapPattern.SetCompletion) };

        var result = GapBackfill.AccumulateSetCompletion(gaps, byId, prior, OwnsNothing(), new HashSet<MediaDomain>());

        Assert.Equal(0, result.Carried);
        Assert.False(result.CappedOut);
    }

    [Fact]
    public void AccumulateSetCompletion_CarriesAnUnownedGapInAnEnabledDomain()
    {
        var gaps = new List<GapItem>();
        var byId = new Dictionary<string, GapItem>(StringComparer.Ordinal);
        var prior = new[] { Gap("a", GapPattern.SetCompletion, MediaDomain.Movies) };

        var result = GapBackfill.AccumulateSetCompletion(gaps, byId, prior, OwnsNothing(), new HashSet<MediaDomain> { MediaDomain.Movies });

        Assert.Equal(1, result.Carried);
        Assert.Single(gaps);
    }

    [Fact]
    public void AccumulateSetCompletion_SkipsAGapOutsideTheEnabledDomains()
    {
        var gaps = new List<GapItem>();
        var byId = new Dictionary<string, GapItem>(StringComparer.Ordinal);
        var prior = new[] { Gap("a", GapPattern.SetCompletion, MediaDomain.Music) };

        var result = GapBackfill.AccumulateSetCompletion(gaps, byId, prior, OwnsNothing(), new HashSet<MediaDomain> { MediaDomain.Movies });

        Assert.Equal(0, result.Carried);
    }

    [Fact]
    public void AccumulateSetCompletion_SkipsAnEpisodeGap()
    {
        // An episode set-completion gap is carried forward by GapEngine's own series-content pass, which
        // checks the library directly; this pass must leave it alone.
        var gaps = new List<GapItem>();
        var byId = new Dictionary<string, GapItem>(StringComparer.Ordinal);
        var prior = new[] { Gap("a", GapPattern.SetCompletion, MediaDomain.Shows, BaseItemKind.Episode) };

        var result = GapBackfill.AccumulateSetCompletion(gaps, byId, prior, OwnsNothing(), new HashSet<MediaDomain> { MediaDomain.Shows });

        Assert.Equal(0, result.Carried);
    }

    [Fact]
    public void AccumulateSetCompletion_SkipsAnAlbumOwnedByArtistAndTitleNameKey()
    {
        var gaps = new List<GapItem>();
        var byId = new Dictionary<string, GapItem>(StringComparer.Ordinal);
        var prior = new[] { Gap("a", GapPattern.SetCompletion, MediaDomain.Music, BaseItemKind.MusicAlbum, sourceItemName: "Miles Davis") };
        prior[0].Name = "Kind of Blue";
        var ownership = new OwnershipIndex(new HashSet<string>(StringComparer.Ordinal)
        {
            OwnershipIndex.MakeKey(BaseItemKind.MusicAlbum, OwnershipIndex.NameKeyProvider, OwnershipIndex.NameKey("Miles Davis", "Kind of Blue"))
        });

        var result = GapBackfill.AccumulateSetCompletion(gaps, byId, prior, ownership, new HashSet<MediaDomain> { MediaDomain.Music });

        Assert.Equal(0, result.Carried);
    }

    [Fact]
    public void AccumulateSetCompletion_ReportsCappedOutOnceTheLimitIsReached()
    {
        var gaps = new List<GapItem>();
        var byId = new Dictionary<string, GapItem>(StringComparer.Ordinal);
        var prior = new[] { Gap("a", GapPattern.SetCompletion, MediaDomain.Movies), Gap("b", GapPattern.SetCompletion, MediaDomain.Movies) };

        var result = GapBackfill.AccumulateSetCompletion(gaps, byId, prior, OwnsNothing(), new HashSet<MediaDomain> { MediaDomain.Movies }, maxAccumulated: 1);

        Assert.Equal(1, result.Carried);
        Assert.True(result.CappedOut);
    }

    [Fact]
    public void AccumulateUnowned_AndAccumulateSetCompletion_PreserveOrderOfPriorGaps()
    {
        var gaps = new List<GapItem>();
        var byId = new Dictionary<string, GapItem>(StringComparer.Ordinal);
        var prior = new[]
        {
            Gap("a", GapPattern.CreatorWorks),
            Gap("b", GapPattern.CreatorWorks)
        };

        GapBackfill.AccumulateUnowned(gaps, byId, prior, OwnsNothing(), GapPattern.CreatorWorks, new HashSet<string>(StringComparer.Ordinal));

        Assert.Equal(["a", "b"], gaps.Select(g => g.Id));
    }
}
