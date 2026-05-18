using System.Text.Json;
using ModelMeister.Inriver.Extensions;

namespace ModelMeister.Inriver.Backup;

/// <summary>
/// JSON backup of the extension list + settings + run-state in one environment. Runtime state
/// (last events, error counts) is intentionally NOT captured — restoring those would be a lie.
/// </summary>
public sealed record ExtensionsBackup
{
    public BackupMetadata Metadata { get; init; } = new();
    public List<Entry> Extensions { get; init; } = [];

    /// <summary>Single extension row.</summary>
    public sealed record Entry(
        string Id,
        string? TypeName,
        bool IsStarted,
        Dictionary<string, string> Settings);

    /// <summary>Outcome of a single setting / state change during restore.</summary>
    public sealed record RestoreEntry(string Id, string Op, bool Ok, string? Error = null);

    /// <summary>Capture every extension's id / type / start state / settings.</summary>
    public static ExtensionsBackup Capture(ExtensionsService service, BackupMetadata metadata)
    {
        var entries = service.List()
            .Select(x => new Entry(
                x.Id,
                x.TypeName,
                x.IsStarted,
                new Dictionary<string, string>(x.Settings, StringComparer.Ordinal)))
            .ToList();
        return new ExtensionsBackup { Metadata = metadata, Extensions = entries };
    }

    /// <summary>Write the backup to disk as pretty-printed JSON.</summary>
    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(this, BackupJson.Options));
    }

    /// <summary>Read a backup file. Throws on malformed JSON.</summary>
    public static ExtensionsBackup Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ExtensionsBackup>(json, BackupJson.Options)
            ?? throw new InvalidDataException($"Extensions backup at {path} was empty or null.");
    }

    /// <summary>
    /// Restore each extension's settings via <see cref="ExtensionsService.SetSettingAsync"/>,
    /// then sync the started/stopped state. Existing extensions whose ids match are mutated;
    /// extensions in the backup that don't exist in the live env are skipped (Remoting cannot
    /// create new extensions). With <paramref name="dryRun"/> set, returns the planned changes
    /// without calling the service.
    /// </summary>
    public async Task<List<RestoreEntry>> RestoreAsync(
        ExtensionsService service,
        bool dryRun = false,
        CancellationToken ct = default)
    {
        var results = new List<RestoreEntry>();
        var liveById = service.List().ToDictionary(x => x.Id, StringComparer.Ordinal);

        foreach (var ext in Extensions)
        {
            if (ct.IsCancellationRequested) break;
            if (!liveById.TryGetValue(ext.Id, out var live))
            {
                results.Add(new RestoreEntry(ext.Id, "missing", false, "Extension not present in target env."));
                continue;
            }

            // Settings: write only changed keys; leave keys absent from the backup untouched.
            foreach (var kvp in ext.Settings)
            {
                if (ct.IsCancellationRequested) break;
                var liveVal = live.Settings.TryGetValue(kvp.Key, out var lv) ? lv : null;
                if (string.Equals(liveVal, kvp.Value, StringComparison.Ordinal)) continue;

                if (dryRun)
                {
                    results.Add(new RestoreEntry(ext.Id, $"set:{kvp.Key}", true));
                    continue;
                }
                try
                {
                    var ok = await service.SetSettingAsync(ext.Id, kvp.Key, kvp.Value, ct).ConfigureAwait(false);
                    results.Add(new RestoreEntry(ext.Id, $"set:{kvp.Key}", ok));
                }
                catch (Exception ex)
                {
                    results.Add(new RestoreEntry(ext.Id, $"set:{kvp.Key}", false, ex.Message));
                }
            }

            // Sync start/stop.
            if (ext.IsStarted != live.IsStarted)
            {
                if (dryRun)
                {
                    results.Add(new RestoreEntry(ext.Id, ext.IsStarted ? "start" : "stop", true));
                }
                else
                {
                    try
                    {
                        var ok = ext.IsStarted
                            ? await service.StartAsync(ext.Id, ct).ConfigureAwait(false)
                            : await service.StopAsync(ext.Id, ct).ConfigureAwait(false);
                        results.Add(new RestoreEntry(ext.Id, ext.IsStarted ? "start" : "stop", ok));
                    }
                    catch (Exception ex)
                    {
                        results.Add(new RestoreEntry(ext.Id, ext.IsStarted ? "start" : "stop", false, ex.Message));
                    }
                }
            }
        }
        return results;
    }
}
