using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Discogs;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Discogs;

/// <summary>
/// Surfaces missing releases from curated Discogs record labels: for each configured label, lists the
/// label's releases and diffs them against the library by Discogs id, emitting a
/// <see cref="GapPattern.SetCompletion"/> gap per unowned release. Opt-in and experimental; needs a Discogs
/// token and at least one label id.
/// </summary>
public sealed class DiscogsLabelGapSource : IGapSource
{
    // Cap the gaps emitted for one label so a broad label does not flood the list.
    private const int MaxGapsPerLabel = 150;

    private readonly DiscogsClient _discogs;
    private readonly ILogger<DiscogsLabelGapSource> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiscogsLabelGapSource"/> class.
    /// </summary>
    /// <param name="discogs">The Discogs client.</param>
    /// <param name="logger">The logger.</param>
    public DiscogsLabelGapSource(DiscogsClient discogs, ILogger<DiscogsLabelGapSource> logger)
    {
        _discogs = discogs;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Discogs labels";

    /// <inheritdoc />
    public IReadOnlyCollection<BaseItemKind> OwnedKinds { get; } = new[] { BaseItemKind.MusicAlbum };

    /// <inheritdoc />
    public bool IsEnabled(PluginConfiguration config)
        => config.ScanDiscogs
            && !string.IsNullOrEmpty(config.DiscogsToken)
            && ParseLabelIds(config.DiscogsLabelIds).Count > 0;

    /// <inheritdoc />
    public async IAsyncEnumerable<GapItem> FindGapsAsync(
        GapScanContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var labelIds = ParseLabelIds(context.Config.DiscogsLabelIds);
        var total = Math.Max(1, labelIds.Count);
        var done = 0;

        foreach (var labelId in labelIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ServiceCircuit.IsOpen("Discogs"))
            {
                _logger.LogInformation("Discogs labels: Discogs is unavailable this run; skipping the remaining labels");
                break;
            }

            IReadOnlyList<DiscogsRelease> releases;
            try
            {
                releases = await _discogs.GetLabelReleasesAsync(labelId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Discogs labels: failed to fetch releases for label {LabelId}", labelId);
                context.ReportProgress((double)++done / total);
                continue;
            }

            _logger.LogInformation("Discogs labels: label {LabelId} has {Count} releases", labelId, releases.Count);

            // Resolve the label's real name so the set reads "Blue Note" rather than "Label 157"; fall back
            // to the id when the lookup turns up nothing.
            var labelName = await _discogs.GetLabelNameAsync(labelId, cancellationToken).ConfigureAwait(false)
                ?? string.Create(CultureInfo.InvariantCulture, $"Label {labelId}");
            foreach (var gap in DiscogsLabelMapper.Build(labelId, labelName, releases, context.Ownership, MaxGapsPerLabel))
            {
                yield return gap;
            }

            context.ReportProgress((double)++done / total);
        }
    }

    // Parse a comma-separated list of Discogs label ids, ignoring blanks and non-numbers.
    private static IReadOnlyList<long> ParseLabelIds(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<long>();
        }

        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => long.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0L)
            .Where(id => id > 0)
            .Distinct()
            .ToList();
    }
}
