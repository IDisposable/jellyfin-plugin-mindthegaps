using System.Collections.Generic;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// GapReportEdits was split out of GapStore specifically so this logic could be exercised without a store
// (no lock, no cache, no disk); see the class remarks for why GapStore itself already had thorough coverage
// of these paths only indirectly, through its public partial-update methods.
public class GapReportEditsTests
{
    private static GapItem Gap(string id, string? sourceItemId = null) => new() { Id = id, Name = id, SourceItemId = sourceItemId };

    [Fact]
    public void IsReplaced_TrueWhenTheOwnerAndPrefixBothMatch()
    {
        var item = Gap("collection:10:1", "owner-1");

        Assert.True(GapReportEdits.IsReplaced(item, "owner-1", ["collection:"]));
    }

    [Fact]
    public void IsReplaced_FalseWhenTheOwnerDiffers()
    {
        var item = Gap("collection:10:1", "owner-1");

        Assert.False(GapReportEdits.IsReplaced(item, "owner-2", ["collection:"]));
    }

    [Fact]
    public void IsReplaced_FalseWhenNoPrefixMatches_EvenForTheSameOwner()
    {
        // The same owning item can seed both a set-completion gap and a recommendation; re-checking one
        // must not disturb the other.
        var item = Gap("recommendation:603", "owner-1");

        Assert.False(GapReportEdits.IsReplaced(item, "owner-1", ["collection:"]));
    }

    [Fact]
    public void DomainsOf_ReturnsTheDistinctDomains()
    {
        var items = new[]
        {
            new GapItem { Domain = MediaDomain.Movies },
            new GapItem { Domain = MediaDomain.Shows },
            new GapItem { Domain = MediaDomain.Movies }
        };

        Assert.Equal(new HashSet<MediaDomain> { MediaDomain.Movies, MediaDomain.Shows }, GapReportEdits.DomainsOf(items));
    }

    [Fact]
    public void CarryEnrichment_DoesNothingWhenThePriorGapWasNeverChecked()
    {
        var prior = Gap("a");
        var fresh = Gap("a");

        GapReportEdits.CarryEnrichment(prior, fresh);

        Assert.False(fresh.AvailabilityChecked);
    }

    [Fact]
    public void CarryEnrichment_CarriesTheCheckedFlagAndOffers_WhenTheFreshGapHasNone()
    {
        var prior = Gap("a");
        prior.AvailabilityChecked = true;
        prior.Availability = [new AvailabilityOffer { Provider = "Netflix" }];
        var fresh = Gap("a");

        GapReportEdits.CarryEnrichment(prior, fresh);

        Assert.True(fresh.AvailabilityChecked);
        Assert.Single(fresh.Availability);
    }

    [Fact]
    public void CarryEnrichment_NeverOverwritesOffersTheFreshGapAlreadyHas()
    {
        var prior = Gap("a");
        prior.AvailabilityChecked = true;
        prior.Availability = [new AvailabilityOffer { Provider = "Netflix" }];
        var fresh = Gap("a");
        fresh.Availability = [new AvailabilityOffer { Provider = "Hulu" }];

        GapReportEdits.CarryEnrichment(prior, fresh);

        Assert.Equal("Hulu", Assert.Single(fresh.Availability).Provider);
    }

    [Fact]
    public void MergeAvailability_CopiesAvailabilityFieldsById()
    {
        var from = new GapReport { Items = [Gap("a")] };
        from.Items[0].AvailabilityChecked = true;
        from.Items[0].Availability = [new AvailabilityOffer { Provider = "Netflix" }];
        var into = new GapReport { Items = [Gap("a")] };

        GapReportEdits.MergeAvailability(from, into);

        Assert.True(into.Items[0].AvailabilityChecked);
        Assert.Single(into.Items[0].Availability);
    }

    [Fact]
    public void MergeAvailability_SkipsAnItemTheNewerReportDoesNotHave()
    {
        var from = new GapReport { Items = [Gap("gone")] };
        from.Items[0].AvailabilityChecked = true;
        var into = new GapReport { Items = [Gap("still-here")] };

        GapReportEdits.MergeAvailability(from, into);

        Assert.False(into.Items[0].AvailabilityChecked);
    }

    [Fact]
    public void MergeAvailability_LeavesAnUntouchedItemsValuesAlone()
    {
        var from = new GapReport { Items = [] };
        var into = new GapReport { Items = [Gap("a")] };
        into.Items[0].AvailabilityChecked = true;

        GapReportEdits.MergeAvailability(from, into);

        Assert.True(into.Items[0].AvailabilityChecked);
    }
}
