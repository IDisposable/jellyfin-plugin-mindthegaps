using System;
using System.Collections.Generic;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps.Services.Availability;

/// <summary>
/// Maps a TMDB watch/providers response to availability offers for one country.
/// </summary>
public static class TmdbWatchMapper
{
    /// <summary>
    /// Flattens the offers for a single country into <see cref="AvailabilityOffer"/>s.
    /// </summary>
    /// <param name="response">The TMDB watch/providers response.</param>
    /// <param name="country">The ISO 3166-1 alpha-2 country code.</param>
    /// <returns>The offers (empty if the country has none).</returns>
    public static IReadOnlyList<AvailabilityOffer> Map(TmdbWatchResponse? response, string country)
    {
        if (response?.Results is null || !response.Results.TryGetValue(country, out var offers) || offers is null)
        {
            return Array.Empty<AvailabilityOffer>();
        }

        var result = new List<AvailabilityOffer>();
        Add(result, offers.Flatrate, "flatrate", offers.Link);
        Add(result, offers.Free, "free", offers.Link);
        Add(result, offers.Ads, "ads", offers.Link);
        Add(result, offers.Rent, "rent", offers.Link);
        Add(result, offers.Buy, "buy", offers.Link);
        return result;
    }

    private static void Add(List<AvailabilityOffer> offers, IReadOnlyList<TmdbWatchProvider>? providers, string monetization, string? link)
    {
        if (providers is null)
        {
            return;
        }

        foreach (var provider in providers)
        {
            if (!string.IsNullOrEmpty(provider.ProviderName))
            {
                offers.Add(new AvailabilityOffer
                {
                    Provider = provider.ProviderName,
                    MonetizationType = monetization,
                    Url = link
                });
            }
        }
    }
}
