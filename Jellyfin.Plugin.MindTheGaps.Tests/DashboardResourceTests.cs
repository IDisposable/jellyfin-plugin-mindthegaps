using System.IO;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// The dashboard is authored as a shell plus separate report markup, config markup, CSS, and JS, concatenated
// at build time into one embedded resource. This verifies the build assembled it under the name the plugin
// serves it from, with every part folded in and the placeholders replaced.
public class DashboardResourceTests
{
    private const string ResourceName = "Jellyfin.Plugin.MindTheGaps.Web.mindthegaps.html";

    [Fact]
    public void DashboardResource_IsEmbedded_WithCssAndJsConcatenated()
    {
        var assembly = typeof(MindTheGaps.Plugin).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName);
        Assert.NotNull(stream);

        using var reader = new StreamReader(stream!);
        var html = reader.ReadToEnd();

        // Placeholders are gone (the concatenation ran).
        Assert.DoesNotContain("@@MTG_REPORT@@", html, System.StringComparison.Ordinal);
        Assert.DoesNotContain("@@MTG_CONFIG@@", html, System.StringComparison.Ordinal);
        Assert.DoesNotContain("@@MTG_CSS@@", html, System.StringComparison.Ordinal);
        Assert.DoesNotContain("@@MTG_JS@@", html, System.StringComparison.Ordinal);

        // The shell, the report panel, the config panel, the CSS, and the JS are all present.
        Assert.Contains("id=\"MindTheGapsPage\"", html, System.StringComparison.Ordinal);
        Assert.Contains("id=\"cgReportPanel\"", html, System.StringComparison.Ordinal);
        Assert.Contains("id=\"cgSettingsPanel\"", html, System.StringComparison.Ordinal);
        Assert.Contains(".cgHdr", html, System.StringComparison.Ordinal);
        Assert.Contains("function load(page)", html, System.StringComparison.Ordinal);
    }
}
