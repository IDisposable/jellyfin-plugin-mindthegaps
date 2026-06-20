using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Services.OpenLibrary;

/// <summary>
/// A minimal client for the OpenLibrary public, key-free JSON API. See https://openlibrary.org/dev/docs/api/.
/// </summary>
public sealed class OpenLibraryClient
{
    private const string BaseUrl = "https://openlibrary.org";

    // OpenLibrary caps a single works page; one page of an author's works is plenty for a spike.
    private const int WorksLimit = 100;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OpenLibraryClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenLibraryClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    public OpenLibraryClient(IHttpClientFactory httpClientFactory, ILogger<OpenLibraryClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
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
    /// Lists an author's works via the search endpoint (search.json?author_key=...), which carries the first
    /// publish year (the author-works list does not), so book gaps can get a year in a single call.
    /// </summary>
    /// <param name="authorKey">The author key (for example "OL79034A").</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The author's works, with years where present.</returns>
    public async Task<IReadOnlyList<OpenLibraryWork>> GetAuthorWorksBySearchAsync(string authorKey, CancellationToken cancellationToken)
    {
        var response = await GetAsync<OpenLibrarySearchResponse>(
            string.Create(CultureInfo.InvariantCulture, $"/search.json?author_key={Uri.EscapeDataString(authorKey)}&fields=key,title,first_publish_year&limit={WorksLimit}"),
            cancellationToken).ConfigureAwait(false);

        if (response?.Docs is null)
        {
            return Array.Empty<OpenLibraryWork>();
        }

        return response.Docs
            .Select(d => new OpenLibraryWork
            {
                Key = d.Key,
                Title = d.Title,
                FirstPublishDate = d.FirstPublishYear?.ToString(CultureInfo.InvariantCulture)
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

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(NamedClient.Default);
            using var response = await HttpRetry.SendAsync(
                client,
                // HttpRetry adds the plugin's versioned User-Agent.
                () => new HttpRequestMessage(HttpMethod.Get, BaseUrl + path),
                _logger,
                "OpenLibrary",
                path,
                cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("OpenLibrary GET {Path} returned {Status}", path, response.StatusCode);
                return default;
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                return await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenLibrary GET {Path} failed", path);
            return default;
        }
    }
}
