using System;
using Jellyfin.Plugin.MindTheGaps.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class DashboardAssetsControllerTests
{
    private static (DashboardAssetsController Controller, IActionResult Result) Get(string name, string? v)
    {
        var controller = new DashboardAssetsController { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        return (controller, controller.Get(name, v));
    }

    private static string CacheControl(DashboardAssetsController controller) => controller.Response.Headers.CacheControl.ToString();

    private static string CurrentVersion(string name)
    {
        var (controller, result) = Get(name, null);
        var file = Assert.IsType<FileContentResult>(result);
        Assert.NotNull(controller);
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(file.FileContents))[..DashboardAssetsController.VersionLength];
    }

    [Theory]
    [InlineData("mindthegaps.css", "text/css")]
    [InlineData("report.js", "application/javascript")]
    [InlineData("settings.js", "application/javascript")]
    public void Get_ServesEachFileWithItsValidators(string name, string contentType)
    {
        var (_, result) = Get(name, null);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal(contentType, file.ContentType);
        Assert.NotEmpty(file.FileContents);
        Assert.NotNull(file.EntityTag);
        Assert.False(file.EntityTag.IsWeak);
        Assert.NotNull(file.LastModified);
    }

    [Fact]
    public void Get_WithTheCurrentContentHash_IsCachedForAYear()
    {
        var (controller, _) = Get("report.js", CurrentVersion("report.js"));

        Assert.Equal("public, max-age=31536000, immutable", CacheControl(controller));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0000000000000000")]
    [InlineData("abc")]
    public void Get_WithoutTheCurrentContentHash_IsRevalidatedInstead(string? version)
    {
        var (controller, _) = Get("report.js", version);

        Assert.Equal("public, no-cache", CacheControl(controller));
    }

    [Fact]
    public void Get_ACurrentHashIsNotEnoughIfItIsTruncatedShort()
    {
        var (controller, _) = Get("report.js", CurrentVersion("report.js")[..8]);

        Assert.Equal("public, no-cache", CacheControl(controller));
    }

    [Theory]
    [InlineData("nope.js")]
    [InlineData("../report.js")]
    [InlineData("Report.js")]
    [InlineData("mindthegaps.report.bundle.js")]
    public void Get_AnythingElse_IsNotFound(string name)
    {
        var (_, result) = Get(name, null);

        Assert.IsType<NotFoundResult>(result);
    }
}
