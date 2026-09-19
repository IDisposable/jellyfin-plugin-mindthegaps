using System;
using System.Collections.Generic;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps.Services.Availability;

/// <summary>
/// Gives an offer its provider's logo when it was stored without one. A scan carries a gap's offers forward and
/// the availability pass skips a gap it has already checked, so an offer stored before logos existed is never
/// looked up again; this fills the logo in from the provider catalog instead.
/// </summary>
internal static class AvailabilityLogos
{
    /// <summary>
    /// Sets <see cref="AvailabilityOffer.LogoUrl"/> on every offer that has none and whose provider has a logo.
    /// </summary>
    /// <param name="items">The gaps.</param>
    /// <param name="logos">Logo URL by provider name, ignoring case.</param>
    /// <returns>How many offers were given a logo.</returns>
    public static int Fill(IEnumerable<GapItem> items, IReadOnlyDictionary<string, string> logos)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(logos);

        if (logos.Count == 0)
        {
            return 0;
        }

        var filled = 0;
        foreach (var item in items)
        {
            foreach (var offer in item.Availability)
            {
                if (string.IsNullOrEmpty(offer.LogoUrl) && logos.TryGetValue(offer.Provider, out var url))
                {
                    offer.LogoUrl = url;
                    filled++;
                }
            }
        }

        return filled;
    }
}
