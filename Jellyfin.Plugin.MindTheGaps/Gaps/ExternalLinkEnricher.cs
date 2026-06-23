using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Folds the host's own external-url providers into each gap's links, so links stay extensible
/// without a hard-coded list or a hard dependency on any other plugin. A throwaway <see cref="BaseItem"/>
/// carrying the gap's provider ids is handed to the host's <see cref="IProviderManager.GetExternalUrls"/>;
/// whatever its registered <c>IExternalUrlProvider</c>s emit (TMDB and IMDb from core, JustWatch from
/// that plugin if it is installed, anything else later) is merged in. The hand-built
/// <see cref="ProviderLinks"/> list stays as a fallback for what core does not ship a provider for
/// (TheTVDB, and season/episode urls that need a Series the synthetic item does not have).
/// </summary>
public sealed class ExternalLinkEnricher
{
    private readonly IProviderManager _providerManager;
    private readonly IItemTypeLookup _itemTypeLookup;
    private readonly ILogger<ExternalLinkEnricher> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalLinkEnricher"/> class.
    /// </summary>
    /// <param name="providerManager">The host provider manager.</param>
    /// <param name="itemTypeLookup">The host's <see cref="BaseItemKind"/> to type-name table, used to
    /// synthesize kinds outside the fast path.</param>
    /// <param name="logger">The logger.</param>
    public ExternalLinkEnricher(IProviderManager providerManager, IItemTypeLookup itemTypeLookup, ILogger<ExternalLinkEnricher> logger)
    {
        _providerManager = providerManager;
        _itemTypeLookup = itemTypeLookup;
        _logger = logger;
    }

    /// <summary>
    /// Merges host-provided links with a gap's existing (fallback) links. Host links win on name; the
    /// fallback fills in any names the host did not produce. Order is host links first (in their order),
    /// then the fallback links the host did not cover. Name matching is case-insensitive.
    /// </summary>
    /// <param name="hostLinks">The links the host's providers emitted.</param>
    /// <param name="fallbackLinks">The hand-built links to fall back to.</param>
    /// <returns>The merged list.</returns>
    public static IReadOnlyList<ExternalLink> Merge(IReadOnlyList<ExternalLink> hostLinks, IReadOnlyList<ExternalLink> fallbackLinks)
    {
        var byName = new Dictionary<string, ExternalLink>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<ExternalLink>();
        foreach (var link in hostLinks)
        {
            if (byName.TryAdd(link.Name, link))
            {
                ordered.Add(link);
            }
        }

        foreach (var link in fallbackLinks)
        {
            if (byName.TryAdd(link.Name, link))
            {
                ordered.Add(link);
            }
        }

        return ordered;
    }

    /// <summary>
    /// Builds a throwaway <see cref="BaseItem"/> of the given kind carrying the supplied provider ids,
    /// so the host's external-url providers can read those ids off it. The kinds a gap actually targets
    /// get a direct constructor; anything else falls back to the host's own kind to type table, so a
    /// future kind still produces links without a code change. Returns <see langword="null"/> for a kind
    /// that cannot be instantiated (unknown, abstract, no parameterless constructor).
    /// </summary>
    /// <param name="kind">The kind to synthesize.</param>
    /// <param name="providerIds">The provider ids to stamp on the item (empty values are skipped).</param>
    /// <param name="itemTypeLookup">The host kind to type-name table for the fallback path.</param>
    /// <returns>The synthesized item, or <see langword="null"/>.</returns>
    public static BaseItem? Synthesize(BaseItemKind kind, IReadOnlyDictionary<string, string> providerIds, IItemTypeLookup itemTypeLookup)
    {
        BaseItem? item = kind switch
        {
            BaseItemKind.Movie => new Movie(),
            BaseItemKind.Series => new Series(),
            BaseItemKind.Season => new Season(),
            BaseItemKind.Episode => new Episode(),
            BaseItemKind.BoxSet => new BoxSet(),
            BaseItemKind.Person => new Person(),
            _ => CreateByKind(kind, itemTypeLookup)
        };

        if (item is null)
        {
            return null;
        }

        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in providerIds)
        {
            if (!string.IsNullOrEmpty(pair.Value))
            {
                ids[pair.Key] = pair.Value;
            }
        }

        item.ProviderIds = ids;
        return item;
    }

    /// <summary>
    /// Merges the host's external links into every gap's link list in place.
    /// </summary>
    /// <param name="gaps">The gaps to enrich.</param>
    public void Enrich(IReadOnlyList<GapItem> gaps)
    {
        foreach (var gap in gaps)
        {
            var hostLinks = HostLinksFor(gap);
            if (hostLinks.Count == 0)
            {
                continue;
            }

            gap.Links = Merge(hostLinks, gap.Links);
        }
    }

    // Resolve a BaseItemKind we do not construct directly via core's IItemTypeLookup, which maps the
    // kind to a type name; the entity types all live in BaseItem's assembly. Anything uninstantiable
    // (abstract, no parameterless constructor) just yields null and the gap keeps its fallback links.
    private static BaseItem? CreateByKind(BaseItemKind kind, IItemTypeLookup itemTypeLookup)
    {
        if (!itemTypeLookup.BaseItemKindNames.TryGetValue(kind, out var typeName) || string.IsNullOrEmpty(typeName))
        {
            return null;
        }

        var type = typeof(BaseItem).Assembly.GetType(typeName);
        if (type is null || type.IsAbstract || !typeof(BaseItem).IsAssignableFrom(type))
        {
            return null;
        }

        try
        {
            return Activator.CreateInstance(type) as BaseItem;
        }
        catch (Exception ex) when (ex is MissingMethodException or MemberAccessException or TargetInvocationException)
        {
            return null;
        }
    }

    private IReadOnlyList<ExternalLink> HostLinksFor(GapItem gap)
    {
        var item = Synthesize(gap.TargetKind, gap.ProviderIds, _itemTypeLookup);
        if (item is null)
        {
            return [];
        }

        // For a missing season/episode, point the synthetic item at its owning series (an owned library
        // item) so core's url providers, which reach through Series for the series id and read the
        // season/episode numbers off the item, can emit proper season/episode urls instead of nothing.
        TryLinkParentSeries(item, gap);

        var links = new List<ExternalLink>();
        try
        {
            foreach (var url in _providerManager.GetExternalUrls(item))
            {
                if (!string.IsNullOrEmpty(url.Name) && !string.IsNullOrEmpty(url.Url))
                {
                    links.Add(new ExternalLink(url.Name, url.Url));
                }
            }
        }
        catch (Exception ex)
        {
            // One misbehaving provider should never sink the scan; fall back to the gap's own links.
            _logger.LogDebug(ex, "Host external-url lookup failed for '{Name}'", gap.Name);
        }

        return links;
    }

    // Set the synthetic season/episode's owning series id (and season/episode numbers) so core can
    // resolve the real owned Series via the host library manager and build the right url. The owning
    // series' guid is on the gap (SourceItemId); the episode number comes from the gap id (see ADR-0008).
    // Fully guarded: any failure leaves the item as it was and the gap keeps its fallback links.
    private void TryLinkParentSeries(BaseItem item, GapItem gap)
    {
        try
        {
            if (item is not (Episode or Season))
            {
                return;
            }

            if (string.IsNullOrEmpty(gap.SourceItemId) || !Guid.TryParseExact(gap.SourceItemId, "N", out var seriesId))
            {
                return;
            }

            if (item is Episode episode)
            {
                episode.SeriesId = seriesId;
                if (gap.Season is int season)
                {
                    episode.ParentIndexNumber = season;
                }

                if (ParseEpisodeNumber(gap.Id) is int number)
                {
                    episode.IndexNumber = number;
                }
            }
            else if (item is Season seasonItem)
            {
                seasonItem.SeriesId = seriesId;
                if (gap.Season is int season)
                {
                    seasonItem.IndexNumber = season;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not link synthetic item to its series for '{Name}'", gap.Name);
        }
    }

    // The episode gap id ends with "...eNN" (SeriesGapKey.Episode), so the trailing digits after the last
    // 'e' are the episode number. Returns null for any id that does not match that shape.
    private static int? ParseEpisodeNumber(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        // Case-insensitive: accept both "...eNN" and "...ENN". Upper-casing does not move indices, so the
        // position found in the upper-cased copy slices the original.
        var idx = id.ToUpperInvariant().LastIndexOf('E');
        if (idx < 0 || idx == id.Length - 1)
        {
            return null;
        }

        var tail = id[(idx + 1)..];
        return int.TryParse(tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
    }
}
