using System;
using System.Collections.Generic;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// Finds the JustWatch page for a movie or series the report already holds a link for, keyed by TMDB id, so
/// the detail dialog can open the title itself rather than a search. Only gaps that carry such a link are
/// indexed, which is a few hundred at most, so a rebuild costs one pass over the report and no allocation per
/// gap, and a lookup is a dictionary read.
/// </summary>
/// <remarks>
/// The index is rebuilt when the store's generation moves. A lookup reads the generation without the store's
/// lock, because a save holds that lock across its disk writes and a dialog must not wait behind one; only the
/// first lookup after a change takes it.
/// </remarks>
public sealed class JustWatchLinkIndex
{
    private const string LinkName = "JustWatch";

    private readonly GapStore _store;
    private readonly GenerationMemo<IReadOnlyDictionary<(BaseItemKind Kind, int TmdbId), string>> _index = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="JustWatchLinkIndex"/> class.
    /// </summary>
    /// <param name="store">The gap store the links are read from.</param>
    public JustWatchLinkIndex(GapStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Finds the JustWatch page a gap for this title carries.
    /// </summary>
    /// <param name="kind">The title's kind, <see cref="BaseItemKind.Movie"/> or <see cref="BaseItemKind.Series"/>.</param>
    /// <param name="tmdbId">The TMDB id.</param>
    /// <returns>The title's JustWatch URL, or <see langword="null"/> when no gap carries one.</returns>
    public string? Find(BaseItemKind kind, int tmdbId)
    {
        var (generation, _) = _store.GetGeneration();
        if (!_index.TryGet(generation, out var index))
        {
            var (report, loadedGeneration) = _store.LoadWithGeneration();
            index = _index.GetOrCompute(loadedGeneration, () => Build(report));
        }

        return index.TryGetValue((kind, tmdbId), out var url) ? url : null;
    }

    /// <summary>
    /// Whether a link is a page on JustWatch itself. A link comes from the account's list or from whatever a
    /// host provider emitted, and the dialog opens it, so it is held to https on the site's own domain.
    /// </summary>
    /// <param name="url">The link.</param>
    /// <returns><see langword="true"/> for an https URL on justwatch.com.</returns>
    internal static bool IsJustWatchUrl(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && (uri.Host.Equals("justwatch.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".justwatch.com", StringComparison.OrdinalIgnoreCase));

    internal static IReadOnlyDictionary<(BaseItemKind Kind, int TmdbId), string> Build(GapReport report)
    {
        var index = new Dictionary<(BaseItemKind Kind, int TmdbId), string>();
        foreach (var gap in report.Items)
        {
            if (gap.TargetKind is not (BaseItemKind.Movie or BaseItemKind.Series)
                || gap.Links.Count == 0
                || !gap.ProviderIds.TryGetProviderIdAsInt(ProviderIds.Tmdb, out var tmdbId))
            {
                continue;
            }

            foreach (var link in gap.Links)
            {
                if (string.Equals(link.Name, LinkName, StringComparison.OrdinalIgnoreCase) && IsJustWatchUrl(link.Url))
                {
                    index.TryAdd((gap.TargetKind, tmdbId), link.Url);
                    break;
                }
            }
        }

        return index;
    }
}
