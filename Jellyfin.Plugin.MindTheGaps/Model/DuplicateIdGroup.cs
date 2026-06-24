using System;
using System.Collections.Generic;
using Jellyfin.Data.Enums;

namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// A set of owned items that share one provider id, so at least one of them is misidentified.
/// </summary>
public sealed class DuplicateIdGroup
{
    /// <summary>
    /// Gets or sets the provider whose id is shared (for example "Tmdb").
    /// </summary>
    public string Provider { get; set; } = ProviderIds.Tmdb;

    /// <summary>
    /// Gets or sets the shared id value.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the kind (Movie or Series) of the items in this group.
    /// </summary>
    public BaseItemKind TargetKind { get; set; }

    /// <summary>
    /// Gets the kind name, so the dashboard can build the right provider link (the serializer writes the
    /// enum as a number, so the dashboard reads this string).
    /// </summary>
    public string TargetKindName => TargetKind.ToString();

    /// <summary>
    /// Gets or sets the owned items that share the id.
    /// </summary>
    public IReadOnlyList<DiagnosisItem> Items { get; set; } = [];
}
