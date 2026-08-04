using System.Linq;
using Jellyfin.Plugin.MindTheGaps.Services;
using MediaBrowser.Model.Querying;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// Pinned because the defaults are the expensive ones.
public class LibraryQueryOptionsTests
{
    [Fact]
    public void WithProviderIds_AsksForProviderIdsAndNothingElse()
    {
        var options = LibraryQueryOptions.WithProviderIds();

        Assert.Equal(new[] { ItemFields.ProviderIds }, options.Fields.ToArray());
        Assert.False(options.EnableImages);
        Assert.False(options.EnableUserData);
    }

    [Fact]
    public void Minimal_AsksForNoNavigationsAtAll()
    {
        var options = LibraryQueryOptions.Minimal();

        Assert.Empty(options.Fields);
        Assert.False(options.EnableImages);
        Assert.False(options.EnableUserData);
    }

    [Fact]
    public void EachCallReturnsItsOwnOptions()
    {
        Assert.NotSame(LibraryQueryOptions.Minimal(), LibraryQueryOptions.Minimal());
        Assert.NotSame(LibraryQueryOptions.WithProviderIds(), LibraryQueryOptions.WithProviderIds());
    }
}
