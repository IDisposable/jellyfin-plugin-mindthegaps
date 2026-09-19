using System;
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

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// Answers "what of this person's work don't I have?" for one library person on demand: reads the person's
/// TMDB id off the library item, fetches their credits through the shared (cached) TMDB client, runs the same
/// filmography mapper the scan uses against a fresh ownership index, and drops what the report has
/// dismissed. Independent of the scan, so a person the rotation has not reached yet still gets an answer.
/// </summary>
public sealed class PersonMissingService
{
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
        if (_libraryManager.GetItemById(personId) is not Person person)
        {
            return null;
        }

        var config = Plugin.RequireConfiguration();
        var result = new PersonMissingResult
        {
            PersonId = personId,
            PersonName = person.Name,
            CanSendMovies = isAdministrator && AcquisitionService.RadarrConfigured(config),
            CanSendSeries = isAdministrator && AcquisitionService.SonarrConfigured(config),
            CanTodo = isAdministrator
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
        if (_libraryManager.GetItemById(personId) is not Person person || string.IsNullOrEmpty(gapId))
        {
            return null;
        }

        // The mapper emits one gap per credit; the card merged them, so the gap handed on must carry the
        // merged credit too.
        var credits = (await BuildGapsAsync(person, cancellationToken).ConfigureAwait(false)).Gaps
            .Where(g => string.Equals(g.Id, gapId, StringComparison.Ordinal))
            .ToList();
        var gap = credits.FirstOrDefault();
        if (gap is not null)
        {
            gap.Overview = PersonMissingBuilder.MergedRole(credits);
        }

        return gap;
    }

    private async Task<(System.Collections.Generic.IReadOnlyList<GapItem> Gaps, int? TmdbId, string? Reason)> BuildGapsAsync(Person person, CancellationToken cancellationToken)
    {
        if (!person.TryGetProviderIdAsInt(ProviderIds.Tmdb, out var tmdbId))
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

        if (!_cache.TryGetValue(OwnershipCache.Key, out OwnershipIndex? ownership) || ownership is null)
        {
            ownership = _ownershipIndexBuilder.Build(_kinds);
            _cache.Set(OwnershipCache.Key, ownership, OwnershipCache.Ttl);
        }

        var gaps = FilmographyGapMapper.Build(
            tmdbPerson,
            person.Id.ToString("N", CultureInfo.InvariantCulture),
            person.Name,
            ownership,
            _tmdb.GetPosterUrl,
            minVotes: config.PersonPageMinVotes,
            maxCastOrder: 0,
            maxCredits: int.MaxValue,
            minTvEpisodes: config.PersonPageMinEpisodes).ToList();
        return (gaps, tmdbId, null);
    }
}
