using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// Two wiring contracts the compiler cannot enforce and that fail only at runtime, on the dashboard, where
// the failure looks like a broken plugin rather than a mistake in one file.
public class PluginContractTests
{
    private const string SettingsHtml = "Jellyfin.Plugin.MindTheGaps.Web.mindthegaps.settings.html";

    // Config the settings form deliberately does not own an input for. Each is still saved, just not from a
    // plain field, so the cross-check below has to be told about it rather than silently allowing gaps.
    private static readonly Dictionary<string, string> _notPlainFields = new(StringComparer.Ordinal)
    {
        ["CuratedCompanyIds"] = "studio chip picker",
        ["CuratedKeywordIds"] = "keyword chip picker",
        ["DiscogsLabelIds"] = "label chip picker",
        ["MdbListListIds"] = "MDBList chip picker",
        ["TmdbSessionId"] = "minted by the TMDB connect wizard, never shown"
    };

    [Fact]
    public void EveryControllerHasAPublicConstructor()
    {
        // ASP.NET activates a controller through ActivatorUtilities, which only considers public
        // constructors. An internal one compiles happily (and is forced if an injected service is internal),
        // then throws "unable to resolve" on the first request to that route. Nothing else catches it.
        var controllers = typeof(Plugin).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("Controller", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(controllers);
        Assert.All(controllers, c => Assert.True(
            c.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length > 0,
            $"{c.Name} has no public constructor, so ASP.NET cannot activate it. Every service it injects has to be public too."));
    }

    [Fact]
    public void EveryInjectedServiceIsPublic()
    {
        // The reason a controller constructor goes internal in the first place: an internal parameter type
        // makes a public constructor a compile error, so the constructor gets quietly narrowed instead.
        var parameters = typeof(Plugin).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("Controller", StringComparison.Ordinal))
            .SelectMany(c => c.GetConstructors())
            .SelectMany(ctor => ctor.GetParameters())
            .Select(p => p.ParameterType)
            .Where(t => t.Assembly == typeof(Plugin).Assembly)
            .Distinct();

        Assert.All(parameters, t => Assert.True(t.IsPublic, $"{t.Name} is injected into a controller, so it has to be public."));
    }

    [Fact]
    public void EverySettableConfigPropertyIsWiredIntoTheSettingsPage()
    {
        // Adding a property to PluginConfiguration and forgetting one of the three places it has to appear
        // (the input, the load half, the save half) leaves a setting that silently resets on every save.
        var page = Read(SettingsHtml);
        var missing = new List<string>();

        foreach (var name in SettableConfigProperties())
        {
            if (_notPlainFields.ContainsKey(name))
            {
                continue;
            }

            if (!page.Contains($"id=\"{name}\"", StringComparison.Ordinal))
            {
                missing.Add($"{name}: no input on the settings page");
            }
            else if (!page.Contains($"config.{name} = ", StringComparison.Ordinal))
            {
                missing.Add($"{name}: never written back on save");
            }
            else if (!page.Contains($"= config.{name}", StringComparison.Ordinal))
            {
                missing.Add($"{name}: never read on load");
            }
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void TheExemptionsAreStillRealConfigProperties()
    {
        // Keeps the list above honest: a renamed or deleted property should not leave a stale excuse behind.
        var settable = SettableConfigProperties().ToHashSet(StringComparer.Ordinal);
        Assert.All(_notPlainFields.Keys, name => Assert.Contains(name, settable));
    }

    private static IEnumerable<string> SettableConfigProperties()
        => typeof(PluginConfiguration)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.DeclaringType == typeof(PluginConfiguration))
            .Select(p => p.Name);

    private static string Read(string resource)
    {
        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream(resource);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        return reader.ReadToEnd();
    }
}
