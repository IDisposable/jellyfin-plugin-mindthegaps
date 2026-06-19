using System;
using System.Collections.Generic;
using Jellyfin.Data.Enums;

namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// The result of diagnosing why a gap is reported missing: a plain-language verdict, the gap itself, and
/// the owned items that look like they should be it (matched by title, or already carrying the gap's id).
/// The dashboard renders these as a comparison table so the provider-id mismatch is obvious and every id
/// links out for a deeper look.
/// </summary>
public sealed class GapDiagnosis
{
    /// <summary>
    /// Gets or sets the stable id of the diagnosed gap, so a report or audit can deep-link back to it.
    /// </summary>
    public string GapId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the overall plain-language verdict.
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the likely reason, so the dashboard can lead with a labeled verdict.
    /// </summary>
    public DiagnosisReason Reason { get; set; }

    /// <summary>
    /// Gets the reason name; the report serializer writes enums as numbers, so the dashboard reads this.
    /// </summary>
    public string ReasonName => Reason.ToString();

    /// <summary>
    /// Gets or sets a value indicating whether the deeper (networked) pass ran, resolving ids against
    /// TheMovieDb. False for the default library-only diagnosis.
    /// </summary>
    public bool Deepened { get; set; }

    /// <summary>
    /// Gets or sets the target kind (Movie or Series).
    /// </summary>
    public BaseItemKind TargetKind { get; set; }

    /// <summary>
    /// Gets the target kind name, so the dashboard can build the right provider-page links. The report's
    /// serializer writes enums as numbers, so the dashboard reads this string rather than TargetKind.
    /// </summary>
    public string TargetKindName => TargetKind.ToString();

    /// <summary>
    /// Gets or sets the gap itself (the title reported missing), or null when the gap kind cannot be
    /// diagnosed.
    /// </summary>
    public DiagnosisItem? Target { get; set; }

    /// <summary>
    /// Gets or sets the owned items that look like they should be the gap (the candidate "peers").
    /// </summary>
    public IReadOnlyList<DiagnosisItem> Candidates { get; set; } = Array.Empty<DiagnosisItem>();
}
