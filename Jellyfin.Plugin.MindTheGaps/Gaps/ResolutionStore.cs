using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.MindTheGaps.Model;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Persists per-gap "resolved" notes (gaps the user marked as not really missing) to the plugin data
/// folder, keyed by <see cref="GapItem.Id"/>, so they survive rescans.
/// </summary>
public sealed class ResolutionStore
{
    // A resolution note is a short, single-line explanation; cap it so a note cannot bloat the store.
    private const int MaxNoteLength = 100;
    private const int MaxIdLength = 256;

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ILogger<ResolutionStore> _logger;
    private readonly string? _dataFolderOverride;
    private readonly object _lock = new();
    private Dictionary<string, GapResolution>? _cached;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResolutionStore"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public ResolutionStore(ILogger<ResolutionStore> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResolutionStore"/> class persisting to a specific
    /// data folder (instead of the plugin's). Used by tests.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="dataFolder">The folder to persist resolutions in.</param>
    public ResolutionStore(ILogger<ResolutionStore> logger, string dataFolder)
    {
        _logger = logger;
        _dataFolderOverride = dataFolder;
    }

    private string FilePath
    {
        get
        {
            var dataFolder = _dataFolderOverride ?? Plugin.Instance?.DataFolderPath ?? Path.GetTempPath();
            Directory.CreateDirectory(dataFolder);
            return Path.Combine(dataFolder, "resolutions.json");
        }
    }

    /// <summary>
    /// Gets a copy of every resolution, keyed by gap id.
    /// </summary>
    /// <returns>The resolutions.</returns>
    public IReadOnlyDictionary<string, GapResolution> GetAll()
    {
        lock (_lock)
        {
            return new Dictionary<string, GapResolution>(Load(), StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Marks a gap resolved with a note (or updates an existing one).
    /// </summary>
    /// <param name="id">The gap id.</param>
    /// <param name="note">The note.</param>
    public void Resolve(string id, string? note) => SetState(id, GapResolution.Resolved, note, null);

    /// <summary>
    /// Dismisses a gap with the given kind (resolved, not-interested, or snoozed), note, and optional
    /// resurface date (or updates an existing one).
    /// </summary>
    /// <param name="id">The gap id.</param>
    /// <param name="kind">The dismissal kind.</param>
    /// <param name="note">The note.</param>
    /// <param name="snoozedUntil">When a snoozed gap should resurface (null for the other kinds).</param>
    public void SetState(string id, string? kind, string? note, DateTime? snoozedUntil)
    {
        // Gap ids are short, structured keys (see ADR-0008); reject anything implausibly long so a
        // malformed request cannot bloat the store.
        if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength)
        {
            return;
        }

        // Null for the default (resolved) so it is omitted from the persisted JSON.
        var normalizedKind = kind switch
        {
            GapResolution.NotInterested => GapResolution.NotInterested,
            GapResolution.Snoozed => GapResolution.Snoozed,
            _ => null
        };

        lock (_lock)
        {
            var map = Load();
            map[id] = new GapResolution
            {
                Kind = normalizedKind,
                Note = Sanitize(note),
                ResolvedUtc = DateTime.UtcNow,
                SnoozedUntil = normalizedKind == GapResolution.Snoozed ? snoozedUntil : null
            };
            Flush(map);
        }
    }

    // Notes come from a user prompt, so drop control characters (a note is a short single-line string),
    // trim, and cap the length. The UI escapes for display; this guards the stored data.
    private static string Sanitize(string? note)
    {
        if (string.IsNullOrEmpty(note))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(note.Length);
        foreach (var ch in note)
        {
            if (!char.IsControl(ch))
            {
                sb.Append(ch);
            }
        }

        var clean = sb.ToString().Trim();
        return clean.Length > MaxNoteLength ? clean[..MaxNoteLength] : clean;
    }

    /// <summary>
    /// Clears a gap's resolution.
    /// </summary>
    /// <param name="id">The gap id.</param>
    public void Clear(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return;
        }

        lock (_lock)
        {
            var map = Load();
            if (map.Remove(id))
            {
                Flush(map);
            }
        }
    }

    // Caller holds _lock.
    private Dictionary<string, GapResolution> Load()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        try
        {
            var path = FilePath;
            if (File.Exists(path))
            {
                _cached = JsonSerializer.Deserialize<Dictionary<string, GapResolution>>(File.ReadAllText(path), _jsonOptions);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read resolutions");
        }

        return _cached ??= new Dictionary<string, GapResolution>(StringComparer.Ordinal);
    }

    // Atomic write (temp then replace); caller holds _lock.
    private void Flush(Dictionary<string, GapResolution> map)
    {
        _cached = map;
        try
        {
            var path = FilePath;
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(map, _jsonOptions));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist resolutions");
        }
    }
}
