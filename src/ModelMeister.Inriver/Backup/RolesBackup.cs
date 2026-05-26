using System.Text.Json;
using ModelMeister.Inriver.Users;

namespace ModelMeister.Inriver.Backup;

/// <summary>
/// JSON backup of the roles + permission bindings in a single environment. Flat schema so the file
/// can be hand-edited or diffed.
/// </summary>
public sealed record RolesBackup
{
    public BackupMetadata Metadata { get; init; } = new();
    public List<Entry> Roles { get; init; } = [];

    /// <summary>Single role row.</summary>
    public sealed record Entry(string Name, string? Description, IReadOnlyList<string> Permissions);

    /// <summary>Capture the roles + permission bindings in the connected env via <see cref="RoleProvisioning"/>.</summary>
    public static RolesBackup Capture(RoleProvisioning provisioning, BackupMetadata metadata)
    {
        var entries = provisioning.ListRoles()
            .Select(r => new Entry(r.Name, r.Description, r.Permissions ?? []))
            .ToList();
        return new RolesBackup { Metadata = metadata, Roles = entries };
    }

    /// <summary>Write the backup to disk as pretty-printed JSON.</summary>
    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(this, BackupJson.Options));
    }

    /// <summary>Read a backup file. Throws on malformed JSON.</summary>
    public static RolesBackup Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<RolesBackup>(json, BackupJson.Options)
            ?? throw new InvalidDataException($"Roles backup at {path} was empty or null.");
    }

    /// <summary>
    /// Restore by upserting each role via <see cref="RoleProvisioning.ProvisionAsync"/>. Every call is
    /// a write (no dry-run). Returns one outcome per role.
    /// </summary>
    public async Task<List<RoleProvisioning.ProvisionResult>> RestoreAsync(
        RoleProvisioning provisioning,
        CancellationToken ct = default)
    {
        var results = new List<RoleProvisioning.ProvisionResult>();
        foreach (var r in Roles)
        {
            if (ct.IsCancellationRequested) break;
            var spec = new RoleProvisioning.RoleSpec(r.Name, r.Description, r.Permissions ?? []);
            results.Add(await provisioning.ProvisionAsync(spec, ct).ConfigureAwait(false));
        }
        return results;
    }
}
