using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.MindTheGaps;

/// <summary>
/// Finds what's missing and related across your library and builds a "todo list"
/// for filling out collections, franchises, series, and filmographies.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <summary>
    /// Gets a value indicating whether detailed external-API logging is enabled, so the verbose request and
    /// response logging is gated on it. Off when no plugin instance or configuration is available.
    /// </summary>
    public static bool DetailedApiLogging => Instance?.Configuration?.DetailedApiLogging == true;

    /// <inheritdoc />
    public override string Name => "Mind the Gaps";

    /// <inheritdoc />
    public override string Description =>
        "Finds what's missing and related across your library and builds a todo list to complete your collection: collections and franchises, series content, and cast and crew filmographies.";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("8c2a93cc-6cc5-493a-880a-2e67ae50e454");

    /// <summary>
    /// Gets the plugin configuration, or throws when the plugin is not initialized. Used instead of falling
    /// back to a fresh default, which would silently ignore the user's saved settings and run as if
    /// unconfigured.
    /// </summary>
    /// <returns>The current configuration.</returns>
    /// <exception cref="InvalidOperationException">The plugin is not initialized.</exception>
    public static PluginConfiguration RequireConfiguration()
        => Instance?.Configuration ?? throw new InvalidOperationException("Mind the Gaps is not initialized.");

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        var ns = GetType().Namespace;

        // Settings first: jellyfin-web's findBestConfigurationPage (apps/dashboard/features/plugins)
        // resolves the plugin-details "Settings" button to the first page flagged EnableInMainMenu, and
        // ignores page names. The report must keep the "MindTheGaps" name: shared view links and exported
        // audit dossiers carry configurationpage?name=MindTheGaps.
        yield return new PluginPageInfo
        {
            Name = "MindTheGapsSettings",
            DisplayName = "Mind the Gaps",
            EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.mindthegaps.settings.html", ns),
            EnableInMainMenu = true,
            MenuIcon = "settings"
        };

        yield return new PluginPageInfo
        {
            Name = "MindTheGaps",
            DisplayName = "Gaps Report",
            EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.mindthegaps.report.html", ns),
            EnableInMainMenu = true,
            MenuIcon = "playlist_add_check"
        };
    }
}
