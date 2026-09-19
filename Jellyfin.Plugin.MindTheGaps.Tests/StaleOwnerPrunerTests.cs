using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Imdb;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Tmdb;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Trakt;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using Jellyfin.Plugin.MindTheGaps.Services.Imdb;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using Jellyfin.Plugin.MindTheGaps.Services.Trakt;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class StaleOwnerPrunerTests
{
    private static GapItem Gap(string id, string? sourceItemId = null) => new() { Id = id, Name = id, SourceItemId = sourceItemId };

    // ---- FindStaleIds dispatch (a fake source, so this only exercises the pruner's own prefix routing) ----

    private sealed class FakeScopeSource : IConfiguredScopeSource
    {
        private readonly HashSet<string> _currentOwners;

        public FakeScopeSource(string gapIdPrefix, params string[] currentOwners)
        {
            GapIdPrefix = gapIdPrefix;
            _currentOwners = new HashSet<string>(currentOwners, StringComparer.Ordinal);
        }

        public string GapIdPrefix { get; }

        public bool StillInScope(GapItem item, PluginConfiguration config) => _currentOwners.Contains(item.SourceItemId ?? string.Empty);
    }

    [Fact]
    public void FindStaleIds_ReturnsIdsNoLongerInAnySourcesScope()
    {
        var source = new FakeScopeSource("fake:", "fake-kept");
        var items = new[]
        {
            Gap("fake:1", "fake-kept"),
            Gap("fake:2", "fake-removed"),
            Gap("other:1", "other-owner")
        };

        var stale = StaleOwnerPruner.FindStaleIds(items, new[] { source }, new PluginConfiguration());

        Assert.Equal(new[] { "fake:2" }, stale);
    }

    [Fact]
    public void FindStaleIds_IgnoresGapsNoScopedSourcesPrefixMatches()
    {
        var source = new FakeScopeSource("fake:");
        var items = new[] { Gap("unrelated:1", "anything") };

        var stale = StaleOwnerPruner.FindStaleIds(items, new[] { source }, new PluginConfiguration());

        Assert.Empty(stale);
    }

    [Fact]
    public void FindStaleIds_AsksOnlyTheFirstMatchingPrefix()
    {
        // A gap id carries exactly one owning prefix; a second source sharing no prefix overlap must never
        // be consulted for it, even if it would (wrongly) claim the gap is stale.
        var keep = new FakeScopeSource("fake:", "fake-kept");
        var wouldPruneEverything = new FakeScopeSource("other:");
        var items = new[] { Gap("fake:1", "fake-kept") };

        var stale = StaleOwnerPruner.FindStaleIds(items, new IConfiguredScopeSource[] { keep, wouldPruneEverything }, new PluginConfiguration());

        Assert.Empty(stale);
    }

    // ---- Real sources: one of each shape (CuratedSetGapSource, single-owner, id-list, kind-suffixed) ----

    private static TmdbClient NewTmdbClient() => new(new MemoryCache(new MemoryCacheOptions()));

    private static CachedApiClient NewCachedApiClient()
        => new(new NullHttpClientFactory(), new MemoryCache(new MemoryCacheOptions()), NullLogger<CachedApiClient>.Instance);

    private sealed class NullHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    // StillInScope never touches the library, so a proxy that just returns defaults is enough to satisfy
    // the constructor.
    private class NoOpLibraryManagerProxy : DispatchProxy
    {
        public static ILibraryManager Create() => (ILibraryManager)DispatchProxy.Create<ILibraryManager, NoOpLibraryManagerProxy>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod?.ReturnType.IsValueType == true ? Activator.CreateInstance(targetMethod.ReturnType) : null;
    }

    private static CuratedSetGapSource NewCuratedSetGapSource()
        => new(NewTmdbClient(), NoOpLibraryManagerProxy.Create(), NullLogger<CuratedSetGapSource>.Instance);

    [Fact]
    public void CuratedSetGapSource_KeywordGap_StaysInScopeWhileTheIdIsStillConfigured()
    {
        var source = NewCuratedSetGapSource();
        var config = new PluginConfiguration { ScanCuratedSets = true, CuratedKeywordIds = "9715" };
        var gap = Gap(source.GapIdPrefix + "keyword:9715:603");

        Assert.True(source.StillInScope(gap, config));
    }

    [Fact]
    public void CuratedSetGapSource_KeywordGap_IsStaleOnceTheIdIsRemoved()
    {
        var source = NewCuratedSetGapSource();
        var config = new PluginConfiguration { ScanCuratedSets = true, CuratedKeywordIds = "1234" };
        var gap = Gap(source.GapIdPrefix + "keyword:9715:603");

        Assert.False(source.StillInScope(gap, config));
    }

    [Fact]
    public void CuratedSetGapSource_KeywordGap_IsStaleWhenCuratedSetsIsDisabledEntirely()
    {
        var source = NewCuratedSetGapSource();
        var config = new PluginConfiguration { ScanCuratedSets = false, CuratedKeywordIds = "9715" };
        var gap = Gap(source.GapIdPrefix + "keyword:9715:603");

        Assert.False(source.StillInScope(gap, config));
    }

    [Fact]
    public void CuratedSetGapSource_CompanyGap_IsStaleOnceTheIdIsRemoved_WhenAutoSeedIsOff()
    {
        var source = NewCuratedSetGapSource();
        var config = new PluginConfiguration { ScanCuratedSets = true, CuratedCompanyIds = "1", AutoSeedStudios = false };
        var gap = Gap(source.GapIdPrefix + "company:41077:603");

        Assert.False(source.StillInScope(gap, config));
    }

    [Fact]
    public void CuratedSetGapSource_CompanyGap_IsNeverPrunedWhileAutoSeedIsOn()
    {
        // Auto-seeded studios are picked live each scan via a TMDB search; a pure, config-only prune has no
        // way to tell which company ids currently qualify without that same network call, so it must not
        // guess and risk deleting a legitimately active auto-seeded studio's gaps.
        var source = NewCuratedSetGapSource();
        var config = new PluginConfiguration { ScanCuratedSets = true, CuratedCompanyIds = string.Empty, AutoSeedStudios = true };
        var gap = Gap(source.GapIdPrefix + "company:41077:603");

        Assert.True(source.StillInScope(gap, config));
    }

    [Fact]
    public void CuratedSetGapSource_TmdbListGap_TracksTheConfiguredListIds()
    {
        var source = NewCuratedSetGapSource();
        var kept = new PluginConfiguration { ScanTmdbLists = true, CuratedTmdbListIds = "999" };
        var removed = new PluginConfiguration { ScanTmdbLists = true, CuratedTmdbListIds = "1" };
        var gap = Gap(GapSourceKeys.TmdbList.GapPrefix + "list:999", GapSourceKeys.TmdbList.Owner(999));

        Assert.True(source.StillInScope(gap, kept));
        Assert.False(source.StillInScope(gap, removed));
    }

    [Fact]
    public void TraktWatchlistGapSource_SingleOwner_TracksWhetherTheSourceIsStillEnabled()
    {
        var source = new TraktWatchlistGapSource(new TraktClient(NewCachedApiClient()), NullLogger<TraktWatchlistGapSource>.Instance);
        var gap = Gap(source.GapIdPrefix + "tt1", GapSourceKeys.TraktWatchlist.Owner());
        var enabled = new PluginConfiguration { ScanTraktWatchlist = true, TraktClientId = "id", TraktUsername = "alice" };
        var disabled = new PluginConfiguration { ScanTraktWatchlist = false, TraktClientId = "id", TraktUsername = "alice" };

        Assert.True(source.StillInScope(gap, enabled));
        Assert.False(source.StillInScope(gap, disabled));
    }

    [Fact]
    public void ImdbListGapSource_IdList_TracksWhichListIdsAreStillConfigured()
    {
        var source = new ImdbListGapSource(new ImdbClient(NewCachedApiClient()), NullLogger<ImdbListGapSource>.Instance);
        var gap = Gap(source.GapIdPrefix + "ls123:tt1", GapSourceKeys.ImdbList.Owner("ls123"));
        var kept = new PluginConfiguration { ScanImdbLists = true, ImdbListIds = "ls123" };
        var removed = new PluginConfiguration { ScanImdbLists = true, ImdbListIds = "ls999" };

        Assert.True(source.StillInScope(gap, kept));
        Assert.False(source.StillInScope(gap, removed));
    }

    [Fact]
    public void TmdbMovieDiscoverGapSource_KindSuffixed_EachFeedTracksItsOwnToggle()
    {
        var source = new TmdbMovieDiscoverGapSource(NewTmdbClient(), NullLogger<TmdbMovieDiscoverGapSource>.Instance);
        var topRated = Gap(source.GapIdPrefix + "top_rated:603", GapSourceKeys.TmdbMovieDiscover.Owner("top_rated"));
        var popular = Gap(source.GapIdPrefix + "popular:603", GapSourceKeys.TmdbMovieDiscover.Owner("popular"));
        var config = new PluginConfiguration { ScanTmdbTopRated = true, ScanTmdbPopular = false };

        Assert.True(source.StillInScope(topRated, config));
        Assert.False(source.StillInScope(popular, config));
    }

    [Fact]
    public void FindStaleIds_EndToEnd_WithARealSource()
    {
        var source = new TraktWatchlistGapSource(new TraktClient(NewCachedApiClient()), NullLogger<TraktWatchlistGapSource>.Instance);
        var owner = GapSourceKeys.TraktWatchlist.Owner();
        var items = new[]
        {
            Gap(source.GapIdPrefix + "tt1", owner),
            Gap("collection:10:1", Guid.NewGuid().ToString("N"))
        };
        var disabled = new PluginConfiguration { ScanTraktWatchlist = false };

        var stale = StaleOwnerPruner.FindStaleIds(items, new[] { source }, disabled);

        Assert.Equal(new[] { source.GapIdPrefix + "tt1" }, stale);
    }
}
