using System.Text.Json;
using ModelMeister.Inriver.Snapshot;

namespace ModelMeister.Inriver.Backup;

/// <summary>Identifier for one scope inside a full backup.</summary>
public enum BackupSlice
{
    Model,
    Users,
    ServerSettings,
    Extensions,
}

/// <summary>
/// Cross-cutting "everything at this point in time" backup. On disk this is a folder named
/// <c>{env}__{ISO-timestamp}/</c> containing one JSON per slice plus a <c>_manifest.json</c>.
/// Captures and restores happen slice-by-slice so a partial failure doesn't poison the rest.
/// </summary>
public sealed class FullBackup
{
    public BackupMetadata Metadata { get; init; } = new();
    public string FolderPath { get; init; } = "";

    /// <summary>Captured slices and their on-disk filenames.</summary>
    public Dictionary<BackupSlice, string> Slices { get; init; } = new();

    /// <summary>Status of each slice's capture (Ok / Skipped / Failed with message).</summary>
    public Dictionary<BackupSlice, SliceStatus> Status { get; init; } = new();

    public sealed record SliceStatus(string State, string? Detail = null)
    {
        public static SliceStatus Ok => new("Ok");
        public static SliceStatus Skipped(string why) => new("Skipped", why);
        public static SliceStatus Failed(string why) => new("Failed", why);
    }

    /// <summary>Standard slice filenames within the folder.</summary>
    private const string ModelFile          = "model.json";
    private const string UsersFile          = "users.json";
    private const string ServerSettingsFile = "serversettings.json";
    private const string ExtensionsFile     = "extensions.json";
    private const string ManifestFile       = "_manifest.json";

    /// <summary>
    /// Capture all slices into <paramref name="folderPath"/>. Each <c>captureXxx</c> callback may
    /// be <c>null</c> to skip that slice — useful when the caller wants only some scopes (e.g.,
    /// for a "model + users" snapshot).
    /// </summary>
    public static FullBackup Capture(
        string folderPath,
        BackupMetadata metadata,
        Func<LiveModel>? captureModel = null,
        Func<UsersBackup>? captureUsers = null,
        Func<ServerSettingsBackup>? captureServerSettings = null,
        Func<ExtensionsBackup>? captureExtensions = null)
    {
        Directory.CreateDirectory(folderPath);
        var slices = new Dictionary<BackupSlice, string>();
        var status = new Dictionary<BackupSlice, SliceStatus>();

        TryCaptureSlice(BackupSlice.Model, captureModel,
            data => LiveModelJson.Save(data, Path.Combine(folderPath, ModelFile)),
            ModelFile, slices, status);

        TryCaptureSlice(BackupSlice.Users, captureUsers,
            data => data.Save(Path.Combine(folderPath, UsersFile)),
            UsersFile, slices, status);

        TryCaptureSlice(BackupSlice.ServerSettings, captureServerSettings,
            data => data.Save(Path.Combine(folderPath, ServerSettingsFile)),
            ServerSettingsFile, slices, status);

        TryCaptureSlice(BackupSlice.Extensions, captureExtensions,
            data => data.Save(Path.Combine(folderPath, ExtensionsFile)),
            ExtensionsFile, slices, status);

        var full = new FullBackup
        {
            Metadata = metadata,
            FolderPath = folderPath,
            Slices = slices,
            Status = status,
        };
        full.SaveManifest();
        return full;
    }

    private static void TryCaptureSlice<T>(
        BackupSlice slice,
        Func<T>? capture,
        Action<T> save,
        string filename,
        Dictionary<BackupSlice, string> slices,
        Dictionary<BackupSlice, SliceStatus> status)
    {
        if (capture is null)
        {
            status[slice] = SliceStatus.Skipped("Not requested.");
            return;
        }
        try
        {
            var data = capture();
            save(data);
            slices[slice] = filename;
            status[slice] = SliceStatus.Ok;
        }
        catch (Exception ex)
        {
            status[slice] = SliceStatus.Failed(ex.Message);
        }
    }

    /// <summary>Write the manifest to <c>{folder}/_manifest.json</c>.</summary>
    private void SaveManifest()
    {
        var manifest = new
        {
            Metadata,
            Slices = Slices.ToDictionary(s => s.Key.ToString(), s => s.Value),
            Status = Status.ToDictionary(s => s.Key.ToString(), s => s.Value),
        };
        File.WriteAllText(Path.Combine(FolderPath, ManifestFile),
            JsonSerializer.Serialize(manifest, BackupJson.Options));
    }

    /// <summary>Open a previously saved full backup folder. Reads the manifest and locates slices.</summary>
    public static FullBackup Open(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException(folderPath);

        var manifestPath = Path.Combine(folderPath, ManifestFile);
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("Missing _manifest.json — not a Full backup folder.", manifestPath);

        var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = doc.RootElement;

        var metadata = root.TryGetProperty("Metadata", out var md)
            ? JsonSerializer.Deserialize<BackupMetadata>(md.GetRawText(), BackupJson.Options) ?? new BackupMetadata()
            : new BackupMetadata();

        var slices = new Dictionary<BackupSlice, string>();
        if (root.TryGetProperty("Slices", out var sl))
            foreach (var prop in sl.EnumerateObject())
                if (Enum.TryParse<BackupSlice>(prop.Name, out var key))
                    slices[key] = prop.Value.GetString() ?? "";

        var status = new Dictionary<BackupSlice, SliceStatus>();
        if (root.TryGetProperty("Status", out var st))
            foreach (var prop in st.EnumerateObject())
                if (Enum.TryParse<BackupSlice>(prop.Name, out var key))
                    status[key] = JsonSerializer.Deserialize<SliceStatus>(prop.Value.GetRawText(), BackupJson.Options)
                        ?? new SliceStatus("Unknown");

        return new FullBackup
        {
            Metadata = metadata,
            FolderPath = folderPath,
            Slices = slices,
            Status = status,
        };
    }

    /// <summary>Path to a slice's JSON file within the folder, or <c>null</c> if the slice wasn't captured.</summary>
    public string? PathFor(BackupSlice slice) =>
        Slices.TryGetValue(slice, out var rel) ? Path.Combine(FolderPath, rel) : null;
}
