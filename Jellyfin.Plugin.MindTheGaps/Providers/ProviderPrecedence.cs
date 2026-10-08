using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Providers;

/// <summary>
/// Decides whether another installed plugin already provides an external id or url, so this plugin's own
/// providers for it step aside rather than showing a second field in the metadata editor or a second link on an
/// item page. A dedicated plugin for a provider then takes over by being installed, with nothing to configure.
/// </summary>
public sealed class ProviderPrecedence
{
    private readonly IPluginManager _pluginManager;
    private readonly ILogger<ProviderPrecedence> _logger;
    private readonly Lazy<IReadOnlySet<string>> _claimed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderPrecedence"/> class.
    /// </summary>
    /// <param name="pluginManager">The host plugin manager.</param>
    /// <param name="logger">The logger.</param>
    public ProviderPrecedence(IPluginManager pluginManager, ILogger<ProviderPrecedence> logger)
    {
        _pluginManager = pluginManager;
        _logger = logger;

        // Read once, on first use rather than at construction. Core does create every plugin before it builds
        // the providers that inject this (FindParts, on both ABIs), but reading lazily keeps the answer right
        // whatever that order becomes, and keeps the plugin scan out of a constructor, where a failure would fail
        // the whole plugin. A plugin installed or removed needs a restart before it is loaded or gone anyway.
        _claimed = new Lazy<IReadOnlySet<string>>(Collect);
    }

    /// <summary>
    /// Gets a value indicating whether another enabled plugin provides the given external id key or url
    /// provider name.
    /// </summary>
    /// <param name="key">The provider id key (also the url provider's name), for example "OpenLibrary".</param>
    /// <returns><see langword="true"/> when this plugin's own provider should step aside.</returns>
    public bool ClaimedElsewhere(string key) => _claimed.Value.Contains(key);

    /// <summary>
    /// Finds what a set of plugins provide. A provider type with a public parameterless constructor is created
    /// to read its key or name, which is exact. One that needs constructor arguments cannot be created here,
    /// so its plugin is taken to provide whatever its own name contains: a plugin named "OpenLibrary" with such
    /// a provider claims "OpenLibrary". A plugin with no provider types claims nothing, whatever its name.
    /// </summary>
    /// <param name="plugins">Each other plugin's name and exported types.</param>
    /// <param name="candidates">This plugin's own keys, which the name rule is matched against.</param>
    /// <returns>The keys and names the plugins provide, case-insensitively.</returns>
    internal static IReadOnlySet<string> Claims(IEnumerable<(string Name, IEnumerable<Type> Types)> plugins, IEnumerable<string> candidates)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentNullException.ThrowIfNull(candidates);

        var keys = candidates.ToList();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, types) in plugins)
        {
            var opaque = false;
            foreach (var type in types)
            {
                if (type.IsAbstract || type.IsInterface
                    || !(typeof(IExternalId).IsAssignableFrom(type) || typeof(IExternalUrlProvider).IsAssignableFrom(type)))
                {
                    continue;
                }

                var created = TryCreate(type);
                if (created is null)
                {
                    opaque = true;
                    continue;
                }

                if (created is IExternalId id && !string.IsNullOrEmpty(id.Key))
                {
                    claimed.Add(id.Key);
                }

                if (created is IExternalUrlProvider url && !string.IsNullOrEmpty(url.Name))
                {
                    claimed.Add(url.Name);
                }
            }

            if (opaque)
            {
                var folded = Fold(name);
                foreach (var key in keys.Where(k => folded.Contains(Fold(k), StringComparison.Ordinal)))
                {
                    claimed.Add(key);
                }
            }
        }

        return claimed;
    }

    private static object? TryCreate(Type type)
    {
        if (type.GetConstructor(Type.EmptyTypes) is null)
        {
            return null;
        }

        try
        {
            return Activator.CreateInstance(type);
        }
        catch (Exception ex) when (ex is TargetInvocationException or MemberAccessException or MissingMethodException or TypeLoadException)
        {
            return null;
        }
    }

    private static string Fold(string value)
        => new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    private static IEnumerable<Type> ExportedTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetExportedTypes();
        }
        catch (Exception ex) when (ex is ReflectionTypeLoadException or TypeLoadException or System.IO.FileNotFoundException)
        {
            return [];
        }
    }

    private IReadOnlySet<string> Collect()
    {
        try
        {
            var self = Plugin.Instance?.Id ?? Guid.Empty;
            var others = _pluginManager.Plugins
                .Where(p => p.IsEnabledAndSupported && p.Id != self && p.Instance is not null)
                .Select(p => (p.Name, ExportedTypes(p.Instance!.GetType().Assembly)))
                .ToList();
            var claimed = Claims(others, OwnProviders.Keys);
            if (claimed.Count > 0)
            {
                _logger.LogInformation("Other plugins provide {Providers}; this plugin's own providers for them step aside", string.Join(", ", claimed.Order(StringComparer.OrdinalIgnoreCase)));
            }

            return claimed;
        }
        catch (Exception ex)
        {
            // Never fail a metadata or item-page request over this; showing ours alongside theirs is the worst case.
            _logger.LogWarning(ex, "Could not read the installed plugins' providers");
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
