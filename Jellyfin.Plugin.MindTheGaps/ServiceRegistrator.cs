using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Books;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Discogs;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Library;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Music;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Series;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Tmdb;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Trakt;
using Jellyfin.Plugin.MindTheGaps.Services.Availability;
using Jellyfin.Plugin.MindTheGaps.Services.Diagnostics;
using Jellyfin.Plugin.MindTheGaps.Services.Discogs;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using Jellyfin.Plugin.MindTheGaps.Services.MusicBrainz;
using Jellyfin.Plugin.MindTheGaps.Services.OpenLibrary;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using Jellyfin.Plugin.MindTheGaps.Services.Trakt;
using Jellyfin.Plugin.MindTheGaps.Services.Tvdb;
using Jellyfin.Plugin.MindTheGaps.Services.TvMaze;
using Jellyfin.Plugin.MindTheGaps.Services.Webhook;
using Jellyfin.Plugin.MindTheGaps.VirtualItems;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
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
        serviceCollection.AddSingleton<ScanCursorStore>();
        serviceCollection.AddSingleton<ExternalLinkEnricher>();
        serviceCollection.AddSingleton<GapEngine>();
        serviceCollection.AddSingleton<GapScanRunner>();
        serviceCollection.AddSingleton<TmdbClient>();
        serviceCollection.AddSingleton<WebhookNotifier>();
        serviceCollection.AddSingleton<CachedApiClient>();
        serviceCollection.AddSingleton<TraktClient>();
        serviceCollection.AddSingleton<TvMazeClient>();
        serviceCollection.AddSingleton<TvdbClient>();
        serviceCollection.AddSingleton<MusicBrainzClient>();
        serviceCollection.AddSingleton<OpenLibraryClient>();
        serviceCollection.AddSingleton<DiscogsClient>();
        serviceCollection.AddSingleton<VirtualMovieMinter>();
        serviceCollection.AddSingleton<MintRunner>();
        serviceCollection.AddSingleton<GapDiagnostics>();

        // Availability sources + aggregator + background enrichment runner.
        serviceCollection.AddSingleton<AvailabilityService>();
        serviceCollection.AddSingleton<AvailabilityRunner>();
        serviceCollection.AddSingleton<IAvailabilitySource, TmdbAvailabilitySource>();

        // Gap sources. Add new IGapSource implementations here.
        serviceCollection.AddSingleton<IGapSource, CollectionGapSource>();
        serviceCollection.AddSingleton<IGapSource, SeriesContentGapSource>();
        serviceCollection.AddSingleton<IGapSource, TvMazeContentGapSource>();
        serviceCollection.AddSingleton<IGapSource, TvdbContentGapSource>();
        serviceCollection.AddSingleton<IGapSource, PeopleGapSource>();
        serviceCollection.AddSingleton<IGapSource, TraktFilmographyGapSource>();
        serviceCollection.AddSingleton<IGapSource, RecommendationsGapSource>();
        serviceCollection.AddSingleton<IGapSource, CuratedSetGapSource>();
        serviceCollection.AddSingleton<IGapSource, MusicDiscographyGapSource>();
        serviceCollection.AddSingleton<IGapSource, MusicArtistWorksGapSource>();
        serviceCollection.AddSingleton<IGapSource, BooksBibliographyGapSource>();
        serviceCollection.AddSingleton<IGapSource, DiscogsLabelGapSource>();
    }
}
