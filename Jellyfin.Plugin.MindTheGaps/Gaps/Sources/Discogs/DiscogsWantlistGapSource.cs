using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Discogs;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Discogs;

/// <summary>
/// Discovery source over a Discogs wantlist: the releases marked as wanted that the library does not hold.
/// Opt-in, needing the username and the Discogs token already configured for the label and artist sources.
/// </summary>
internal sealed class DiscogsWantlistGapSource : IGapSource
{
    // A wantlist is a deliberate list, so it is capped generously.
    private const int MaxGaps = 1000;

    private readonly DiscogsClient _discogs;
    private readonly ILogger<DiscogsWantlistGapSource> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiscogsWantlistGapSource"/> class.
    /// </summary>
    /// <param name="discogs">The Discogs client.</param>
    /// <param name="logger">The logger.</param>
    public DiscogsWantlistGapSource(DiscogsClient discogs, ILogger<DiscogsWantlistGapSource> logger)
    {
        _discogs = discogs;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Discogs wantlist";

    /// <inheritdoc />
    public IReadOnlyCollection<BaseItemKind> OwnedKinds { get; } = new[] { BaseItemKind.MusicAlbum };

    /// <inheritdoc />
    public bool IsEnabled(PluginConfiguration config)
        => config.ScanDiscogsWantlist
            && !string.IsNullOrWhiteSpace(config.DiscogsToken)
            && !string.IsNullOrWhiteSpace(config.DiscogsUsername);

    /// <inheritdoc />
    public async IAsyncEnumerable<GapItem> FindGapsAsync(
        GapScanContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (ServiceCircuit.IsOpen(ServiceNames.Discogs))
        {
            _logger.LogWarning("Discogs wantlist: service unavailable this run");
            yield break;
        }

        var username = context.Config.DiscogsUsername.Trim();
        IReadOnlyList<DiscogsWant>? wants;
        try
        {
            wants = await _discogs.GetWantlistAsync(username, MaxGaps, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Discogs wantlist: failed to read the wantlist of {User}", username);
            yield break;
        }

        if (wants is null)
        {
            _logger.LogWarning(
                "Discogs wantlist: the wantlist of {User} could not be read; it may be private to another account",
                username);
            yield break;
        }

        _logger.LogInformation("Discogs wantlist: {User} wants {Count} releases", username, wants.Count);
        context.ReportProgress(0.5);

        foreach (var gap in DiscogsWantlistMapper.Build(username, wants, context.Ownership, MaxGaps))
        {
            yield return gap;
        }

        context.ReportProgress(1);
    }
}
