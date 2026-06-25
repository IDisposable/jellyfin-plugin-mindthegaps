using System;
using System.Net;
using System.Text.RegularExpressions;
using MediaBrowser.Common.Extensions;

namespace Jellyfin.Plugin.MindTheGaps.Services.Text;

/// <summary>
/// Turns the lightly-marked-up summaries some providers return into plain text. TVmaze, for example, wraps
/// its summary in <c>&lt;p&gt;...&lt;/p&gt;</c> with the odd <c>&lt;b&gt;</c>/<c>&lt;i&gt;</c>/<c>&lt;br&gt;</c>.
/// The host's own <c>BaseExtensions.StripHtml</c> does the tag removal (and script/style blocks); this only
/// adds the two things it leaves out, which is what we actually want here: a line break (<c>&lt;br&gt;</c>)
/// or paragraph end (<c>&lt;/p&gt;</c>) becomes a newline, and the remaining HTML entities are decoded.
/// Applied centrally in <c>GapItemFactory.Create</c> so a gap's overview is stored plain whatever produced it.
/// </summary>
internal static partial class HtmlText
{
    /// <summary>
    /// Strips HTML from a provider summary, keeping the text. Returns the input unchanged when it is
    /// null/empty or carries no tags.
    /// </summary>
    /// <param name="html">The possibly-HTML text.</param>
    /// <returns>Plain text, or the original value when there is nothing to strip.</returns>
    public static string? ToPlainText(string? html)
    {
        if (string.IsNullOrEmpty(html) || html.IndexOf('<', StringComparison.Ordinal) < 0)
        {
            return html;
        }

        // Turn explicit breaks into newlines, let the host strip the remaining tags, then decode the
        // entities its StripHtml leaves behind (it only handles &nbsp;).
        var text = BreakTags().Replace(html, "\n");
        text = WebUtility.HtmlDecode(text.StripHtml());

        // Tidy: drop trailing spaces on a line, then collapse any run of newlines to a single one (no
        // blank lines, whatever combination of <br>/</p> the source used).
        text = TrailingSpaces().Replace(text, "\n");
        text = RepeatedNewlines().Replace(text, "\n");
        return text.Trim();
    }

    // A line break in any spelling - <br>, <br/>, <br />, and the malformed </br> - or a paragraph end
    // </p>. Not a <p> open, which just gets stripped (so paragraphs are separated by one newline, not two).
    [GeneratedRegex(@"<\s*/?\s*br\s*/?\s*>|<\s*/\s*p\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakTags();

    [GeneratedRegex(@"[ \t]+\n")]
    private static partial Regex TrailingSpaces();

    [GeneratedRegex(@"\n{2,}")]
    private static partial Regex RepeatedNewlines();
}
