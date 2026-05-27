using System.Text.Json;
using ModelMeister.Inriver.HtmlTemplates;

namespace ModelMeister.Inriver.Backup;

/// <summary>
/// JSON backup of the HTML (print / ContentStore) templates in one environment. Bodies are stored inline;
/// restore reconciles by name + template type, independent of the per-env integer ids.
/// </summary>
public sealed record HtmlTemplatesBackup
{
    public BackupMetadata Metadata { get; init; } = new();
    public List<HtmlTemplateDto> Templates { get; init; } = [];

    /// <summary>Outcome row for the restore-result table.</summary>
    public sealed record RestoreEntry(string Name, string Op, bool Ok, string? Error = null);

    public static HtmlTemplatesBackup Capture(HtmlTemplateService service, BackupMetadata metadata) =>
        new() { Metadata = metadata, Templates = service.List().ToList() };

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(this, BackupJson.Options));
    }

    public static HtmlTemplatesBackup Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<HtmlTemplatesBackup>(json, BackupJson.Options)
            ?? throw new InvalidDataException($"HtmlTemplates backup at {path} was empty or null.");
    }

    /// <summary>Reconcile the backed-up templates into the live env by name + type (create + update; never
    /// deletes). Returns one outcome row per template.</summary>
    public async Task<List<RestoreEntry>> RestoreAsync(HtmlTemplateService service, CancellationToken ct = default)
    {
        var before = service.List()
            .Select(t => $"{t.Name}{t.TemplateType}")
            .ToHashSet(StringComparer.Ordinal);
        var result = await service.ApplyAsync(Templates, allowDeletes: false, ct).ConfigureAwait(false);
        var rows = Templates
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(t => new RestoreEntry(t.Name, before.Contains($"{t.Name}{t.TemplateType}") ? "updated" : "created", true))
            .ToList();
        if (result.Failed > 0)
            rows.Add(new RestoreEntry("(batch)", "error", false, $"{result.Failed} template(s) failed — see log."));
        return rows;
    }
}
