using System;
using System.Reflection;
using MediaBrowser.Common.Plugins;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Services;

/// <summary>
/// Best-effort, guarded reflection into another installed plugin's configuration to read an API key when
/// this plugin's own is blank. There is no supported host API for reading another plugin's secrets, so this
/// reflects into the foreign plugin's <c>Configuration</c> by assembly name and property name; any failure
/// (the plugin absent, disabled, its config property renamed, a type mismatch) returns null and never
/// throws. The caller must already have checked that reuse is opted in and its own key is blank. Fragile and
/// version-coupled by design; see docs/credentials-spike.md.
/// </summary>
public sealed class InstalledPluginCredentials
{
    private const string TvdbPluginAssembly = "Jellyfin.Plugin.Tvdb";

    // Tried in order; the in-box TheTVDB plugin's subscriber key has gone by a few names across versions.
    private static readonly string[] _tvdbKeyPropertyNames = { "SubscriberPIN", "SubscriberPin", "ApiKey" };

    private readonly IPluginManager _pluginManager;
    private readonly ILogger<InstalledPluginCredentials> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="InstalledPluginCredentials"/> class.
    /// </summary>
    /// <param name="pluginManager">The host plugin manager, used to enumerate installed plugins.</param>
    /// <param name="logger">The logger.</param>
    public InstalledPluginCredentials(IPluginManager pluginManager, ILogger<InstalledPluginCredentials> logger)
    {
        _pluginManager = pluginManager;
        _logger = logger;
    }

    /// <summary>
    /// Returns the installed TheTVDB plugin's subscriber key/PIN if one is present and non-empty, or null on
    /// any failure (plugin absent, disabled, property renamed, or other reflection error).
    /// </summary>
    /// <returns>The discovered key, or null.</returns>
    public string? TryGetTvdbApiKey() => TryReadPluginConfigString(TvdbPluginAssembly, _tvdbKeyPropertyNames);

    private string? TryReadPluginConfigString(string assemblyName, string[] propertyNames)
    {
        try
        {
            foreach (var local in _pluginManager.Plugins)
            {
                var instance = local.Instance;
                if (instance is null)
                {
                    continue;
                }

                var type = instance.GetType();
                if (!string.Equals(type.Assembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var config = type.GetProperty("Configuration", BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance);
                if (config is null)
                {
                    return null;
                }

                foreach (var name in propertyNames)
                {
                    if (config.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(config) is string value
                        && !string.IsNullOrWhiteSpace(value))
                    {
                        _logger.LogInformation("Reusing an API key from installed plugin '{Plugin}' (property '{Property}')", instance.Name, name);
                        return value;
                    }
                }

                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Best-effort credential reuse failed for {Assembly}", assemblyName);
        }

        return null;
    }
}
