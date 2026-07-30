using System;
using System.Globalization;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// One source's naming, declared once: the prefix its <see cref="Model.GapItem.Id"/>s carry and, when the
/// thing that surfaced the gap is not a library item, the prefix of the synthetic
/// <see cref="Model.GapItem.SourceItemId"/> that stands in for it. Both are derived from a single stem, so a
/// source cannot end up calling itself two different things.
/// </summary>
/// <remarks>
/// Both values persist in a saved report, so they are a contract (ADR-0008). Three sources shipped with a
/// stem their owner id does not match, and those are declared with <see cref="LegacyOwner"/> rather than
/// quietly hand-written, so the exceptions are the visible ones and everything else is mechanical.
/// </remarks>
internal sealed class GapSourceKey
{
    private GapSourceKey(string? stem, string? ownerStem)
    {
        GapPrefix = stem is null ? string.Empty : string.Concat(stem, ":");
        OwnerStem = ownerStem;
    }

    /// <summary>
    /// Gets the prefix every gap id from this source starts with, colon included. Empty for a source whose
    /// gaps are keyed under another source's prefix (a curated TMDB list, under "curated:").
    /// </summary>
    public string GapPrefix { get; }

    /// <summary>
    /// Gets the synthetic owner id's stem, or null when this source's gaps are owned by a real library item
    /// and carry its guid instead.
    /// </summary>
    public string? OwnerStem { get; }

    /// <summary>
    /// Declares a source whose gap prefix and owner prefix share one stem. The normal case.
    /// </summary>
    /// <param name="stem">The stem, for example "imdblist".</param>
    /// <returns>The key.</returns>
    public static GapSourceKey For(string stem) => new(stem, stem);

    /// <summary>
    /// Declares a source whose gaps are owned by a library item, so it needs no synthetic owner id.
    /// </summary>
    /// <param name="stem">The stem.</param>
    /// <returns>The key.</returns>
    public static GapSourceKey GapOnly(string stem) => new(stem, null);

    /// <summary>
    /// Declares a source that only mints an owner id, its gaps being keyed under another source's prefix.
    /// </summary>
    /// <param name="ownerStem">The owner stem.</param>
    /// <returns>The key.</returns>
    public static GapSourceKey OwnerOnly(string ownerStem) => new(null, ownerStem);

    /// <summary>
    /// Declares a source whose stored owner id does not match its gap prefix. Only for spellings already in
    /// saved reports; a new source uses <see cref="For"/>.
    /// </summary>
    /// <param name="stem">The gap-id stem.</param>
    /// <param name="ownerStem">The owner id's differing stem, as already persisted.</param>
    /// <returns>The key.</returns>
    public static GapSourceKey LegacyOwner(string stem, string ownerStem) => new(stem, ownerStem);

    /// <summary>
    /// Builds the owner id for a source that has exactly one (an account's single watchlist, say).
    /// </summary>
    /// <returns>The owner id.</returns>
    public string Owner() => OwnerStem ?? throw new InvalidOperationException("This source's gaps are owned by a library item.");

    /// <summary>
    /// Builds the owner id for one of many owners, suffixed with what identifies it.
    /// </summary>
    /// <param name="suffix">The list id, username, or other discriminator.</param>
    /// <returns>The owner id.</returns>
    public string Owner(string suffix)
        => string.Create(CultureInfo.InvariantCulture, $"{Owner()}-{suffix}");

    /// <summary>
    /// Builds the owner id for one of many owners identified by a number.
    /// </summary>
    /// <param name="suffix">The numeric id.</param>
    /// <returns>The owner id.</returns>
    public string Owner(long suffix)
        => string.Create(CultureInfo.InvariantCulture, $"{Owner()}-{suffix}");

    /// <summary>
    /// Builds a gap id from this source's prefix and the key that identifies the missing thing.
    /// </summary>
    /// <param name="key">The rest of the id.</param>
    /// <returns>The gap id.</returns>
    public string Gap(string key) => string.Concat(GapPrefix, key);
}
