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
}
