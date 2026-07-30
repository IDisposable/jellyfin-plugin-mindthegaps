using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using Jellyfin.Plugin.MindTheGaps.Services.OpenLibrary;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Books;

/// <summary>
/// Discovery source over a reader's OpenLibrary "Want to Read" shelf: the books they have marked and the
/// library does not hold. Opt-in and keyless, needing only the username, because OpenLibrary serves a public
/// reading log as JSON.
/// </summary>
internal sealed class OpenLibraryWantToReadGapSource : IGapSource
{
    // A want-to-read shelf is a deliberate list, so it is capped generously.
    private const int MaxGaps = 1000;

    private readonly OpenLibraryClient _openLibrary;
    private readonly ILogger<OpenLibraryWantToReadGapSource> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenLibraryWantToReadGapSource"/> class.
    /// </summary>
    /// <param name="openLibrary">The OpenLibrary client.</param>
    /// <param name="logger">The logger.</param>
    public OpenLibraryWantToReadGapSource(OpenLibraryClient openLibrary, ILogger<OpenLibraryWantToReadGapSource> logger)
    {
        _openLibrary = openLibrary;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "OpenLibrary want to read";

    /// <inheritdoc />
    public IReadOnlyCollection<BaseItemKind> OwnedKinds { get; } = new[] { BaseItemKind.Book };

    /// <inheritdoc />
    public bool IsEnabled(PluginConfiguration config)
        => config.ScanOpenLibraryWantToRead && !string.IsNullOrWhiteSpace(config.OpenLibraryUsername);

    /// <inheritdoc />
    public async IAsyncEnumerable<GapItem> FindGapsAsync(
        GapScanContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (ServiceCircuit.IsOpen(ServiceNames.OpenLibrary))
        {
            _logger.LogWarning("OpenLibrary want to read: service unavailable this run");
            yield break;
        }

        var username = context.Config.OpenLibraryUsername.Trim();
        IReadOnlyList<OpenLibraryReadingLogWork>? works;
        try
        {
            works = await _openLibrary.GetWantToReadAsync(username, MaxGaps, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "OpenLibrary want to read: failed to read the shelf of {User}", username);
            yield break;
        }

        if (works is null)
        {
            _logger.LogWarning(
                "OpenLibrary want to read: the shelf of {User} could not be read; the reading log may be private",
                username);
            yield break;
        }

        _logger.LogInformation("OpenLibrary want to read: {User} wants {Count} books", username, works.Count);
        context.ReportProgress(0.5);

        foreach (var gap in OpenLibraryWantToReadMapper.Build(username, works, context.Ownership, MaxGaps))
        {
            yield return gap;
        }

        context.ReportProgress(1);
    }
}
