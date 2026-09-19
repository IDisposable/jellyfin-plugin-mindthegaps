using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// The dashboard is two pages (the report and the settings form), each a shell of markup plus its own script
// bundle, over one shared stylesheet. The build assembles the shells and the bundles as embedded resources;
// the shells reference the bundles and the stylesheet by content-hashed URL (DashboardAssetsController serves
// them), so a browser keeps them. Verifies the build assembled both under the names Plugin.GetPages serves
// them from, and that neither carries the other's half: a page loads on its own, so a lookup for the other's
// element throws.
public class DashboardResourceTests
{
    private const string ReportResource = "Jellyfin.Plugin.MindTheGaps.Web.mindthegaps.report.html";
    private const string SettingsResource = "Jellyfin.Plugin.MindTheGaps.Web.mindthegaps.settings.html";
    private const string ReportBundle = "Jellyfin.Plugin.MindTheGaps.Web.mindthegaps.report.bundle.js";
    private const string SettingsBundle = "Jellyfin.Plugin.MindTheGaps.Web.mindthegaps.settings.bundle.js";
    private const string Stylesheet = "Jellyfin.Plugin.MindTheGaps.Web.mindthegaps.css";

    [Theory]
    [InlineData(ReportResource)]
    [InlineData(SettingsResource)]
    public void DashboardShell_IsEmbedded_WithEveryPlaceholderReplaced_AndReferencesItsFiles(string resource)
    {
        var html = Read(resource);

        Assert.DoesNotContain("@@MTG_", html, StringComparison.Ordinal);
        Assert.Contains("<link rel=\"stylesheet\" href=\"../MindTheGaps/Dashboard/mindthegaps.css?v=", html, StringComparison.Ordinal);

        // The stylesheet and script are files of their own, not part of the shell.
        Assert.DoesNotContain(".cgHdr", html, StringComparison.Ordinal);
        Assert.DoesNotContain("function wrap(tag, attrs, innerHtml)", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ReportBundle)]
    [InlineData(SettingsBundle)]
    public void DashboardBundle_IsOnePrivateScope_CarryingTheSharedMarkupKit(string resource)
    {
        var js = Read(resource);

        Assert.StartsWith("(function () {", js, StringComparison.Ordinal);
        Assert.EndsWith("})();\n", js, StringComparison.Ordinal);
        Assert.DoesNotContain("@@MTG_", js, StringComparison.Ordinal);
        Assert.Contains("function wrap(tag, attrs, innerHtml)", js, StringComparison.Ordinal);
    }

    [Fact]
    public void Stylesheet_IsEmbedded_ForBothPages()
    {
        Assert.Contains(".cgHdr", Read(Stylesheet), StringComparison.Ordinal);
    }

    // The shell names its files by hash, and the controller decides whether a URL is content-addressed by
    // comparing that hash with the file it serves. If the build hashed something other than what ships (the
    // wrong file, or the file before it was written), every page would revalidate forever, or worse, an old
    // page would pin new content under an old address.
    [Theory]
    [InlineData(ReportResource, "report.js")]
    [InlineData(SettingsResource, "settings.js")]
    public void DashboardShell_HashesMatchTheFilesTheControllerServes(string shell, string script)
    {
        var html = Read(shell);

        foreach (var name in new[] { "mindthegaps.css", script })
        {
            var match = Regex.Match(html, "Dashboard/" + Regex.Escape(name) + "\\?v=([0-9a-f]+)");
            Assert.True(match.Success, name);

            var controller = new Api.DashboardAssetsController { ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() } };
            controller.Get(name, match.Groups[1].Value);

            Assert.Equal("public, max-age=31536000, immutable", controller.Response.Headers.CacheControl.ToString());
        }
    }

    [Fact]
    public void ReportPage_CarriesTheReportAndItsActions_ButNotTheSettingsForm()
    {
        var html = Read(ReportResource);
        var js = Read(ReportBundle);

        Assert.Contains("id=\"MindTheGapsPage\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"cgReportPanel\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"cgList\"", html, StringComparison.Ordinal);
        Assert.Contains("function load(page)", js, StringComparison.Ordinal);

        // The library and scan actions belong with the report: they act on the library, not on settings.
        Assert.Contains("id=\"RemoveMinted\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"ResetRotation\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"cgScrollTop\"", html, StringComparison.Ordinal);
        Assert.Contains("function pollRemoval()", js, StringComparison.Ordinal);

        Assert.DoesNotContain("MindTheGapsConfigForm", html, StringComparison.Ordinal);
        Assert.DoesNotContain("function saveConfig(", js, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsPage_CarriesTheForm_ButNotTheReport()
    {
        var html = Read(SettingsResource);
        var js = Read(SettingsBundle);

        Assert.Contains("id=\"MindTheGapsSettingsPage\"", html, StringComparison.Ordinal);
        Assert.Contains("MindTheGapsConfigForm", html, StringComparison.Ordinal);
        Assert.Contains("function saveConfig(", js, StringComparison.Ordinal);

        Assert.DoesNotContain("id=\"cgList\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"cgTodoModal\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"RemoveMinted\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("function pollRemoval()", js, StringComparison.Ordinal);
    }

    // A display:none on the panel would serve a blank settings page.
    [Fact]
    public void SettingsPanel_DoesNotStartHidden()
    {
        Assert.Contains("<div id=\"cgSettingsPanel\">", Read(SettingsResource), StringComparison.Ordinal);
    }

    // The gear and the back button navigate by page name. The report's name is also what shared view
    // links and exported audit dossiers point at.
    [Fact]
    public void Pages_CrossLinkByName()
    {
        Assert.Contains("configurationpage?name=MindTheGapsSettings", Read(ReportBundle), StringComparison.Ordinal);
        Assert.Contains("configurationpage?name=MindTheGaps'", Read(SettingsBundle), StringComparison.Ordinal);
    }

    private static string Read(string resource)
    {
        var assembly = typeof(MindTheGaps.Plugin).Assembly;
        using var stream = assembly.GetManifestResourceStream(resource);
        Assert.NotNull(stream);

        using var reader = new StreamReader(stream!);
        return reader.ReadToEnd();
    }
}
