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
    /// deletes). Drives the reconcile session one action at a time so each folder gets an accurate
    /// created/updated/error outcome (rather than a single batch tally).</summary>
    public async Task<List<RestoreEntry>> RestoreAsync(WorkAreaService service, CancellationToken ct = default)
    {
        var before = service.List().Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var session = service.Plan(Folders, allowDeletes: false);
        var rows = new List<RestoreEntry>();
        foreach (var action in session.Actions)
        {
            ct.ThrowIfCancellationRequested();
            var op = before.Contains(action.Path) ? "updated" : "created";
            try
            {
                await session.ExecuteAsync(action, ct).ConfigureAwait(false);
                rows.Add(new RestoreEntry(action.Path, op, true));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                rows.Add(new RestoreEntry(action.Path, "error", false, ex.Message));
            }
        }
        return rows.OrderBy(r => r.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
