using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelMeister.Inriver.Cvl;
using ModelMeister.Inriver.Snapshot;

namespace ModelMeister.Inriver.Backup;

/// <summary>
/// JSON backup of the CVL definitions (+ their values) in one environment. Captured from a live model
/// snapshot; restored by recreating missing CVLs and upserting every value (create/update — never
/// deletes), the same primitives the CVL workbench + import use.
/// </summary>
public sealed record CvlsBackup
{
    public BackupMetadata Metadata { get; init; } = new();
    public List<LiveCvl> Cvls { get; init; } = [];

    /// <summary>Outcome row for the restore-result table.</summary>
    public sealed record RestoreEntry(string CvlId, string Op, bool Ok, string? Error = null);

    public static CvlsBackup Capture(LiveModel live, BackupMetadata metadata) =>
        new() { Metadata = metadata, Cvls = live.Cvls.ToList() };

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(this, BackupJson.Options));
    }

    public static CvlsBackup Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<CvlsBackup>(json, BackupJson.Options)
            ?? throw new InvalidDataException($"CVLs backup at {path} was empty or null.");
    }

    /// <summary>Reconcile the backed-up CVLs back into the live env: create missing definitions, upsert
    /// every value. Never deletes. Returns one outcome row per CVL.</summary>
    public async Task<List<RestoreEntry>> RestoreAsync(CvlAdmin admin, IReadOnlyList<LiveCvl> liveNow, CancellationToken ct = default)
    {
        var liveIds = liveNow.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = new List<RestoreEntry>();
        foreach (var c in Cvls.OrderBy(c => c.Id, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var exists = liveIds.Contains(c.Id);
                if (!exists) await admin.AddCvlAsync(c.Id, c.DataType, c.ParentId, c.CustomValueList, ct).ConfigureAwait(false);
                foreach (var v in c.Values)
                    await admin.UpsertValueAsync(c.Id, v, ct).ConfigureAwait(false);
                rows.Add(new RestoreEntry(c.Id, exists ? "updated" : "created", true));
            }
            catch (Exception ex)
            {
                rows.Add(new RestoreEntry(c.Id, "error", false, ex.Message));
            }
        }
        return rows;
    }
}
