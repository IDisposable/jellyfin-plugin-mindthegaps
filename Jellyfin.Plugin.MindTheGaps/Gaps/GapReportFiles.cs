using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// The on-disk shape of a <see cref="GapReport"/>: one file per domain plus a shared meta file, and the
/// legacy single-file layout older installs hold. Pure file I/O with no shared mutable state of its own
/// (every call takes the data folder and the data explicitly), split out of <see cref="GapStore"/> so the
/// locking/generation state machine there is not interleaved with how a report serializes. Every write here
/// is atomic per file (temp then replace), so a crash mid-write cannot truncate or lose a domain's gaps;
/// the caller (<see cref="GapStore"/>, always under its lock) decides when to call these, not whether the
/// result is durable.
/// </summary>
internal static class GapReportFiles
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    // The lowercase file-name segment for each domain, computed once rather than on every DomainFilePath call.
    private static readonly IReadOnlyDictionary<MediaDomain, string> DomainFileNames =
        MediaDomains.Implemented.ToDictionary(d => d, d => d.ToString().ToLowerInvariant());

    /// <summary>
    /// Loads the report from disk: the split per-domain files plus meta file when present (the file layout
    /// every save since this shipped writes), else the legacy single-file monolith older installs hold.
    /// </summary>
    /// <param name="dataFolder">The plugin data folder.</param>
    /// <returns>The report, or null when neither layout exists on disk.</returns>
    public static GapReport? Load(string dataFolder)
    {
        // Flush always writes the meta file, on every save, unconditionally - it existing at all already
        // means this install has converted, whether or not any domain currently has gaps (an empty domain's
        // file is deleted, not left behind empty), so its presence alone answers the question; checking the
        // domain files too would only repeat what it already confirms.
        if (File.Exists(MetaFilePath(dataFolder)))
        {
            return LoadSplit(dataFolder);
        }

        var legacy = LegacyFilePath(dataFolder);
        return File.Exists(legacy)
            ? JsonSerializer.Deserialize<GapReport>(File.ReadAllText(legacy), JsonOptions)
            : null;
    }

    /// <summary>
    /// Writes only the domains in <paramref name="dirtyDomains"/> (null means every implemented domain: a
    /// full scan or a multi-source pass genuinely touches all of them, so there is nothing to gain from
    /// tracking it there).
    /// </summary>
    /// <param name="dataFolder">The plugin data folder.</param>
    /// <param name="items">Every item in the report being saved (bucketed here by domain).</param>
    /// <param name="dirtyDomains">The domains to actually rewrite, or null for all of them.</param>
    public static void WriteDirtyDomains(string dataFolder, IReadOnlyList<GapItem> items, IReadOnlySet<MediaDomain>? dirtyDomains)
    {
        var byDomain = new Dictionary<MediaDomain, List<GapItem>>();
        foreach (var domain in MediaDomains.Implemented)
        {
            byDomain[domain] = new List<GapItem>();
        }

        foreach (var item in items)
        {
            if (byDomain.TryGetValue(item.Domain, out var list))
            {
                list.Add(item);
            }
        }

        foreach (var domain in MediaDomains.Implemented)
        {
            if (dirtyDomains is not null && !dirtyDomains.Contains(domain))
            {
                continue;
            }

            WriteDomainFile(dataFolder, domain, byDomain[domain]);
        }
    }

    /// <summary>
    /// Writes the shared meta file (scan timestamp/version/total and the source runs, which carry no domain
    /// of their own and so cannot be sharded like the items).
    /// </summary>
    /// <param name="dataFolder">The plugin data folder.</param>
    /// <param name="report">The report being saved.</param>
    public static void WriteMeta(string dataFolder, GapReport report)
    {
        var meta = new GapReport
        {
            GeneratedUtc = report.GeneratedUtc,
            GeneratedVersion = report.GeneratedVersion,
            TotalGaps = report.TotalGaps,
            SourceRuns = report.SourceRuns
        };
        WriteJson(MetaFilePath(dataFolder), meta);
    }

    /// <summary>
    /// Deletes the legacy single-file monolith if present. Once the split files exist, the legacy file is
    /// never read again; this keeps it from lingering as a second, increasingly stale, on-disk copy of the
    /// report.
    /// </summary>
    /// <param name="dataFolder">The plugin data folder.</param>
    public static void DeleteLegacyIfPresent(string dataFolder)
    {
        var legacy = LegacyFilePath(dataFolder);
        if (File.Exists(legacy))
        {
            File.Delete(legacy);
        }
    }

    // Reassembles one unified report from the per-domain files plus the shared meta file; the very next
    // write (from any save path) writes the split files going forward, so this only ever runs once per
    // process against a legacy-format install, or after a legacy import.
    private static GapReport LoadSplit(string dataFolder)
    {
        var meta = new GapReport();
        var metaPath = MetaFilePath(dataFolder);
        if (File.Exists(metaPath))
        {
            meta = JsonSerializer.Deserialize<GapReport>(File.ReadAllText(metaPath), JsonOptions) ?? new GapReport();
        }

        var items = new List<GapItem>();
        foreach (var domain in MediaDomains.Implemented)
        {
            var path = DomainFilePath(dataFolder, domain);
            if (!File.Exists(path))
            {
                continue;
            }

            var domainItems = JsonSerializer.Deserialize<List<GapItem>>(File.ReadAllText(path), JsonOptions);
            if (domainItems is not null)
            {
                items.AddRange(domainItems);
            }
        }

        return new GapReport
        {
            GeneratedUtc = meta.GeneratedUtc,
            GeneratedVersion = meta.GeneratedVersion,
            TotalGaps = meta.TotalGaps,
            Items = items,
            SourceRuns = meta.SourceRuns
        };
    }

    private static void WriteDomainFile(string dataFolder, MediaDomain domain, IReadOnlyList<GapItem> items)
    {
        var path = DomainFilePath(dataFolder, domain);
        if (items.Count == 0)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return;
        }

        WriteJson(path, items);
    }

    private static void WriteJson<T>(string path, T value)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(tmp, path, overwrite: true);
    }

    // The report is stored one file per domain, so a small, frequent update (a Verify, a Send) only has to
    // rewrite the one domain file it actually touched instead of the whole report. SourceRuns carries no
    // domain (a run is not scoped to one), so it lives in the shared meta file alongside the scan
    // timestamp/version rather than being shardable itself. A single gaps.json is the layout older installs
    // hold, and is imported once.
    private static string LegacyFilePath(string dataFolder) => Path.Combine(dataFolder, "gaps.json");

    private static string MetaFilePath(string dataFolder) => Path.Combine(dataFolder, "gaps-meta.json");

    private static string DomainFilePath(string dataFolder, MediaDomain domain)
        => Path.Combine(dataFolder, string.Concat("gaps-", DomainFileNames[domain], ".json"));
}
