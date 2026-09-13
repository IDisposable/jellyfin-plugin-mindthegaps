using System;
using System.Globalization;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// The pure half of script injection: adds the person page client script tag to a jellyfin-web index.html.
/// </summary>
internal static class IndexHtmlInjector
{
    // Marks the tag so a second pass (or a tag someone pasted by hand from the settings page) is recognised.
    private const string Marker = "data-mtg-webui";

    /// <summary>
    /// Builds the script tag. The src is relative to index.html's own folder (<c>/web/</c>), so it resolves
    /// correctly when Jellyfin is served under a base URL prefix; the version query defeats a stale cached
    /// copy after a plugin update.
    /// </summary>
    /// <param name="version">The plugin assembly version.</param>
    /// <returns>The tag.</returns>
    public static string ScriptTag(string version)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"<script {Marker} src=\"../{Api.WebUiController.ClientScriptPath}?v={Uri.EscapeDataString(version)}\" defer></script>");

    /// <summary>
    /// Inserts the script tag before the closing body tag, once.
    /// </summary>
    /// <param name="html">The index.html contents.</param>
    /// <param name="version">The plugin assembly version.</param>
    /// <returns>The contents with the tag, or unchanged when it is already there or there is no body to add it to.</returns>
    public static string Inject(string html, string version)
    {
        ArgumentNullException.ThrowIfNull(html);

        if (html.Contains(Marker, StringComparison.Ordinal))
        {
            return html;
        }

        var bodyClose = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (bodyClose < 0)
        {
            return html;
        }

        return string.Concat(html.AsSpan(0, bodyClose), ScriptTag(version), "\n", html.AsSpan(bodyClose));
    }
}
