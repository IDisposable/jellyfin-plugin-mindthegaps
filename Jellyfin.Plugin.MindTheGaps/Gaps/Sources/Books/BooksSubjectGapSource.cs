using System;
using System.Collections.Generic;
using System.Linq;
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
/// Completes curated books sets: for each configured OpenLibrary subject, lists the subject's works and diffs
/// them against the library by OpenLibrary work id (with an author-and-title name fallback), emitting a
/// <see cref="GapPattern.SetCompletion"/> gap per unowned work. Opt-in; needs at least one configured subject.
/// </summary>
internal sealed class BooksSubjectGapSource : IGapSource, IExploreSource, IConfiguredScopeSource
{
    // OpenLibrary's subject page caps a single request; one page is plenty for set completion, and the cap
    // keeps a broad subject from flooding the list.
    private const int SubjectWorksLimit = 200;

    private readonly OpenLibraryClient _openLibrary;
    private readonly ILogger<BooksSubjectGapSource> _logger;
    private readonly IReadOnlyCollection<ExploreDescriptor> _exploreDescriptors;

    /// <summary>
    /// Initializes a new instance of the <see cref="BooksSubjectGapSource"/> class.
    /// </summary>
    /// <param name="openLibrary">The OpenLibrary client.</param>
    /// <param name="logger">The logger.</param>
    public BooksSubjectGapSource(OpenLibraryClient openLibrary, ILogger<BooksSubjectGapSource> logger)
    {
        _openLibrary = openLibrary;
        _logger = logger;
        _exploreDescriptors = new[]
        {
            new ExploreDescriptor
            {
                Kind = "openlibrarysubject",
                Label = "OpenLibrary subject",
                Source = this,

                // A subject is a free-typed slug, which is exactly what the chip picker and the ad-hoc
                // explore run pass here (Model.CuratedSetRef's id is a string), so an explore run surfaces
                // the subjects the user picked rather than falling back to the configured set.
                Run = (context, subjects, ct) => FindGapsForSubjectsAsync(context, subjects, ct),
                Search = (query, ct) => _openLibrary.SearchSubjectsAsync(query, ct),
                Resolve = (subject, ct) => _openLibrary.GetSubjectNameAsync(subject, ct)
            }
        };
    }

    /// <inheritdoc />
    public string Name => "OpenLibrary subjects";

    /// <inheritdoc />
    public IReadOnlyCollection<BaseItemKind> OwnedKinds { get; } = new[] { BaseItemKind.Book };

    /// <inheritdoc />
    public IReadOnlyCollection<ExploreDescriptor> ExploreDescriptors => _exploreDescriptors;

    /// <inheritdoc />
    public string GapIdPrefix => GapSourceKeys.OpenLibrarySubject.GapPrefix;

    /// <inheritdoc />
    public bool IsEnabled(PluginConfiguration config)
        => config.ScanCuratedBooks && ParseSubjects(config.CuratedOpenLibrarySubjects).Count > 0;

    /// <inheritdoc />
    public bool StillInScope(GapItem item, PluginConfiguration config)
        => config.ScanCuratedBooks
            && ParseSubjects(config.CuratedOpenLibrarySubjects).Any(subject => string.Equals(GapSourceKeys.OpenLibrarySubject.Owner(subject), item.SourceItemId, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    public IAsyncEnumerable<GapItem> FindGapsAsync(
        GapScanContext context,
        CancellationToken cancellationToken)
        => FindGapsForSubjectsAsync(context, ParseSubjects(context.Config.CuratedOpenLibrarySubjects), cancellationToken);

    // Subjects are slugs, not ids, so the numeric ConfigIds parser does not apply: split on commas and trim,
    // dropping blanks and de-duplicating in input order.
    private static IReadOnlyList<string> ParseSubjects(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (seen.Add(part))
            {
                result.Add(part);
            }
        }

        return result;
    }

    private async IAsyncEnumerable<GapItem> FindGapsForSubjectsAsync(
        GapScanContext context,
        IReadOnlyList<string> subjects,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(subjects);

        var total = Math.Max(1, subjects.Count);
        var done = 0;

        foreach (var subject in subjects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ServiceCircuit.IsOpen(ServiceNames.OpenLibrary))
            {
                _logger.LogWarning("OpenLibrary subjects: OpenLibrary is unavailable this run; skipping the remaining subjects");
                break;
            }

            OpenLibrarySubjectResponse? response;
            try
            {
                response = await _openLibrary.GetSubjectWorksAsync(subject, SubjectWorksLimit, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "OpenLibrary subjects: failed to fetch works for subject {Subject}", subject);
                context.ReportProgress((double)++done / total);
                continue;
            }

            if (response?.Works is null)
            {
                context.ReportProgress((double)++done / total);
                continue;
            }

            _logger.LogInformation("OpenLibrary subjects: subject {Subject} returned {Count} works", subject, response.Works.Count);

            foreach (var gap in OpenLibrarySubjectMapper.Build(subject, response.Name, response.Works, context.Ownership, SubjectWorksLimit))
            {
                yield return gap;
            }

            context.ReportProgress((double)++done / total);
        }
    }
}
