using System.Text.Json;
using ModelMeister.Inriver.WorkAreas;

namespace ModelMeister.Inriver.Backup;

/// <summary>
/// JSON backup of the shared work-area folder tree (+ saved queries) in one environment. Folders carry
/// their tree <c>Path</c> so the restore reconciles by path, independent of the per-env folder GUIDs.
/// </summary>
public sealed record WorkAreasBackup
{
    public BackupMetadata Metadata { get; init; } = new();
    public List<WorkAreaFolderDto> Folders { get; init; } = [];

    /// <summary>Outcome row for the restore-result table.</summary>
    public sealed record RestoreEntry(string Path, string Op, bool Ok, string? Error = null);

    public static WorkAreasBackup Capture(WorkAreaService service, BackupMetadata metadata) =>
        new() { Metadata = metadata, Folders = service.List().ToList() };

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(this, BackupJson.Options));
    }

    public static WorkAreasBackup Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<WorkAreasBackup>(json, BackupJson.Options)
            ?? throw new InvalidDataException($"WorkAreas backup at {path} was empty or null.");
    }

    /// <summary>Reconcile the backed-up folders back into the live env by path (create + update; never
    /// deletes). Returns one outcome row per folder.</summary>
    public async Task<List<RestoreEntry>> RestoreAsync(WorkAreaService service, CancellationToken ct = default)
    {
        var before = service.List().Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = await service.ApplyAsync(Folders, allowDeletes: false, ct).ConfigureAwait(false);
        // ApplyAsync reconciles as a batch; derive per-folder rows from the before/after path sets.
        var rows = Folders
            .OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Select(f => new RestoreEntry(f.Path, before.Contains(f.Path) ? "updated" : "created", true))
            .ToList();
        if (result.Failed > 0)
            rows.Add(new RestoreEntry("(batch)", "error", false, $"{result.Failed} folder(s) failed — see log."));
        return rows;
    }
}
