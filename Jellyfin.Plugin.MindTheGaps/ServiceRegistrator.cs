using Jellyfin.Data.Events.Users;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Books;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Discogs;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Imdb;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.JustWatch;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.MdbList;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Music;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Series;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Tmdb;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Todo;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Trakt;
using Jellyfin.Plugin.MindTheGaps.Services.Acquisition;
using Jellyfin.Plugin.MindTheGaps.Services.Availability;
using Jellyfin.Plugin.MindTheGaps.Services.Diagnostics;
using Jellyfin.Plugin.MindTheGaps.Services.Discogs;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using Jellyfin.Plugin.MindTheGaps.Services.Images;
using Jellyfin.Plugin.MindTheGaps.Services.Imdb;
using Jellyfin.Plugin.MindTheGaps.Services.JustWatch;
using Jellyfin.Plugin.MindTheGaps.Services.MdbList;
using Jellyfin.Plugin.MindTheGaps.Services.MusicBrainz;
using Jellyfin.Plugin.MindTheGaps.Services.OpenLibrary;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using Jellyfin.Plugin.MindTheGaps.Services.Trakt;
using Jellyfin.Plugin.MindTheGaps.Services.Tvdb;
using Jellyfin.Plugin.MindTheGaps.Services.TvMaze;
using Jellyfin.Plugin.MindTheGaps.Services.Webhook;
using Jellyfin.Plugin.MindTheGaps.VirtualItems;
using Jellyfin.Plugin.MindTheGaps.WebUi;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.MindTheGaps;

/// <summary>
/// Registers the plugin's services into the host DI container.
/// </summary>
public sealed class ServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<PluginLifetime>();
        serviceCollection.AddSingleton<GapStore>();
        serviceCollection.AddSingleton<ResolutionStore>();
        serviceCollection.AddSingleton<TodoStore>();
        serviceCollection.AddSingleton<TodoOwner>();
        serviceCollection.AddSingleton<IEventConsumer<UserDeletedEventArgs>>(sp => sp.GetRequiredService<TodoOwner>());
        serviceCollection.AddSingleton<ScanCursorStore>();
        serviceCollection.AddSingleton<ExternalLinkEnricher>();
        serviceCollection.AddSingleton<OwnershipIndexBuilder>();
        serviceCollection.AddSingleton<LibraryVerifier>();
        serviceCollection.AddSingleton<ExploreRegistry>();
        serviceCollection.AddSingleton<GapEngine>();
        serviceCollection.AddSingleton<GapScanRunner>();
        serviceCollection.AddSingleton<ExploreRunner>();
        serviceCollection.AddSingleton<RecheckRunner>();
        serviceCollection.AddSingleton<TmdbClient>();
        serviceCollection.AddSingleton<TmdbAccountClient>();
        serviceCollection.AddSingleton<WebhookNotifier>();
        serviceCollection.AddSingleton<CachedApiClient>();
        serviceCollection.AddSingleton<TraktClient>();
        serviceCollection.AddSingleton<TvMazeClient>();
        serviceCollection.AddSingleton<TvdbClient>();
        serviceCollection.AddSingleton<MusicBrainzClient>();
        serviceCollection.AddSingleton<OpenLibraryClient>();
        serviceCollection.AddSingleton<DiscogsClient>();
        serviceCollection.AddSingleton<MdbListClient>();
        serviceCollection.AddSingleton<ImdbClient>();
        serviceCollection.AddSingleton<JustWatchClient>();
        serviceCollection.AddSingleton<VirtualItemMinter>();
        serviceCollection.AddSingleton<MintRunner>();
        serviceCollection.AddSingleton<GapDiagnostics>();
        serviceCollection.AddSingleton<AcquisitionService>();
        serviceCollection.AddSingleton<PersonMissingService>();
        serviceCollection.AddSingleton<RelatedMissingService>();
        serviceCollection.AddSingleton<WorksMissingService>();
        serviceCollection.AddSingleton<HomeDiscoverService>();
        serviceCollection.AddSingleton<JustWatchLinkIndex>();
        serviceCollection.AddSingleton<WantedRowService>();
        serviceCollection.AddSingleton<WebUiAccess>();
        serviceCollection.AddSingleton<ImageCache>();
        serviceCollection.AddSingleton<IStartupFilter, WebUiScriptInjection>();

        // Availability sources + aggregator + background enrichment runner.
        serviceCollection.AddSingleton<AvailabilityService>();
        serviceCollection.AddSingleton<TmdbProviderLogos>();
        serviceCollection.AddSingleton<AvailabilityRunner>();
        serviceCollection.AddSingleton<IAvailabilitySource, TmdbAvailabilitySource>();

        // Gap sources. Add new IGapSource implementations here.
        serviceCollection.AddSingleton<IGapSource, CollectionGapSource>();
        serviceCollection.AddSingleton<IGapSource, SeriesContentGapSource>();
        serviceCollection.AddSingleton<IGapSource, PeopleGapSource>();
        serviceCollection.AddSingleton<IGapSource, TraktFilmographyGapSource>();
        serviceCollection.AddSingleton<IGapSource, RecommendationsGapSource>();
        serviceCollection.AddSingleton<IGapSource, CuratedSetGapSource>();
        serviceCollection.AddSingleton<IGapSource, MusicDiscographyGapSource>();
        serviceCollection.AddSingleton<IGapSource, MusicArtistWorksGapSource>();
        serviceCollection.AddSingleton<IGapSource, BooksBibliographyGapSource>();
        serviceCollection.AddSingleton<IGapSource, BooksSubjectGapSource>();
        serviceCollection.AddSingleton<IGapSource, DiscogsLabelGapSource>();
        serviceCollection.AddSingleton<IGapSource, DiscogsArtistGapSource>();
        serviceCollection.AddSingleton<IGapSource, MdbListGapSource>();
        serviceCollection.AddSingleton<IGapSource, TraktListGapSource>();
        serviceCollection.AddSingleton<IGapSource, ImdbListGapSource>();
        serviceCollection.AddSingleton<IGapSource, ImdbPeopleListGapSource>();
        serviceCollection.AddSingleton<IGapSource, MdbListWatchlistGapSource>();
        serviceCollection.AddSingleton<IGapSource, DiscogsWantlistGapSource>();
        serviceCollection.AddSingleton<IGapSource, OpenLibraryWantToReadGapSource>();
        serviceCollection.AddSingleton<IGapSource, TvdbFavoritesGapSource>();
        serviceCollection.AddSingleton<IGapSource, TraktWatchlistGapSource>();
        serviceCollection.AddSingleton<IGapSource, TmdbAccountListGapSource>();
        serviceCollection.AddSingleton<IGapSource, TmdbMovieDiscoverGapSource>();
        serviceCollection.AddSingleton<IGapSource, JustWatchListGapSource>();
        serviceCollection.AddSingleton<IGapSource, EveryoneWatchlistGapSource>();

        // The episode providers the series-content source merges per series (not gap sources themselves).
        serviceCollection.AddSingleton<ISeriesEpisodeProvider, TmdbEpisodeProvider>();
        serviceCollection.AddSingleton<ISeriesEpisodeProvider, TvMazeEpisodeProvider>();
        serviceCollection.AddSingleton<ISeriesEpisodeProvider, TvdbEpisodeProvider>();

        // The chip-pickable sources (CuratedSetGapSource for studios/keywords/TMDB lists, DiscogsLabelGapSource,
        // MdbListGapSource) also implement IExploreSource, declaring their explore kinds. ExploreRegistry
        // aggregates those descriptors so the engine (ad-hoc explore), the API (search/resolve and the kinds
        // dropdown), and ExploreRunner share one derived list rather than each hard-coding the kinds. The scan
        // and the explore share one source instance (and its cache); no separate concrete registration is needed.
    }
}
