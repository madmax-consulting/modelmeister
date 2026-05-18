using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelMeister.Ui.Models;

namespace ModelMeister.Ui.Services;

/// <summary>
/// Persists and retrieves environment definitions plus their associated secrets. Implementations
/// are responsible for encryption-at-rest (Windows builds use DPAPI; see <see cref="DpapiEnvironmentVault"/>).
/// </summary>
public interface IEnvironmentVault
{
    /// <summary>All known environments, in their on-disk order.</summary>
    IReadOnlyList<EnvironmentEntry> List();

    /// <summary>Look up a single environment by id.</summary>
    EnvironmentEntry? Get(Guid id);

    /// <summary>Returns the stored secret (or <c>null</c> when there is no secret on file).</summary>
    EnvironmentSecret? GetSecret(Guid id);

    /// <summary>True when the secret file is unreadable or the entry's secret was missing on load.</summary>
    bool SecretMissing(Guid id);

    /// <summary>Insert or replace an environment together with its secret.</summary>
    void Upsert(EnvironmentEntry entry, EnvironmentSecret secret);

    /// <summary>Remove an environment and its secret.</summary>
    void Delete(Guid id);

    /// <summary>Update <see cref="EnvironmentEntry.LastUsedUtc"/> to "now" and persist.</summary>
    void Touch(Guid id);

    /// <summary>Raised after the vault contents change (Upsert / Delete / Touch).</summary>
    event Action? Changed;
}

/// <summary>
/// Windows-only <see cref="IEnvironmentVault"/> that encrypts both the environment list and the
/// secret map using DPAPI scoped to the current user.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiEnvironmentVault : IEnvironmentVault
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly List<EnvironmentEntry> _entries;
    private readonly Dictionary<Guid, EnvironmentSecret?> _secrets;
    private readonly HashSet<Guid> _secretLoadFailures = new();

    /// <inheritdoc/>
    public event Action? Changed;

    /// <summary>Reads existing vault files (if any) — corrupt files are quarantined and a fresh vault starts.</summary>
    public DpapiEnvironmentVault()
    {
        Paths.EnsureAppDataDir();
        _entries = LoadEntries();
        _secrets = LoadSecrets(_entries, _secretLoadFailures);
    }

    /// <inheritdoc/>
    public IReadOnlyList<EnvironmentEntry> List() => _entries;

    /// <inheritdoc/>
    public EnvironmentEntry? Get(Guid id) => _entries.FirstOrDefault(e => e.Id == id);

    /// <inheritdoc/>
    public EnvironmentSecret? GetSecret(Guid id) => _secrets.TryGetValue(id, out var s) ? s : null;

    /// <inheritdoc/>
    public bool SecretMissing(Guid id) => _secretLoadFailures.Contains(id);

    /// <inheritdoc/>
    public void Upsert(EnvironmentEntry entry, EnvironmentSecret secret)
    {
        var existingIndex = _entries.FindIndex(e => e.Id == entry.Id);
        if (existingIndex >= 0) _entries[existingIndex] = entry;
        else _entries.Add(entry);

        _secrets[entry.Id] = secret;
        _secretLoadFailures.Remove(entry.Id);
        SaveAll();
        Changed?.Invoke();
    }

    /// <inheritdoc/>
    public void Delete(Guid id)
    {
        _entries.RemoveAll(e => e.Id == id);
        _secrets.Remove(id);
        _secretLoadFailures.Remove(id);
        SaveAll();
        Changed?.Invoke();
    }

    /// <inheritdoc/>
    public void Touch(Guid id)
    {
        var entry = _entries.FirstOrDefault(x => x.Id == id);
        if (entry is null) return;
        entry.LastUsedUtc = DateTime.UtcNow;
        SaveAll();
        Changed?.Invoke();
    }

    private static List<EnvironmentEntry> LoadEntries()
    {
        if (!File.Exists(Paths.EnvironmentsFile)) return [];
        try
        {
            var json = DecryptToString(Paths.EnvironmentsFile);
            return JsonSerializer.Deserialize<List<EnvironmentEntry>>(json, Json) ?? [];
        }
        catch
        {
            QuarantineBroken(Paths.EnvironmentsFile);
            return [];
        }
    }

    private static Dictionary<Guid, EnvironmentSecret?> LoadSecrets(List<EnvironmentEntry> entries, HashSet<Guid> failures)
    {
        var result = entries.ToDictionary(e => e.Id, _ => (EnvironmentSecret?)null);
        if (!File.Exists(Paths.SecretsFile)) return result;

        try
        {
            var json = DecryptToString(Paths.SecretsFile);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, EnvironmentSecret>>(json, Json);
            if (loaded is null) return result;

            foreach (var entry in entries)
            {
                if (loaded.TryGetValue(entry.Id.ToString(), out var s)) result[entry.Id] = s;
                else failures.Add(entry.Id);
            }
        }
        catch
        {
            QuarantineBroken(Paths.SecretsFile);
            foreach (var entry in entries) failures.Add(entry.Id);
        }
        return result;
    }

    private void SaveAll()
    {
        Paths.EnsureAppDataDir();
        WriteProtected(Paths.EnvironmentsFile, JsonSerializer.SerializeToUtf8Bytes(_entries, Json));

        var dict = _secrets
            .Where(kv => kv.Value is not null)
            .ToDictionary(kv => kv.Key.ToString(), kv => kv.Value!);
        WriteProtected(Paths.SecretsFile, JsonSerializer.SerializeToUtf8Bytes(dict, Json));
    }

    private static string DecryptToString(string path)
    {
        var protectedBytes = File.ReadAllBytes(path);
        var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }

    private static void WriteProtected(string path, byte[] bytes)
    {
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, encrypted);
        if (File.Exists(path)) File.Replace(tmp, path, null);
        else File.Move(tmp, path);
    }

    private static void QuarantineBroken(string path)
    {
        try
        {
            var dest = $"{path}.broken-{DateTime.UtcNow:yyyyMMddHHmmss}";
            File.Move(path, dest);
        }
        catch
        {
            // Best-effort: if we can't even rename the broken file, fall through silently.
        }
    }
}
