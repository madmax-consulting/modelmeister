using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ModelMeister.Inriver.Backup;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Ui.Models;

namespace ModelMeister.Ui.Services;

/// <summary>
/// One row in the backup library shown by the Backup hub. Owns the file or folder path on disk.
/// </summary>
public sealed record BackupFileInfo(
    BackupKind Kind,
    string Path,
    string EnvName,
    string Scope,
    DateTime CapturedAtUtc,
    long SizeBytes,
    string? Label);

/// <summary>Whether the backup is a single JSON file (scoped) or a Full-snapshot folder.</summary>
public enum BackupKind
{
    File,
    Folder,
}

/// <summary>
/// Reads + writes scoped and full backups. Files live under
/// <c>%AppData%/ModelMeister/backups/&lt;envName&gt;/&lt;scope&gt;/&lt;timestamp&gt;.json</c>;
/// Full-snapshot folders sit at <c>backups/&lt;envName&gt;/Full__&lt;timestamp&gt;/</c>.
/// </summary>
public sealed class BackupService
{
    private readonly Shell _shell;
    private readonly IConnectionLifecycle _connection;
    private readonly IEnvironmentVault _vault;

    public BackupService(Shell shell, IConnectionLifecycle connection, IEnvironmentVault vault)
    {
        _shell = shell;
        _connection = connection;
        _vault = vault;
    }

    /// <summary>
    /// Fires after the on-disk backup library has changed (capture or external delete). Subscribers
    /// (e.g. <see cref="SnapshotsViewModel"/>, <see cref="DashboardViewModel"/>) reload their lists.
    /// May fire on a thread-pool thread — UI subscribers must marshal to the UI thread themselves.
    /// </summary>
    public event Action? Changed;

    /// <summary>Notify subscribers that a backup was added or removed. Public so the rare external
    /// mutator (e.g. the Snapshots page deleting a file in-process) can keep listeners in sync.</summary>
    public void RaiseChanged() => Changed?.Invoke();

    /// <summary>Root folder for all backups produced by the app.</summary>
    public string BackupsRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ModelMeister", "backups");

    /// <summary>Per-env folder, e.g., <c>backups/Prod</c>.</summary>
    public string EnvFolder(string envName) => Path.Combine(BackupsRoot, Sanitize(envName));

    /// <summary>Per-env per-scope folder, e.g., <c>backups/Prod/Users</c>.</summary>
    public string ScopeFolder(string envName, string scope) => Path.Combine(EnvFolder(envName), Sanitize(scope));

    /// <summary>Build a metadata record describing the current capture context.</summary>
    public BackupMetadata BuildMetadata(string? label = null)
    {
        var env = _connection.Connected;
        return new BackupMetadata
        {
            EnvName = env?.Name ?? "(none)",
            EnvUrl = env?.Url,
            Stage = env?.Stage.ToString(),
            CapturedAtUtc = DateTime.UtcNow,
            Label = label,
            Tool = "ModelMeister",
        };
    }

    /// <summary>Capture a Users backup and write it to disk. Returns the saved path.</summary>
    public async Task<string> CaptureUsersAsync(string? label = null, System.Threading.CancellationToken ct = default)
    {
        var env = _connection.Connected ?? throw new InvalidOperationException("Not connected.");
        var meta = BuildMetadata(label);
        var backup = await _shell.CaptureUsersBackupAsync(meta, ct).ConfigureAwait(false);
        var path = NewFilePath(env.Name, "Users");
        backup.Save(path);
        RaiseChanged();
        return path;
    }

    /// <summary>Capture a ServerSettings backup. Returns the saved path.</summary>
    public async Task<string> CaptureServerSettingsAsync(string? label = null, System.Threading.CancellationToken ct = default)
    {
        var env = _connection.Connected ?? throw new InvalidOperationException("Not connected.");
        var meta = BuildMetadata(label);
        var backup = await _shell.CaptureServerSettingsBackupAsync(meta, ct).ConfigureAwait(false);
        var path = NewFilePath(env.Name, "ServerSettings");
        backup.Save(path);
        RaiseChanged();
        return path;
    }

    /// <summary>Capture an Extensions backup. Returns the saved path.</summary>
    public async Task<string> CaptureExtensionsAsync(string? label = null, System.Threading.CancellationToken ct = default)
    {
        var env = _connection.Connected ?? throw new InvalidOperationException("Not connected.");
        var secret = _vault.GetSecret(env.Id);
        var meta = BuildMetadata(label);
        var backup = await _shell.CaptureExtensionsBackupAsync(env, secret, meta, ct).ConfigureAwait(false);
        var path = NewFilePath(env.Name, "Extensions");
        backup.Save(path);
        RaiseChanged();
        return path;
    }

    /// <summary>
    /// Capture a Full snapshot — model + users + server-settings + extensions — into a folder
    /// under the env's backup directory. Slices that fail or aren't requested are noted in the
    /// manifest; the operation never aborts on a single slice failure.
    /// </summary>
    public async Task<string> CaptureFullAsync(
        bool includeModel = true,
        bool includeUsers = true,
        bool includeServerSettings = true,
        bool includeExtensions = true,
        string? label = null,
        System.Threading.CancellationToken ct = default)
    {
        var env = _connection.Connected ?? throw new InvalidOperationException("Not connected.");
        var secret = _vault.GetSecret(env.Id);
        var meta = BuildMetadata(label);
        var folder = Path.Combine(EnvFolder(env.Name), $"Full__{Timestamp()}");

        // Capture each slice eagerly so any exception is captured by FullBackup.Capture's per-slice try.
        Func<LiveModel>? model = null;
        Func<UsersBackup>? users = null;
        Func<ServerSettingsBackup>? serverSettings = null;
        Func<ExtensionsBackup>? extensions = null;

        if (includeModel)
            model = () => _shell.CaptureSnapshotAsync(ct).GetAwaiter().GetResult();
        if (includeUsers)
            users = () => _shell.CaptureUsersBackupAsync(meta, ct).GetAwaiter().GetResult();
        if (includeServerSettings)
            serverSettings = () => _shell.CaptureServerSettingsBackupAsync(meta, ct).GetAwaiter().GetResult();
        if (includeExtensions)
            extensions = () => _shell.CaptureExtensionsBackupAsync(env, secret, meta, ct).GetAwaiter().GetResult();

        var full = await Task.Run(() => FullBackup.Capture(folder, meta, model, users, serverSettings, extensions), ct)
            .ConfigureAwait(false);
        RaiseChanged();
        return full.FolderPath;
    }

    /// <summary>Enumerate all backups under <see cref="BackupsRoot"/>. Filters by scope if specified.</summary>
    public IReadOnlyList<BackupFileInfo> List(string? scope = null)
    {
        if (!Directory.Exists(BackupsRoot)) return [];
        var results = new List<BackupFileInfo>();
        foreach (var envDir in Directory.EnumerateDirectories(BackupsRoot))
        {
            var envName = Path.GetFileName(envDir);
            foreach (var scopeDir in Directory.EnumerateDirectories(envDir))
            {
                var scopeName = Path.GetFileName(scopeDir);
                if (scopeName.StartsWith("Full__", StringComparison.Ordinal))
                {
                    // Full snapshot folder
                    if (scope is not null && !string.Equals(scope, "Full", StringComparison.OrdinalIgnoreCase)) continue;
                    var manifest = Path.Combine(scopeDir, "_manifest.json");
                    if (!File.Exists(manifest)) continue;
                    results.Add(new BackupFileInfo(
                        BackupKind.Folder,
                        scopeDir,
                        envName,
                        "Full",
                        Directory.GetCreationTimeUtc(scopeDir),
                        FolderSize(scopeDir),
                        null));
                    continue;
                }

                if (scope is not null && !string.Equals(scope, scopeName, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var file in Directory.EnumerateFiles(scopeDir, "*.json"))
                {
                    var fi = new FileInfo(file);
                    results.Add(new BackupFileInfo(
                        BackupKind.File,
                        file,
                        envName,
                        scopeName,
                        fi.CreationTimeUtc,
                        fi.Length,
                        null));
                }
            }
        }
        return results.OrderByDescending(b => b.CapturedAtUtc).ToList();
    }

    private string NewFilePath(string envName, string scope) =>
        Path.Combine(ScopeFolder(envName, scope), $"{Timestamp()}.json");

    private static string Timestamp() => DateTime.UtcNow.ToString("yyyyMMdd-HHmmssZ");

    private static string Sanitize(string s) =>
        new(s.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray());

    private static long FolderSize(string folder)
    {
        try { return new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); }
        catch { return 0; }
    }
}
