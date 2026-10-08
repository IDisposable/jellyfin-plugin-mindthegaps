using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.MindTheGaps.Services.Tmdb;

/// <summary>
/// A title's certification, picked out of TMDB's per-country list and spelled the way Jellyfin's own TMDB
/// provider writes a library item's official rating, so core's rating table reads it exactly as it reads a
/// title the library holds: the metadata country first, then the US, then whichever country TMDB lists first;
/// a US rating plain, any other prefixed with its country, Germany's as <c>FSK-</c>. The country it came from
/// travels with it, since that is the rating table it belongs to.
/// </summary>
/// <param name="Country">The country the rating is from, as an ISO 3166-1 code in upper case.</param>
/// <param name="Rating">The rating, as Jellyfin spells an official rating.</param>
internal readonly record struct TmdbCertification(string Country, string Rating)
{
    /// <summary>
    /// Picks the certification.
    /// </summary>
    /// <param name="ratings">Each country TMDB lists and its certifications, in TMDB's order (a movie has one per
    /// release, a series one per country).</param>
    /// <param name="country">The metadata country code, or null for the US.</param>
    /// <returns>The certification, or <see langword="null"/> when TMDB has none for any country.</returns>
    public static TmdbCertification? Pick(IEnumerable<(string? Country, IEnumerable<string?> Certifications)> ratings, string? country)
    {
        ArgumentNullException.ThrowIfNull(ratings);

        var rated = ratings
            .Where(r => !string.IsNullOrWhiteSpace(r.Country))
            .Select(r => (Country: r.Country!.Trim().ToUpperInvariant(), Certification: r.Certifications.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim()))
            .Where(r => r.Certification is not null)
            .ToList();

        var preferred = string.IsNullOrWhiteSpace(country) ? "US" : country.Trim().ToUpperInvariant();
        var pick = rated.FirstOrDefault(r => r.Country == preferred);
        if (pick.Certification is null)
        {
            pick = rated.FirstOrDefault(r => r.Country == "US");
        }

        if (pick.Certification is null)
        {
            pick = rated.FirstOrDefault();
        }

        return pick.Certification is null ? null : new TmdbCertification(pick.Country, Spell(pick.Country, pick.Certification));
    }

    private static string Spell(string country, string certification)
        => country switch
        {
            "US" => certification,
            "DE" => "FSK-" + certification,
            _ => country + "-" + certification,
        };
}
