using System.Text.Json;
using ModelMeister.Inriver.Users;

namespace ModelMeister.Inriver.Backup;

/// <summary>
/// JSON backup of the users + role memberships in a single environment. Schema is intentionally
/// flat so the file can be hand-edited or diffed.
/// </summary>
public sealed record UsersBackup
{
    public BackupMetadata Metadata { get; init; } = new();
    public List<UsersBackup.Entry> Users { get; init; } = [];

    /// <summary>Single user row.</summary>
    public sealed record Entry(
        string Username,
        string? Email,
        string? FirstName,
        string? LastName,
        bool Active,
        IReadOnlyList<string> Roles);

    /// <summary>Capture the users + roles in the connected env via <see cref="UserProvisioning"/>.</summary>
    public static UsersBackup Capture(UserProvisioning provisioning, BackupMetadata metadata)
    {
        var entries = provisioning.ListUsers()
            .Select(u => new Entry(
                u.Username ?? "",
                u.Email,
                u.FirstName,
                u.LastName,
                u.Active,
                u.Roles ?? []))
            .ToList();
        return new UsersBackup { Metadata = metadata, Users = entries };
    }

    /// <summary>Write the backup to disk as pretty-printed JSON.</summary>
    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(this, BackupJson.Options));
    }

    /// <summary>Read a backup file. Throws on malformed JSON.</summary>
    public static UsersBackup Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<UsersBackup>(json, BackupJson.Options)
            ?? throw new InvalidDataException($"Users backup at {path} was empty or null.");
    }

    /// <summary>
    /// Restore by calling <see cref="UserProvisioning.ProvisionAsync"/> per entry. Note that the
    /// underlying provisioning surface has no dry-run mode — every call to this method is a write.
    /// Returns one outcome per user.
    /// </summary>
    public async Task<List<UserProvisioning.ProvisionResult>> RestoreAsync(
        UserProvisioning provisioning,
        CancellationToken ct = default)
    {
        var results = new List<UserProvisioning.ProvisionResult>();
        foreach (var u in Users)
        {
            if (ct.IsCancellationRequested) break;
            var spec = new UserProvisioning.UserSpec(
                u.Username,
                u.Email,
                u.FirstName,
                u.LastName,
                Company: null,
                Roles: u.Roles ?? [],
                Language: "en",
                GenerateApiKey: false);
            var result = await provisioning.ProvisionAsync(spec, ct).ConfigureAwait(false);
            results.Add(result);
        }
        return results;
    }
}
