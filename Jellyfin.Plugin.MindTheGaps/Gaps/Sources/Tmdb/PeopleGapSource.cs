using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using TmdbPerson = TMDbLib.Objects.People.Person;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Tmdb;

/// <summary>
/// Finds movies from an owned person's filmography (TMDB person credits) that are missing
/// from the library. Covers acting roles and key crew jobs (director/writer).
/// </summary>
public sealed class PeopleGapSource : IGapSource
{
    // Fallback cap when the configured value is not set; TMDB person fetch is one cached call each.
    private const int DefaultMaxPeople = 1000;

    private readonly ILibraryManager _libraryManager;
    private readonly TmdbClient _tmdb;
    private readonly ILogger<PeopleGapSource> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PeopleGapSource"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="tmdb">The TMDB client.</param>
    /// <param name="logger">The logger.</param>
    public PeopleGapSource(
        ILibraryManager libraryManager,
        TmdbClient tmdb,
        ILogger<PeopleGapSource> logger)
    {
        _libraryManager = libraryManager;
        _tmdb = tmdb;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Filmography";

    /// <inheritdoc />
    public IReadOnlyCollection<BaseItemKind> OwnedKinds { get; } = new[] { BaseItemKind.Movie };

    /// <inheritdoc />
    public bool IsEnabled(PluginConfiguration config) => config.ScanPeople;

    /// <inheritdoc />
    public async IAsyncEnumerable<GapItem> FindGapsAsync(
        GapScanContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var language = context.Config.MetadataLanguage;
        var country = context.Config.MetadataCountryCode;
        var cap = context.Config.MaxFilmographyPeople > 0 ? context.Config.MaxFilmographyPeople : DefaultMaxPeople;

        var people = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Person },
            Recursive = true
        });

        // Order people by how many owned movies/series credit them, so the per-run cap keeps the creators
        // the library has the most work from. The default item order is SortName (alphabetical), so a flat
        // cap on that order would only ever reach the "A" names on a large library.
        var ordered = OrderByRelevance(people, cancellationToken);

        var processed = 0;
        for (var index = 0; index < ordered.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.ReportProgress((double)index / Math.Max(1, ordered.Count));

            if (processed >= cap)
            {
                _logger.LogInformation(
                    "Filmography: reached people cap ({Cap}); {Remaining} less-credited people not scanned this run",
                    cap,
                    ordered.Count - processed);
                break;
            }

            var person = ordered[index];
            if (!person.TryGetProviderId(MetadataProvider.Tmdb, out var idStr)
                || !int.TryParse(idStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var personTmdbId))
            {
                continue;
            }

            TmdbPerson? tmdbPerson = null;
            try
            {
                tmdbPerson = await _tmdb
                    .GetPersonAsync(personTmdbId, language, country, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Filmography: failed to fetch TMDB person {Id} ({Name})", personTmdbId, person.Name);
            }

            processed++;
            if (tmdbPerson is null)
            {
                continue;
            }

            var gaps = FilmographyGapMapper.Build(
                tmdbPerson,
                person.Id.ToString("N", CultureInfo.InvariantCulture),
                person.Name,
                context.Ownership,
                _tmdb.GetPosterUrl);

            foreach (var gap in gaps)
            {
                yield return gap;
            }
        }
    }

    // Order owned people most-credited-first. Counts how many owned movies/series list each person, then
    // sorts the person list by that count (descending), tie-breaking by name. This makes the per-run cap
    // keep the prominent creators rather than the alphabetically-first names.
    private List<BaseItem> OrderByRelevance(IReadOnlyList<BaseItem> people, CancellationToken cancellationToken)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var owned = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
            Recursive = true
        });

        foreach (var item in owned)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var credit in _libraryManager.GetPeople(item))
            {
                if (string.IsNullOrEmpty(credit.Name))
                {
                    continue;
                }

                counts.TryGetValue(credit.Name, out var current);
                counts[credit.Name] = current + 1;
            }
        }

        return people
            .OrderByDescending(p => counts.TryGetValue(p.Name, out var c) ? c : 0)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
