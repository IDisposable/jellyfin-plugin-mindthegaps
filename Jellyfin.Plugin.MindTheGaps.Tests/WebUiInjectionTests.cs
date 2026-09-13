using Jellyfin.Plugin.MindTheGaps.WebUi;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class WebUiInjectionTests
{
    private const string Index = "<!doctype html><html><head><title>Jellyfin</title></head><body><div id=\"reactRoot\"></div><script defer src=\"main.bundle.js\"></script></body></html>";

    [Fact]
    public void Inject_AddsTheTagBeforeBodyClose_Once()
    {
        var once = IndexHtmlInjector.Inject(Index, "12.0.7.0");

        Assert.Contains("MindTheGaps/WebUi/client.js?v=12.0.7.0", once, System.StringComparison.Ordinal);
        Assert.Contains("data-mtg-webui", once, System.StringComparison.Ordinal);
        Assert.EndsWith("</script>\n</body></html>", once, System.StringComparison.Ordinal);

        // A second pass (or a tag pasted by hand) must not double up.
        var twice = IndexHtmlInjector.Inject(once, "12.0.7.0");
        Assert.Same(once, twice);
        Assert.Equal(1, Count(twice, "client.js"));
    }

    [Fact]
    public void Inject_SrcIsRelativeToTheWebFolder_SoABaseUrlPrefixResolves()
    {
        var tag = IndexHtmlInjector.ScriptTag("1.0");
        Assert.Contains("src=\"../MindTheGaps/WebUi/client.js?v=1.0\"", tag, System.StringComparison.Ordinal);
        Assert.Contains(" defer", tag, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Inject_LeavesADocumentWithoutABodyAlone()
    {
        const string fragment = "<div>not a page</div>";
        Assert.Same(fragment, IndexHtmlInjector.Inject(fragment, "1.0"));
    }

    [Theory]
    [InlineData("/web/index.html", true)]
    [InlineData("/web/", true)]
    [InlineData("/web", true)]
    [InlineData("/jellyfin/web/index.html", true)]
    [InlineData("/WEB/INDEX.HTML", true)]
    [InlineData("/web/main.bundle.js", false)]
    [InlineData("/webhook", false)]
    [InlineData("/Items/abc", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsIndexRequest_MatchesOnlyTheShell(string? path, bool expected)
        => Assert.Equal(expected, WebUiScriptInjection.IsIndexRequest(path));

    private static int Count(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, System.StringComparison.Ordinal); i >= 0; i = haystack.IndexOf(needle, i + needle.Length, System.StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
