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

    /// <inheritdoc />
    public override string Name => "Mind the Gaps";

    /// <inheritdoc />
    public override string Description =>
        "Finds what's missing and related across your library and builds a todo list to complete your collection: collections and franchises, series content, and cast and crew filmographies.";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("8c2a93cc-6cc5-493a-880a-2e67ae50e454");

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        var ns = GetType().Namespace;

        // Admin configuration page. It must be EnableInMainMenu too: jellyfin-web's
        // findBestConfigurationPage (apps/dashboard/features/plugins) resolves the plugin-details
        // "Settings" button to the first candidate that has EnableInMainMenu (DisplayName is not
        // consulted). With only the report page flagged, Settings would open the report; flagging this
        // page and yielding it first makes Settings open the config. The side effect is that this page
        // also appears in the admin dashboard drawer alongside the report, which is a normal place for
        // plugin settings. The name must NOT collide case-insensitively with the report below: the host
        // resolves pages by name with OrdinalIgnoreCase (DashboardController.GetDashboardConfigurationPage).
        yield return new PluginPageInfo
        {
            Name = "MindTheGaps",
            DisplayName = "Mind the Gaps",
            EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", ns),
            EnableInMainMenu = true,
            MenuIcon = "settings"
        };

        // The todo-list dashboard page, surfaced in the main menu.
        yield return new PluginPageInfo
        {
            Name = "MindTheGapsReport",
            DisplayName = "Mind the Gaps ToDo List",
            EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.mindthegaps.html", ns),
            EnableInMainMenu = true,
            MenuIcon = "playlist_add_check"
        };
    }
}
