using System.Security.Cryptography;
using Jellyfin.Plugin.MindTheGaps.Api;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class EmbeddedAssetTests
{
    private const string Resource = "Jellyfin.Plugin.MindTheGaps.Web.mindthegaps.webui.js";

    [Fact]
    public void Load_TagsTheAssetWithItsContentHash()
    {
        var asset = EmbeddedAsset.Load(typeof(Plugin).Assembly, Resource, "application/javascript");

        Assert.NotNull(asset);
        Assert.NotEmpty(asset.Bytes);
        Assert.Equal(System.Convert.ToHexStringLower(SHA256.HashData(asset.Bytes)), asset.Hash);
        Assert.Equal("\"" + asset.Hash + "\"", asset.ETag.Tag.ToString());
        Assert.False(asset.ETag.IsWeak);
        Assert.Equal("application/javascript", asset.ContentType);
    }

    [Fact]
    public void Load_CarriesTheAssembliesWriteTime()
    {
        var asset = EmbeddedAsset.Load(typeof(Plugin).Assembly, Resource, "application/javascript");

        Assert.NotNull(asset!.LastModified);
    }

    [Fact]
    public void Load_ReturnsNullForAResourceTheAssemblyDoesNotHave()
    {
        Assert.Null(EmbeddedAsset.Load(typeof(Plugin).Assembly, "Jellyfin.Plugin.MindTheGaps.Web.nope.js", "application/javascript"));
    }
}
