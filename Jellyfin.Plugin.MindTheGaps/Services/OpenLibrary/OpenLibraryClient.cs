using System;
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
    /// <returns>The author key (for example "OL23919A"), or <see langword="null"/>.</returns>
    public async Task<string?> ResolveAuthorKeyAsync(string authorName, CancellationToken cancellationToken)
    {
        var response = await GetAsync<OpenLibraryAuthorSearchResponse>(
            string.Create(CultureInfo.InvariantCulture, $"/search/authors.json?q={Uri.EscapeDataString(authorName)}"),
            cancellationToken).ConfigureAwait(false);

        return response?.Docs?.FirstOrDefault(d => !string.IsNullOrEmpty(d.Key))?.Key;
    }

    /// <summary>
    /// Gets an author's works (the abstract titles, not individual editions).
    /// </summary>
    /// <param name="authorKey">The author key (for example "OL23919A").</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The works response, or <see langword="null"/>.</returns>
    public Task<OpenLibraryWorksResponse?> GetAuthorWorksAsync(string authorKey, CancellationToken cancellationToken)
        => GetAsync<OpenLibraryWorksResponse>(
            string.Create(CultureInfo.InvariantCulture, $"/authors/{Uri.EscapeDataString(authorKey)}/works.json?limit={WorksLimit}"),
            cancellationToken);

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
