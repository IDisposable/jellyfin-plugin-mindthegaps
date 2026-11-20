using System;
using System.Collections.Generic;
using System.Reflection;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class ExternalLinkEnricherTests
{
    private static readonly FakeItemTypeLookup EmptyLookup = new();
    private static readonly Dictionary<string, string> EmptyIds = new();

    [Fact]
    public void Merge_HostWinsOnNameConflict()
    {
        var host = new List<ExternalLink> { new("TMDB", "host-url") };
        var fallback = new List<ExternalLink> { new("TMDB", "fallback-url") };

        var merged = ExternalLinkEnricher.Merge(host, fallback);

        Assert.Equal("host-url", Assert.Single(merged).Url);
    }

    [Fact]
    public void Merge_FallbackFillsNamesHostDidNotProduce()
    {
        var host = new List<ExternalLink> { new("TMDB", "t") };
        var fallback = new List<ExternalLink> { new("TheTVDB", "v") };

        var merged = ExternalLinkEnricher.Merge(host, fallback);

        Assert.Equal(2, merged.Count);
        Assert.Contains(merged, l => l.Name == "TheTVDB");
    }

    [Fact]
    public void Merge_OrdersHostFirstThenUncoveredFallback()
    {
        var host = new List<ExternalLink> { new("TMDB", "t"), new("IMDb", "i") };
        var fallback = new List<ExternalLink> { new("TheTVDB", "v"), new("IMDb", "dup") };

        var merged = ExternalLinkEnricher.Merge(host, fallback);

        Assert.Collection(
            merged,
            l => Assert.Equal("TMDB", l.Name),
            l => Assert.Equal("IMDb", l.Name),
            l => Assert.Equal("TheTVDB", l.Name));
        // The IMDb duplicate from the fallback did not override the host's value.
        Assert.Equal("i", merged[1].Url);
    }

    [Fact]
    public void Merge_NameMatchIsCaseInsensitive()
    {
        var host = new List<ExternalLink> { new("TMDB", "host") };
        var fallback = new List<ExternalLink> { new("tmdb", "fallback") };

        Assert.Single(ExternalLinkEnricher.Merge(host, fallback));
    }

    [Fact]
    public void Merge_EmptyHost_ReturnsFallback()
    {
        var fallback = new List<ExternalLink> { new("TheTVDB", "v") };

        var merged = ExternalLinkEnricher.Merge(new List<ExternalLink>(), fallback);

        Assert.Equal("TheTVDB", Assert.Single(merged).Name);
    }

    [Theory]
    [InlineData(BaseItemKind.Movie, typeof(Movie))]
    [InlineData(BaseItemKind.Series, typeof(Series))]
    [InlineData(BaseItemKind.Season, typeof(Season))]
    [InlineData(BaseItemKind.Episode, typeof(Episode))]
    [InlineData(BaseItemKind.BoxSet, typeof(BoxSet))]
    [InlineData(BaseItemKind.Person, typeof(Person))]
    public void Synthesize_FastPathKinds_ProduceConcreteType(BaseItemKind kind, Type expected)
    {
        var item = ExternalLinkEnricher.Synthesize(kind, EmptyIds, EmptyLookup);

        Assert.NotNull(item);
        Assert.IsType(expected, item);
    }

    [Fact]
    public void Synthesize_StampsProviderIds_SkippingEmptyValues()
    {
        var item = ExternalLinkEnricher.Synthesize(
            BaseItemKind.Movie,
            new Dictionary<string, string> { ["Tmdb"] = "603", ["Imdb"] = string.Empty },
            EmptyLookup);

        Assert.NotNull(item);
        Assert.Equal("603", item!.ProviderIds["Tmdb"]);
        Assert.False(item.ProviderIds.ContainsKey("Imdb"));
    }

    [Fact]
    public void Synthesize_ProviderIdsAreCaseInsensitive()
    {
        var item = ExternalLinkEnricher.Synthesize(
            BaseItemKind.Movie,
            new Dictionary<string, string> { ["Tmdb"] = "603" },
            EmptyLookup);

        Assert.NotNull(item);
        Assert.True(item!.ProviderIds.ContainsKey("tmdb"));
    }

    [Fact]
    public void Synthesize_FallbackKind_ResolvesViaLookup()
    {
        var lookup = new FakeItemTypeLookup
        {
            BaseItemKindNames = new Dictionary<BaseItemKind, string>
            {
                [BaseItemKind.Studio] = typeof(Studio).FullName!
            }
        };

        Assert.IsType<Studio>(ExternalLinkEnricher.Synthesize(BaseItemKind.Studio, EmptyIds, lookup));
    }

    [Fact]
    public void Synthesize_KindNotInLookup_ReturnsNull()
    {
        Assert.Null(ExternalLinkEnricher.Synthesize(BaseItemKind.Studio, EmptyIds, EmptyLookup));
    }

    [Fact]
    public void Synthesize_UnresolvableTypeName_ReturnsNull()
    {
        var lookup = new FakeItemTypeLookup
        {
            BaseItemKindNames = new Dictionary<BaseItemKind, string> { [BaseItemKind.Studio] = "Not.A.Real.Type" }
        };

        Assert.Null(ExternalLinkEnricher.Synthesize(BaseItemKind.Studio, EmptyIds, lookup));
    }

    [Fact]
    public void Synthesize_AbstractType_ReturnsNull()
    {
        var lookup = new FakeItemTypeLookup
        {
            BaseItemKindNames = new Dictionary<BaseItemKind, string> { [BaseItemKind.Studio] = typeof(BaseItem).FullName! }
        };

        Assert.Null(ExternalLinkEnricher.Synthesize(BaseItemKind.Studio, EmptyIds, lookup));
    }

    [Fact]
    public void ForServing_AddsTheHostLinksToACopy_AndLeavesTheStoredGapAlone()
    {
        var enricher = new ExternalLinkEnricher(HostLinksProxy.Create(), EmptyLookup, NullLogger<ExternalLinkEnricher>.Instance);
        var stored = new GapItem
        {
            Id = "recommendation:movie:603",
            Name = "The Matrix",
            TargetKind = BaseItemKind.Movie,
            ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Tmdb"] = "603" }
        };
        ExternalLinkEnricher.FillStored([stored]);
        var storedLinks = stored.Links;

        var served = enricher.ForServing(stored);
        var servedAll = enricher.ForServing([stored]);

        Assert.NotSame(stored, served);
        Assert.Same(storedLinks, stored.Links);
        Assert.DoesNotContain(stored.Links, l => l.Name == "Host");
        Assert.Contains(served.Links, l => l.Name == "Host" && l.Url == "https://host.example/603");
        Assert.Contains(served.Links, l => l.Name == "TMDB");
        Assert.Contains(Assert.Single(servedAll).Links, l => l.Name == "Host");
    }

    private class HostLinksProxy : DispatchProxy
    {
        public static IProviderManager Create() => DispatchProxy.Create<IProviderManager, HostLinksProxy>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IProviderManager.GetExternalUrls))
            {
                var item = (BaseItem)args![0]!;
                return new[] { new ExternalUrl { Name = "Host", Url = "https://host.example/" + item.GetProviderId("Tmdb") } };
            }

            return targetMethod?.ReturnType.IsValueType == true ? Activator.CreateInstance(targetMethod.ReturnType) : null;
        }
    }

    private sealed class FakeItemTypeLookup : IItemTypeLookup
    {
        public IReadOnlyList<string> MusicGenreTypes { get; } = [];

        public IReadOnlyDictionary<BaseItemKind, string> BaseItemKindNames { get; init; } = new Dictionary<BaseItemKind, string>();
    }
}
