using System;
using System.IO;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// The dashboard is two pages (the report and the settings form), each a shell plus its own body and
// script over the shared stylesheet and markup kit, concatenated at build time into one embedded resource
// per page. Verifies the build assembled both under the names Plugin.GetPages serves them from, and that
// neither carries the other's half: a page loads on its own, so a lookup for the other's element throws.
public class DashboardResourceTests
{
    private const string ReportResource = "Jellyfin.Plugin.MindTheGaps.Web.mindthegaps.report.html";
    private const string SettingsResource = "Jellyfin.Plugin.MindTheGaps.Web.mindthegaps.settings.html";

    [Theory]
    [InlineData(ReportResource)]
    [InlineData(SettingsResource)]
    public void DashboardPage_IsEmbedded_WithEveryPlaceholderReplaced(string resource)
    {
        var html = Read(resource);

        Assert.DoesNotContain("@@MTG_", html, StringComparison.Ordinal);

        // The shared stylesheet and the shared markup kit are folded into both pages.
        Assert.Contains(".cgHdr", html, StringComparison.Ordinal);
        Assert.Contains("function wrap(tag, attrs, innerHtml)", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportPage_CarriesTheReportAndItsActions_ButNotTheSettingsForm()
    {
        var html = Read(ReportResource);

        Assert.Contains("id=\"MindTheGapsPage\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"cgReportPanel\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"cgList\"", html, StringComparison.Ordinal);
        Assert.Contains("function load(page)", html, StringComparison.Ordinal);

        // The library and scan actions belong with the report: they act on the library, not on settings.
        Assert.Contains("id=\"RemoveMinted\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"ResetRotation\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"cgScrollTop\"", html, StringComparison.Ordinal);
        Assert.Contains("function pollRemoval()", html, StringComparison.Ordinal);

        Assert.DoesNotContain("MindTheGapsConfigForm", html, StringComparison.Ordinal);
        Assert.DoesNotContain("function saveConfig(", html, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsPage_CarriesTheForm_ButNotTheReport()
    {
        var html = Read(SettingsResource);

        Assert.Contains("id=\"MindTheGapsSettingsPage\"", html, StringComparison.Ordinal);
        Assert.Contains("MindTheGapsConfigForm", html, StringComparison.Ordinal);
        Assert.Contains("function saveConfig(", html, StringComparison.Ordinal);

        Assert.DoesNotContain("id=\"cgList\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"cgTodoModal\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"RemoveMinted\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("function pollRemoval()", html, StringComparison.Ordinal);
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
        Assert.Contains("configurationpage?name=MindTheGapsSettings", Read(ReportResource), StringComparison.Ordinal);
        Assert.Contains("configurationpage?name=MindTheGaps'", Read(SettingsResource), StringComparison.Ordinal);
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
