using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Tmdb;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Acquisition;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.PersonPage;

/// <summary>
/// Answers "what of this person's work don't I have?" for one library person on demand: reads the person's
/// TMDB id off the library item, fetches their credits through the shared (cached) TMDB client, runs the same
/// filmography mapper the scan uses against a fresh ownership index, and drops what the report has
/// dismissed. Independent of the scan, so a person the rotation has not reached yet still gets an answer.
/// </summary>
public sealed class PersonMissingService
{
    // The ownership index is one library read; cache it briefly so browsing several person pages in a row
    // does not re-read the library each time, while a title added to the library still drops off within a minute.
    private const string OwnershipCacheKey = "mtg-personpage-ownership";
    private static readonly TimeSpan _ownershipTtl = TimeSpan.FromSeconds(60);
    private static readonly BaseItemKind[] _kinds = [BaseItemKind.Movie, BaseItemKind.Series];

    private readonly ILibraryManager _libraryManager;
    private readonly TmdbClient _tmdb;
    private readonly OwnershipIndexBuilder _ownershipIndexBuilder;
    private readonly ResolutionStore _resolutions;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PersonMissingService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PersonMissingService"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="tmdb">The TMDB client.</param>
    /// <param name="ownershipIndexBuilder">Indexes the owned movies and series.</param>
    /// <param name="resolutions">The dismissals, so a gap hidden on the report is hidden here too.</param>
    /// <param name="cache">The memory cache.</param>
    /// <param name="logger">The logger.</param>
    public PersonMissingService(
        ILibraryManager libraryManager,
        TmdbClient tmdb,
        OwnershipIndexBuilder ownershipIndexBuilder,
        ResolutionStore resolutions,
        IMemoryCache cache,
        ILogger<PersonMissingService> logger)
    {
        _libraryManager = libraryManager;
        _tmdb = tmdb;
        _ownershipIndexBuilder = ownershipIndexBuilder;
        _resolutions = resolutions;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Computes the person's unowned filmography.
    /// </summary>
    /// <param name="personId">The Jellyfin person id.</param>
    /// <param name="isAdministrator">Whether the caller is an administrator, which gates the Send buttons.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result, or <see langword="null"/> when the id is not a library person.</returns>
    public async Task<PersonMissingResult?> GetAsync(Guid personId, bool isAdministrator, CancellationToken cancellationToken)
    {
        var person = _libraryManager.GetItemById<Person>(personId);
        if (person is null)
        {
            return null;
        }

        var config = Plugin.RequireConfiguration();
        var result = new PersonMissingResult
        {
            PersonId = personId,
            PersonName = person.Name,
            CanSendMovies = isAdministrator && AcquisitionService.RadarrConfigured(config),
            CanSendSeries = isAdministrator && AcquisitionService.SonarrConfigured(config)
        };
        result.CanSend = result.CanSendMovies || result.CanSendSeries;

        var gaps = await BuildGapsAsync(person, cancellationToken).ConfigureAwait(false);
        if (gaps.Reason is not null)
        {
            result.Reason = gaps.Reason;
            return result;
        }

        result.TmdbId = gaps.TmdbId;
        var (movies, series) = PersonMissingBuilder.Split(gaps.Gaps, person.Name, _resolutions.GetAll());
        result.Movies = movies;
        result.Series = series;
        return result;
    }

    /// <summary>
    /// Rehydrates one of the person's gaps by id, for a Send. Recomputed server-side from the same inputs the
    /// page listed, so a client can only ever send a title this person is actually credited on and the
    /// library actually lacks.
    /// </summary>
    /// <param name="personId">The Jellyfin person id.</param>
    /// <param name="gapId">The gap id the page showed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The gap, or <see langword="null"/> when the person or the gap is not there.</returns>
    public async Task<GapItem?> FindGapAsync(Guid personId, string gapId, CancellationToken cancellationToken)
    {
        var person = _libraryManager.GetItemById<Person>(personId);
        if (person is null || string.IsNullOrEmpty(gapId))
        {
            return null;
        }

        var gaps = await BuildGapsAsync(person, cancellationToken).ConfigureAwait(false);
        return gaps.Gaps.FirstOrDefault(g => string.Equals(g.Id, gapId, StringComparison.Ordinal));
    }

    private async Task<(IReadOnlyList<GapItem> Gaps, int? TmdbId, string? Reason)> BuildGapsAsync(Person person, CancellationToken cancellationToken)
    {
        if (!person.TryGetProviderId(ProviderIds.Tmdb, out var raw)
            || !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tmdbId)
            || tmdbId <= 0)
        {
            return ([], null, "This person has no TMDB id in the library, so their filmography cannot be looked up. Refresh the person's metadata with TheMovieDb enabled.");
        }

        var config = Plugin.RequireConfiguration();
        var tmdbPerson = await _tmdb.GetPersonAsync(tmdbId, config.MetadataLanguage, config.MetadataCountryCode, cancellationToken).ConfigureAwait(false);
        if (tmdbPerson is null)
        {
            _logger.LogWarning("Person page: TMDB returned nothing for person {TmdbId} ({Name})", tmdbId, person.Name);
            return ([], tmdbId, "TMDB has no record for this person right now.");
        }

        var ownership = await _cache.GetOrCreateAsync(OwnershipCacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _ownershipTtl;
            return Task.FromResult(_ownershipIndexBuilder.Build(_kinds));
        }).ConfigureAwait(false);

        var gaps = FilmographyGapMapper.Build(
            tmdbPerson,
            person.Id.ToString("N", CultureInfo.InvariantCulture),
            person.Name,
            ownership!,
            _tmdb.GetPosterUrl,
            config.PersonPageMinVotes,
            maxCastOrder: 0).ToList();
        return (gaps, tmdbId, null);
    }
}
