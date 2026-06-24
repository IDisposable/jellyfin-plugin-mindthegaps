using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class ExploreRegistryTests
{
    [Fact]
    public void Find_ReturnsDescriptorForAKnownKindCaseInsensitive()
    {
        var registry = new ExploreRegistry([new FakeExploreSource()]);

        Assert.NotNull(registry.Find("STUDIO"));
        Assert.Equal("studio", registry.Find("studio")!.Kind);
        Assert.Null(registry.Find("nope"));
        Assert.Null(registry.Find(null));
    }

    [Fact]
    public void IsKnown_TracksDeclaredKinds()
    {
        var registry = new ExploreRegistry([new FakeExploreSource()]);

        Assert.True(registry.IsKnown("tmdblist"));
        Assert.False(registry.IsKnown("label"));
        Assert.False(registry.IsKnown(null));
    }

    [Fact]
    public void Kinds_DeriveLabelAndSearchableFromTheDescriptors()
    {
        var registry = new ExploreRegistry([new FakeExploreSource()]);

        var studio = Assert.Single(registry.Kinds, k => k.Kind == "studio");
        Assert.Equal("Studio", studio.Label);
        Assert.True(studio.Searchable);

        var list = Assert.Single(registry.Kinds, k => k.Kind == "tmdblist");
        Assert.False(list.Searchable);
    }

    // A source that declares two explore kinds: a searchable one (has a Search delegate) and a raw-id one.
    private sealed class FakeExploreSource : IGapSource, IExploreSource
    {
        public FakeExploreSource()
        {
            ExploreDescriptors = new[]
            {
                new ExploreDescriptor
                {
                    Kind = "studio",
                    Label = "Studio",
                    Source = this,
                    Run = (context, ids, ct) => Empty(),
                    Search = (query, ct) => Task.FromResult<IReadOnlyList<CuratedSetRef>>([])
                },
                new ExploreDescriptor
                {
                    Kind = "tmdblist",
                    Label = "TMDB list",
                    Source = this,
                    Run = (context, ids, ct) => Empty()
                }
            };
        }

        public string Name => "Fake";

        public IReadOnlyCollection<BaseItemKind> OwnedKinds { get; } = [];

        public IReadOnlyCollection<ExploreDescriptor> ExploreDescriptors { get; }

        public bool IsEnabled(PluginConfiguration config) => true;

        public IAsyncEnumerable<GapItem> FindGapsAsync(GapScanContext context, CancellationToken cancellationToken) => Empty();

        private static async IAsyncEnumerable<GapItem> Empty()
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
