using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// A library-wide identification audit: the gaps that look like a metadata mismatch (you own them under a
/// different id) and the owned items that share a provider id (so one is misidentified). The dashboard
/// formats this as a downloadable Markdown report.
/// </summary>
public sealed class IdentificationAudit
{
    /// <summary>
    /// Gets or sets the scan time the audit is based on (the report's <c>GeneratedUtc</c>). The audit does
    /// no fresh discovery, so it is dated by the scan whose gaps it analyses, not by when it was run.
    /// </summary>
    public DateTime GeneratedUtc { get; set; }

    /// <summary>
    /// Gets or sets how many owned movies were scanned.
    /// </summary>
    public int OwnedMovies { get; set; }

    /// <summary>
    /// Gets or sets how many owned shows were scanned.
    /// </summary>
    public int OwnedShows { get; set; }

    /// <summary>
    /// Gets or sets how many gaps were checked.
    /// </summary>
    public int GapsChecked { get; set; }

    /// <summary>
    /// Gets or sets the gaps that look like a metadata mismatch (each is a per-gap diagnosis with the owned
    /// candidate that should probably be it).
    /// </summary>
    public IReadOnlyList<GapDiagnosis> Mismatches { get; set; } = Array.Empty<GapDiagnosis>();

    /// <summary>
    /// Gets or sets the groups of owned items that share a provider id (one of each group is misidentified).
    /// </summary>
    public IReadOnlyList<DuplicateIdGroup> Duplicates { get; set; } = Array.Empty<DuplicateIdGroup>();
}
