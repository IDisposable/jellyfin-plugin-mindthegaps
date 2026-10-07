using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// Reads a user's home screen slot order from the server's own display preferences, the record jellyfin-web
/// saves its home screen settings to, so the home rows can follow a section type without the script asking
/// for it separately.
/// </summary>
public sealed class HomeSections
{
    // jellyfin-web's userSettings record: the id it saves under (hashed to an item id the way core's
    // DisplayPreferencesController does for a non-Guid id) and its client name.
    private const string PreferencesId = "usersettings";
    private const string Client = "emby";

    private readonly IDisplayPreferencesManager _preferences;
    private readonly ILogger<HomeSections> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="HomeSections"/> class.
    /// </summary>
    /// <param name="preferences">The server's display preferences.</param>
    /// <param name="logger">The logger.</param>
    public HomeSections(IDisplayPreferencesManager preferences, ILogger<HomeSections> logger)
    {
        _preferences = preferences;
        _logger = logger;
    }

    /// <summary>
    /// The user's home slots in order, when the placement follows a section type.
    /// </summary>
    /// <param name="userId">The signed-in user, or null for a request without one.</param>
    /// <param name="placement">The placement <see cref="HomePlacement.Discover"/> or <see cref="HomePlacement.Wanted"/> returned.</param>
    /// <returns>One section type per slot, or null when the placement needs none or it could not be read
    /// (the rows then go to the bottom).</returns>
    public IReadOnlyList<string>? For(Guid? userId, string placement)
    {
        if (userId is not { } id || !HomePlacement.FollowsSection(placement))
        {
            return null;
        }

        try
        {
            var prefs = _preferences.GetDisplayPreferences(id, PreferencesId.GetMD5(), Client);
            return HomePlacement.Resolve(prefs.HomeSections.Select(s => (s.Order, s.Type.ToString())));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not read the home screen sections of user {UserId}", id);
            return null;
        }
    }
}
