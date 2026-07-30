using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.VirtualItems;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

/// <summary>
/// Guards the vocabulary the dashboard and the model share. The lists themselves are served from the model
/// now, so these cover what is left that a person still has to keep in step: which domains count as
/// implemented, and how each set kind is worded on screen.
/// </summary>
public class VocabularyTests
{
    [Fact]
    public void EveryMediaDomain_IsClassifiedAsImplementedOrNot()
    {
        // A new enum member has to be a deliberate choice: either it is offered in the Type selector or it
        // is recorded as not filled yet. Neither list may silently omit it.
        var classified = MediaDomains.Implemented.Concat(MediaDomains.NotYetImplemented).ToList();

        Assert.Equal(Enum.GetValues<MediaDomain>().Length, classified.Count);
        Assert.Equal(classified.Count, classified.Distinct().Count());
        foreach (var domain in Enum.GetValues<MediaDomain>())
        {
            Assert.Contains(domain, classified);
        }
    }

    [Fact]
    public void SetKindsInOrder_HoldsOnlyKnownSourceItemTypes()
    {
        var known = SourceTypeConstants();

        foreach (var kind in SourceItemTypes.SetKindsInOrder)
        {
            Assert.Contains(kind, known, StringComparer.Ordinal);
        }

        Assert.Equal(SourceItemTypes.SetKindsInOrder.Count, SourceItemTypes.SetKindsInOrder.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void CuratedListKinds_HoldsOnlyKnownSourceItemTypes()
    {
        var known = SourceTypeConstants();

        foreach (var kind in SourceItemTypes.CuratedListKinds)
        {
            Assert.Contains(kind, known, StringComparer.Ordinal);
        }
    }

    [Fact]
    public void EverySetKind_HasWordingInTheDashboard()
    {
        // The kinds and their order are served; only the wording lives in the page. An unworded kind falls
        // back to its raw name, which is readable but not what we want to ship, so pin it here.
        var js = DashboardScript();
        var labels = Regex.Matches(js, @"var SET_KIND_LABELS = \{(?<body>[^}]*)\}", RegexOptions.Singleline)
            .Select(m => m.Groups["body"].Value)
            .FirstOrDefault();

        Assert.False(string.IsNullOrEmpty(labels), "SET_KIND_LABELS not found in the built dashboard.");

        foreach (var kind in SourceItemTypes.SetKindsInOrder)
        {
            Assert.Contains(kind + ":", labels, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MintableKinds_CoversTheKindsTheMinterAccepts()
    {
        // The dashboard shows Mint from this map, so it has to name every kind the minter can build and
        // the provider id each is keyed under. Episodes are native in core and must stay out.
        var kinds = VirtualItemMinter.MintableKinds;

        Assert.Equal("Tmdb", kinds[nameof(BaseItemKind.Movie)]);
        Assert.Equal("Tmdb", kinds[nameof(BaseItemKind.Series)]);
        Assert.Equal("MusicBrainzReleaseGroup", kinds[nameof(BaseItemKind.MusicAlbum)]);
        Assert.Equal("OpenLibrary", kinds[nameof(BaseItemKind.Book)]);
        Assert.DoesNotContain(nameof(BaseItemKind.Episode), kinds.Keys, StringComparer.Ordinal);
        Assert.Equal(4, kinds.Count);
    }

    private static IReadOnlyList<string> SourceTypeConstants()
        => typeof(SourceItemTypes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

    private static string DashboardScript()
    {
        var assembly = typeof(Plugin).Assembly;
        using var stream = assembly.GetManifestResourceStream("Jellyfin.Plugin.MindTheGaps.Web.mindthegaps.report.html")
            ?? throw new InvalidOperationException("Report page resource not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
