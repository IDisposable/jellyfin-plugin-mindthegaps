namespace Jellyfin.Plugin.MindTheGaps.Services.Tmdb;

internal interface ITmdbAccountApiFactory
{
    ITmdbAccountApi Create(string apiKey);
}
