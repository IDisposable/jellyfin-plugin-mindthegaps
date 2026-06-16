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

        // Admin configuration page.
        yield return new PluginPageInfo
        {
            Name = "MindTheGaps",
            EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", ns)
        };

        // The todo-list dashboard page, surfaced in the main menu. The name must NOT collide
        // case-insensitively with the config page above: the host resolves pages by name with
        // OrdinalIgnoreCase (DashboardController.GetDashboardConfigurationPage), so "mindthegaps"
        // would be shadowed by "MindTheGaps" and never load.
        yield return new PluginPageInfo
        {
            Name = "MindTheGapsReport",
            DisplayName = "Mind the Gaps",
            EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.mindthegaps.html", ns),
            EnableInMainMenu = true,
            MenuIcon = "playlist_add_check"
        };
    }
}
