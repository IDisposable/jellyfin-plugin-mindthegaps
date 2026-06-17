using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Library;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Series;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Tmdb;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Trakt;
using Jellyfin.Plugin.MindTheGaps.Services.Availability;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using Jellyfin.Plugin.MindTheGaps.Services.Trakt;
using Jellyfin.Plugin.MindTheGaps.Services.Tvdb;
using Jellyfin.Plugin.MindTheGaps.Services.TvMaze;
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
        serviceCollection.AddSingleton<GapStore>();
        serviceCollection.AddSingleton<ExternalLinkEnricher>();
        serviceCollection.AddSingleton<GapEngine>();
        serviceCollection.AddSingleton<GapScanRunner>();
        serviceCollection.AddSingleton<TmdbClient>();
        serviceCollection.AddSingleton<TraktClient>();
        serviceCollection.AddSingleton<TvMazeClient>();
        serviceCollection.AddSingleton<TvdbClient>();
        serviceCollection.AddSingleton<VirtualMovieMinter>();
        serviceCollection.AddSingleton<MintRunner>();

        // Availability sources + aggregator.
        serviceCollection.AddSingleton<AvailabilityService>();
        serviceCollection.AddSingleton<IAvailabilitySource, TmdbAvailabilitySource>();

        // Gap sources. Add new IGapSource implementations here.
        serviceCollection.AddSingleton<IGapSource, CollectionGapSource>();
        serviceCollection.AddSingleton<IGapSource, SeriesContentGapSource>();
        serviceCollection.AddSingleton<IGapSource, TvMazeContentGapSource>();
        serviceCollection.AddSingleton<IGapSource, TvdbContentGapSource>();
        serviceCollection.AddSingleton<IGapSource, PeopleGapSource>();
        serviceCollection.AddSingleton<IGapSource, TraktFilmographyGapSource>();
        serviceCollection.AddSingleton<IGapSource, RecommendationsGapSource>();
    }
}
