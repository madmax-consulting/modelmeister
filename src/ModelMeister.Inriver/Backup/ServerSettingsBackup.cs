using System.Text.Json;
using ModelMeister.Inriver.ServerSettings;

namespace ModelMeister.Inriver.Backup;

/// <summary>
/// JSON backup of the flat string→string server-settings dictionary in one environment. Restore
/// pushes each key back through <see cref="ServerSettingsService.SetAsync"/>.
/// </summary>
public sealed record ServerSettingsBackup
{
    public BackupMetadata Metadata { get; init; } = new();
    public Dictionary<string, string> Settings { get; init; } = new(StringComparer.Ordinal);

    /// <summary>Outcome of a single key during restore.</summary>
    public sealed record RestoreEntry(string Key, bool Written, string? Error = null);

    /// <summary>Capture the full server-settings dictionary from the connected env.</summary>
    public static ServerSettingsBackup Capture(ServerSettingsService service, BackupMetadata metadata)
    {
        var dict = new Dictionary<string, string>(service.GetAll(), StringComparer.Ordinal);
        return new ServerSettingsBackup { Metadata = metadata, Settings = dict };
    }

    /// <summary>Write the backup to disk as pretty-printed JSON.</summary>
    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(this, BackupJson.Options));
    }

    /// <summary>Read a backup file. Throws on malformed JSON.</summary>
    public static ServerSettingsBackup Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ServerSettingsBackup>(json, BackupJson.Options)
            ?? throw new InvalidDataException($"ServerSettings backup at {path} was empty or null.");
    }

    /// <summary>
    /// Restore by writing each setting via <see cref="ServerSettingsService.SetAsync"/>. With
    /// <paramref name="dryRun"/> set, returns the projected writes without calling the service.
    /// </summary>
    public async Task<List<RestoreEntry>> RestoreAsync(
        ServerSettingsService service,
        bool dryRun = false,
        CancellationToken ct = default)
    {
        var results = new List<RestoreEntry>();
        foreach (var kvp in Settings)
        {
            if (ct.IsCancellationRequested) break;
            if (dryRun)
            {
                results.Add(new RestoreEntry(kvp.Key, Written: false));
                continue;
            }
            try
            {
                var ok = await service.SetAsync(kvp.Key, kvp.Value, ct).ConfigureAwait(false);
                results.Add(new RestoreEntry(kvp.Key, ok));
            }
            catch (Exception ex)
            {
                results.Add(new RestoreEntry(kvp.Key, false, ex.Message));
            }
        }
        return results;
    }
}
