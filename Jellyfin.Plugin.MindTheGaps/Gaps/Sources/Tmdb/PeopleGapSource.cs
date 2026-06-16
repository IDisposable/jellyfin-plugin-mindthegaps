using System;
using System.Collections.Generic;
using System.Globalization;
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
    // TMDB person fetch is a single cached call per person, so a generous cap is fine.
    private const int MaxPeople = 500;

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

        var people = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Person },
            Recursive = true
        });

        var processed = 0;
        var index = 0;
        foreach (var person in people)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.ReportProgress((double)index++ / Math.Max(1, people.Count));

            if (processed >= MaxPeople)
            {
                _logger.LogInformation(
                    "Filmography: reached people cap ({Cap}); {Remaining} people not scanned this run",
                    MaxPeople,
                    people.Count - processed);
                break;
            }

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
}
