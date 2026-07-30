using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using Jellyfin.Plugin.MindTheGaps.Services.OpenLibrary;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Books;

/// <summary>
/// Spike: finds other works in an owned book's author bibliography (OpenLibrary) that are missing from
/// the library. Jellyfin's Book metadata is thin (often no stable provider id, author carried only as a
/// person/tag), so this is a deliberately modest slice: it resolves the author by name, lists that
/// author's works, and diffs them against owned books by OpenLibrary work key.
/// </summary>
internal sealed class BooksBibliographyGapSource : IGapSource, ISetContentSource
{
    // OpenLibrary is keyless but still rate-limited; each author is two calls, so cap distinct authors.
    private const int MaxAuthors = 100;

    private readonly ILibraryManager _libraryManager;
    private readonly OpenLibraryClient _openLibrary;
    private readonly ILogger<BooksBibliographyGapSource> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BooksBibliographyGapSource"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="openLibrary">The OpenLibrary client.</param>
    /// <param name="logger">The logger.</param>
    public BooksBibliographyGapSource(
        ILibraryManager libraryManager,
        OpenLibraryClient openLibrary,
        ILogger<BooksBibliographyGapSource> logger)
    {
        _libraryManager = libraryManager;
        _openLibrary = openLibrary;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Bibliography";

    /// <inheritdoc />
    public IReadOnlyCollection<BaseItemKind> OwnedKinds { get; } = new[] { BaseItemKind.Book };

    /// <inheritdoc />
    public string GapIdPrefix => GapSourceKeys.Bibliography.GapPrefix;

    /// <inheritdoc />
    public bool IsEnabled(PluginConfiguration config) => config.ScanBooks;

    /// <inheritdoc />
    public bool Claims(BaseItem owner)
        => owner is not null && owner.GetBaseItemKind() == BaseItemKind.Book;

    /// <inheritdoc />
    public async IAsyncEnumerable<GapItem> FindGapsAsync(
        GapScanContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var books = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Book },
            Recursive = true
        });

        // One book per author is enough: every book by the same author yields the same bibliography.
        var seenAuthors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processed = 0;
        var index = 0;

        foreach (var book in books)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.ReportProgress((double)index++ / Math.Max(1, books.Count));

            if (ServiceCircuit.IsOpen(ServiceNames.OpenLibrary))
            {
                _logger.LogWarning("Bibliography: OpenLibrary is unavailable this run; skipping the remaining authors");
                break;
            }

            if (processed >= MaxAuthors)
            {
                _logger.LogInformation("Bibliography: reached author cap ({Cap})", MaxAuthors);
                break;
            }

            var authorName = ResolveAuthorName(book);
            if (string.IsNullOrEmpty(authorName) || !seenAuthors.Add(authorName))
            {
                continue;
            }

            processed++;

            var gaps = await CheckOneAsync(book, context, cancellationToken).ConfigureAwait(false);
            if (gaps is null)
            {
                // Nothing determined for this author (unresolvable, or OpenLibrary failed). Skip to the
                // next book rather than ending the source's whole pass.
                continue;
            }

            foreach (var gap in gaps)
            {
                yield return gap;
            }
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GapItem>?> CheckOneAsync(BaseItem owner, GapScanContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(context);

        var authorName = ResolveAuthorName(owner);
        if (string.IsNullOrEmpty(authorName))
        {
            // Nothing to resolve by, so nothing was determined about this author's bibliography.
            return null;
        }

        string? authorKey;
        IReadOnlyList<OpenLibraryWork> works;
        try
        {
            // Prefer resolving the author from the owned book's own OpenLibrary work id: that reads the
            // work's author directly and skips the name search (where a common name resolves the wrong
            // namesake). Fall back to the name search only when the book carries no work id.
            var ownedWorkId = OwnedWorkId(owner);
            authorKey = string.IsNullOrEmpty(ownedWorkId)
                ? null
                : await _openLibrary.GetWorkAuthorKeyAsync(ownedWorkId, cancellationToken).ConfigureAwait(false);
            authorKey ??= await _openLibrary.ResolveAuthorKeyAsync(authorName, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(authorKey))
            {
                return null;
            }

            works = await _openLibrary.GetAuthorWorksBySearchAsync(authorKey, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Bibliography: OpenLibrary lookup failed for author {Author}", authorName);
            return null;
        }

        if (works.Count == 0)
        {
            // OpenLibrary answered and listed nothing, which is a real answer.
            return [];
        }

        return OpenLibraryMapper.Build(
            authorKey,
            authorName,
            works,
            owner.Id.ToString("N", CultureInfo.InvariantCulture),
            context.Ownership,
            context.Config.MaxRelatedPerItem).ToList();
    }

    // The owned book's OpenLibrary work id, if its metadata carries one (the OpenLibrary metadata plugin
    // stores the work key under this provider id). Lets the author be resolved from the work directly.
    private static string? OwnedWorkId(BaseItem book)
        => book.ProviderIds.TryGetValue(ProviderIds.OpenLibrary, out var id) && !string.IsNullOrEmpty(id)
            ? id
            : null;

    private string? ResolveAuthorName(BaseItem book)
    {
        // Books in Jellyfin carry their author as a Person with type "Author"; fall back to the first
        // listed person when the role is unset.
        var people = _libraryManager.GetPeople(book);
        string? firstPerson = null;
        foreach (var person in people)
        {
            if (string.IsNullOrEmpty(person.Name))
            {
                continue;
            }

            firstPerson ??= person.Name;
            if (person.Type == PersonKind.Author
                || string.Equals(person.Role, "Author", StringComparison.OrdinalIgnoreCase))
            {
                return person.Name;
            }
        }

        return firstPerson;
    }
}
