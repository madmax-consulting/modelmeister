using System.Text.Json;
using ModelMeister.Inriver.Users;

namespace ModelMeister.Inriver.Backup;

/// <summary>
/// JSON backup of the restricted-field permissions in a single environment. Rows are keyed by role
/// name + scope ids so the file is portable across environments.
/// </summary>
public sealed record RestrictedFieldsBackup
{
    public BackupMetadata Metadata { get; init; } = new();
    public List<Entry> RestrictedFields { get; init; } = [];

    /// <summary>Single restricted-field permission row.</summary>
    public sealed record Entry(
        string RoleName,
        string RestrictionType,
        string? EntityTypeId,
        string? FieldTypeId,
        string? CategoryId);

    /// <summary>Capture the restricted-field permissions in the connected env via <see cref="RestrictedFieldProvisioning"/>.</summary>
    public static RestrictedFieldsBackup Capture(RestrictedFieldProvisioning provisioning, BackupMetadata metadata)
    {
        var entries = provisioning.ListRestrictedFields()
            .Select(r => new Entry(r.RoleName, r.RestrictionType, r.EntityTypeId, r.FieldTypeId, r.CategoryId))
            .ToList();
        return new RestrictedFieldsBackup { Metadata = metadata, RestrictedFields = entries };
    }

    /// <summary>Write the backup to disk as pretty-printed JSON.</summary>
    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(this, BackupJson.Options));
    }

    /// <summary>Read a backup file. Throws on malformed JSON.</summary>
    public static RestrictedFieldsBackup Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<RestrictedFieldsBackup>(json, BackupJson.Options)
            ?? throw new InvalidDataException($"Restricted-fields backup at {path} was empty or null.");
    }

    /// <summary>
    /// Restore by adding each row that isn't already present (matched by natural key) — restricted-field
    /// permissions have no update, so existing rows are left untouched.
    /// </summary>
    public async Task<List<RestrictedFieldProvisioning.ProvisionResult>> RestoreAsync(
        RestrictedFieldProvisioning provisioning,
        CancellationToken ct = default)
    {
        var existing = provisioning.ListRestrictedFields()
            .Select(r => r.NaturalKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var results = new List<RestrictedFieldProvisioning.ProvisionResult>();
        foreach (var r in RestrictedFields)
        {
            if (ct.IsCancellationRequested) break;
            var key = RestrictedFieldProvisioning.NaturalKey(r.RoleName, r.RestrictionType, r.EntityTypeId, r.FieldTypeId, r.CategoryId);
            if (existing.Contains(key))
            {
                results.Add(new RestrictedFieldProvisioning.ProvisionResult(key, false, false, ["already exists — skipped"]));
                continue;
            }
            var spec = new RestrictedFieldProvisioning.RestrictedFieldSpec(
                r.RoleName, r.RestrictionType, r.EntityTypeId, r.FieldTypeId, r.CategoryId);
            results.Add(await provisioning.AddAsync(spec, ct).ConfigureAwait(false));
        }
        return results;
    }
}
