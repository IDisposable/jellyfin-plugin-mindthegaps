using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// The titles still on a user's want-to-watch list, as the home screen's row shows them.
/// </summary>
public sealed class WantedRowResult
{
    /// <summary>
    /// Gets or sets the titles the library does not hold yet, the ones added last first.
    /// </summary>
    public IReadOnlyList<MissingTitle> Titles { get; set; } = Array.Empty<MissingTitle>();

    /// <summary>
    /// Gets or sets where the row goes on the home screen: empty for the bottom, <c>top</c>, or the home
    /// section type it follows.
    /// </summary>
    public string Placement { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the caller's home slots in order, one section type each, when <see cref="Placement"/>
    /// follows a section type; null otherwise.
    /// </summary>
    public IReadOnlyList<string>? HomeSections { get; set; }
}
