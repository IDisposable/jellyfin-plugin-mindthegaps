using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Http;

namespace Jellyfin.Plugin.MindTheGaps.Services.OpenLibrary;

/// <summary>
/// A minimal client for the OpenLibrary public, key-free JSON API. See https://openlibrary.org/dev/docs/api/.
/// Implements <see cref="IOpenLibraryWorkDescriptions"/>, the one capability the Web UI needs directly,
/// as a public seam, so this class itself (and the internal types the rest of its surface uses) can stay
/// internal rather than escalating the whole client to public for one method.
/// </summary>
internal sealed class OpenLibraryClient : IOpenLibraryWorkDescriptions
{
    private const string BaseUrl = "https://openlibrary.org";

    // OpenLibrary caps a single works page; one page of an author's works is plenty for a spike.
    private const int WorksLimit = 100;

    // The reading log pages the same way. A shelf is usually one or two pages.
    private const int ReadingLogLimit = 100;

    // The settings type-ahead shows a short list, so a partial query does not flood the dropdown.
    private const int SubjectSuggestionLimit = 10;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CachedApiClient _api;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenLibraryClient"/> class.
    /// </summary>
    /// <param name="api">The cached API client.</param>
    public OpenLibraryClient(CachedApiClient api)
    {
        _api = api;
    }

    /// <summary>
    /// Resolves the best-matching OpenLibrary author key for an author name.
    /// </summary>
    /// <param name="authorName">The author's name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The author key (for example "OL79034A"), or <see langword="null"/>.</returns>
    public async Task<string?> ResolveAuthorKeyAsync(string authorName, CancellationToken cancellationToken)
    {
        var response = await GetAsync<OpenLibraryAuthorSearchResponse>(
            string.Create(CultureInfo.InvariantCulture, $"/search/authors.json?q={Uri.EscapeDataString(authorName)}"),
            cancellationToken).ConfigureAwait(false);

        // Do not take the first result: it is often a different person of the same name. The matcher prefers
        // the shortest exactly-matching name and the most works.
        return OpenLibraryAuthorMatcher.Pick(response?.Docs, authorName);
    }

    /// <summary>
    /// Reads a work's first author key directly from its OpenLibrary record (works/{key}.json), so an owned
    /// book resolves its author without a name search (which hits the namesake problem). Null when the work
    /// or its author cannot be read.
    /// </summary>
    /// <param name="workKey">The work id (for example "OL45804W", with or without the "/works/" prefix).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The author key (for example "OL79034A"), or <see langword="null"/>.</returns>
    public async Task<string?> GetWorkAuthorKeyAsync(string workKey, CancellationToken cancellationToken)
    {
        var bare = LastSegment(workKey);
        if (string.IsNullOrEmpty(bare))
        {
            return null;
        }

        var detail = await GetAsync<OpenLibraryWorkDetail>(
            string.Create(CultureInfo.InvariantCulture, $"/works/{Uri.EscapeDataString(bare)}.json"),
            cancellationToken).ConfigureAwait(false);

        var key = detail?.Authors?
            .Select(a => a.Author?.Key)
            .FirstOrDefault(k => !string.IsNullOrEmpty(k));
        return string.IsNullOrEmpty(key) ? null : LastSegment(key);
    }

    /// <summary>
    /// Reads a work's description directly from its OpenLibrary record (works/{key}.json, the same page
    /// <see cref="GetWorkAuthorKeyAsync"/> reads), for the Web UI's book detail dialog. Null when the work
    /// carries none, or cannot be read.
    /// </summary>
    /// <param name="workKey">The work id (for example "OL45804W", with or without the "/works/" prefix).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The description, trimmed, or <see langword="null"/>.</returns>
    public async Task<string?> GetWorkDescriptionAsync(string workKey, CancellationToken cancellationToken)
    {
        var bare = LastSegment(workKey);
        if (string.IsNullOrEmpty(bare))
        {
            return null;
        }

        var detail = await GetAsync<OpenLibraryWorkDetail>(
            string.Create(CultureInfo.InvariantCulture, $"/works/{Uri.EscapeDataString(bare)}.json"),
            cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(detail?.Description) ? null : detail.Description.Trim();
    }

    /// <summary>
    /// Lists the works tagged with a subject (subjects/{subject}.json), the page used to complete a curated
    /// books set. Each work carries its first publish year and authors, so a gap can get a year and an author
    /// in a single call. Null when the subject cannot be read.
    /// </summary>
    /// <param name="subject">The subject slug (for example "science_fiction").</param>
    /// <param name="limit">The maximum number of works to request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The subject response (its display name and works), or <see langword="null"/>.</returns>
    public Task<OpenLibrarySubjectResponse?> GetSubjectWorksAsync(string subject, int limit, CancellationToken cancellationToken)
    {
        var bounded = limit < 1 ? 1 : limit;
        return GetAsync<OpenLibrarySubjectResponse>(
            string.Create(CultureInfo.InvariantCulture, $"/subjects/{Uri.EscapeDataString(subject)}.json?limit={bounded}"),
            cancellationToken);
    }

    /// <summary>
    /// Searches OpenLibrary subjects by name for the settings type-ahead, returning the top matches as slug
    /// and name pairs (search/subjects.json?q=...). The id is the subject slug (there is no separate numeric
    /// id to key on).
    /// </summary>
    /// <param name="query">The partial subject name typed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The top matches.</returns>
    public async Task<IReadOnlyList<CuratedSetRef>> SearchSubjectsAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var response = await GetAsync<OpenLibrarySubjectSearchResponse>(
            string.Create(CultureInfo.InvariantCulture, $"/search/subjects.json?q={Uri.EscapeDataString(query)}&limit={SubjectSuggestionLimit}"),
            cancellationToken).ConfigureAwait(false);

        var refs = new List<CuratedSetRef>();
        foreach (var doc in response?.Docs ?? [])
        {
            var slug = LastSegment(doc.Key);
            if (!string.IsNullOrEmpty(slug) && !string.IsNullOrEmpty(doc.Name))
            {
                refs.Add(new CuratedSetRef { Id = slug, Name = doc.Name });
            }
        }

        return refs;
    }

    /// <summary>
    /// Gets a subject's display name by its slug, for the chip picker's Resolve. A subject's own page
    /// (subjects/{subject}.json, the same one <see cref="GetSubjectWorksAsync"/> reads) carries the name;
    /// asked here for one work rather than a whole page, since only the name is wanted.
    /// </summary>
    /// <param name="subject">The subject slug.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The subject's display name, or null if not found.</returns>
    public async Task<string?> GetSubjectNameAsync(string subject, CancellationToken cancellationToken)
    {
        var response = await GetSubjectWorksAsync(subject, 1, cancellationToken).ConfigureAwait(false);
        return response?.Name;
    }

    /// <summary>
    /// Lists an author's works via the search endpoint (search.json?author_key=...), which carries the first
    /// publish year and a cover id (the author-works list does not), so book gaps can get a year and a cover
    /// in a single call.
    /// </summary>
    /// <param name="authorKey">The author key (for example "OL79034A").</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The author's works, with years and covers where present.</returns>
    public async Task<IReadOnlyList<OpenLibraryWork>> GetAuthorWorksBySearchAsync(string authorKey, CancellationToken cancellationToken)
    {
        var response = await GetAsync<OpenLibrarySearchResponse>(
            string.Create(CultureInfo.InvariantCulture, $"/search.json?author_key={Uri.EscapeDataString(authorKey)}&fields=key,title,first_publish_year,cover_i&limit={WorksLimit}"),
            cancellationToken).ConfigureAwait(false);

        if (response?.Docs is null)
        {
            return [];
        }

        return response.Docs
            .Select(d => new OpenLibraryWork
            {
                Key = d.Key,
                Title = d.Title,
                FirstPublishDate = d.FirstPublishYear?.ToString(CultureInfo.InvariantCulture),
                CoverId = d.CoverId
            })
            .ToList();
    }

    // The last path segment of an OpenLibrary key (so "/works/OL45804W" yields "OL45804W" and
    // "/authors/OL79034A" yields "OL79034A"), so a prefixed key from one endpoint queries another.
    private static string LastSegment(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        var slash = key.LastIndexOf('/');
        return slash >= 0 && slash < key.Length - 1 ? key[(slash + 1)..] : key;
    }

    /// <summary>
    /// Reads a reader's public "Want to Read" shelf, following the pages until the shelf ends or
    /// <paramref name="maxItems"/> is reached. Needs no credential: OpenLibrary serves a reading log as JSON
    /// when the reader has made it public. Returns null when it cannot be read, so a caller can tell an empty
    /// shelf from an unreachable one.
    /// </summary>
    /// <param name="username">The OpenLibrary username (the part after /people/ in a profile URL).</param>
    /// <param name="maxItems">The most entries to read.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The wanted works, or null.</returns>
    public async Task<IReadOnlyList<OpenLibraryReadingLogWork>?> GetWantToReadAsync(
        string username,
        int maxItems,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        var works = new List<OpenLibraryReadingLogWork>();
        var user = Uri.EscapeDataString(username.Trim());

        for (var page = 1; works.Count < maxItems; page++)
        {
            var response = await GetAsync<OpenLibraryReadingLogResponse>(
                string.Create(CultureInfo.InvariantCulture, $"/people/{user}/books/want-to-read.json?page={page}&limit={ReadingLogLimit}"),
                cancellationToken).ConfigureAwait(false);
            if (response is null)
            {
                return works.Count > 0 ? works : null;
            }

            var entries = response.ReadingLogEntries ?? [];
            foreach (var entry in entries)
            {
                if (entry.Work is { Title.Length: > 0 })
                {
                    works.Add(entry.Work);
                }
            }

            // A short page is the last one. NumFound also bounds it, so a shelf that keeps serving full pages
            // cannot spin forever.
            if (entries.Count < ReadingLogLimit || works.Count >= response.NumFound)
            {
                break;
            }
        }

        return works;
    }

    /// <summary>
    /// Builds the cover URL for a reading-log work, which carries a cover id rather than a URL.
    /// </summary>
    /// <param name="coverId">The cover id, or null.</param>
    /// <returns>The cover URL, or null.</returns>
    public static string? CoverUrl(long? coverId)
        => coverId is > 0
            ? string.Create(CultureInfo.InvariantCulture, $"https://covers.openlibrary.org/b/id/{coverId}-M.jpg")
            : null;

    // CachedApiClient caches the result and adds the plugin's versioned User-Agent via the shared HttpRetry path.
    private Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
        where T : class
        => _api.GetJsonAsync<T>(ServiceNames.OpenLibrary, BaseUrl + path, CachedApiClient.DefaultCacheDuration, _jsonOptions, null, cancellationToken);
}
