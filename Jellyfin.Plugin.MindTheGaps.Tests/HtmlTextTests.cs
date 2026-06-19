using Jellyfin.Plugin.MindTheGaps.Services.Text;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// HtmlText turns the lightly-marked-up summaries some providers return (TVmaze especially) into plain text.
public class HtmlTextTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("Already plain text.", "Already plain text.")]
    public void ToPlainText_LeavesNonHtmlUntouched(string? input, string? expected)
    {
        Assert.Equal(expected, HtmlText.ToPlainText(input));
    }

    [Fact]
    public void ToPlainText_StripsParagraphWrapper()
    {
        Assert.Equal("Hello world.", HtmlText.ToPlainText("<p>Hello world.</p>"));
    }

    [Fact]
    public void ToPlainText_TurnsParagraphEndAndBreakIntoNewlines()
    {
        Assert.Equal("One.\nTwo.", HtmlText.ToPlainText("<p>One.</p><p>Two.</p>"));
        Assert.Equal("Line one\nLine two", HtmlText.ToPlainText("Line one<br>Line two"));
        Assert.Equal("A\nB", HtmlText.ToPlainText("A<br />B"));
    }

    [Theory]
    [InlineData("A<br>B")]
    [InlineData("A<br/>B")]
    [InlineData("A<br />B")]
    [InlineData("A</br>B")]
    public void ToPlainText_HandlesEveryBreakSpelling(string input)
    {
        Assert.Equal("A\nB", HtmlText.ToPlainText(input));
    }

    [Fact]
    public void ToPlainText_DropsInlineTagsButKeepsTheirText()
    {
        Assert.Equal("Bold and italic.", HtmlText.ToPlainText("<b>Bold</b> and <i>italic</i>."));
    }

    [Fact]
    public void ToPlainText_DecodesEntities()
    {
        Assert.Equal("Ben & Jerry <3", HtmlText.ToPlainText("<p>Ben &amp; Jerry &lt;3</p>"));
    }

    [Fact]
    public void ToPlainText_CollapsesNewlineRunsToOne()
    {
        // No "\n\n" survives, whatever combination of <br>/</p> the source piled up.
        Assert.Equal("A\nB", HtmlText.ToPlainText("<p>A</p><br><br><br><p>B</p>"));
    }
}
