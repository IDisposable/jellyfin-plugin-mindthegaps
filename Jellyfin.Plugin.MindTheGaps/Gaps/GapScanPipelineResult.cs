using System.Collections.Generic;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// The merged, de-duplicated result of one <see cref="GapScanPipeline.RunAsync"/> call.
/// </summary>
/// <param name="Gaps">The merged gaps, in first-seen order.</param>
/// <param name="Runs">Each discovery kind's <see cref="SourceRun"/> for this scan.</param>
public sealed record GapScanPipelineResult(IReadOnlyList<GapItem> Gaps, IReadOnlyList<SourceRun> Runs);
