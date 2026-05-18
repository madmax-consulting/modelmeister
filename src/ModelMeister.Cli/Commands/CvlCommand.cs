using Spectre.Console;
using ModelMeister.Excel;
using ModelMeister.Inriver;
using ModelMeister.Inriver.Cvl;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Scaffolder;

namespace ModelMeister.Cli.Commands;

public static class CvlCommand
{
    public static async Task<int> ExportEnvAsync(string url, InriverAuth auth, string xlsxPath, CancellationToken ct)
    {
        using var client = new InriverClient(url);
        var rc = await auth.ConnectAsync(client).ConfigureAwait(false);
        if (rc != ExitCodes.Success) return rc;
        try
        {
            var snapshot = new InriverSnapshot(client).Capture();
            var json = LiveModelConverter.ToJsonModel(snapshot);
            CvlValuesWorkbook.Save(json, xlsxPath);
            AnsiConsole.MarkupLine($"[green]Wrote {xlsxPath.EscapeMarkup()} ({json.Cvls.Count} CVLs, {json.CvlValues.Count} values)[/]");
            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]CVL export failed:[/] {ex.Message.EscapeMarkup()}");
            return ExitCodes.OperationFailed;
        }
    }

    public static int ExportJson(string jsonPath, string xlsxPath)
    {
        if (!File.Exists(jsonPath))
        {
            AnsiConsole.MarkupLine($"[red]JSON file not found: {jsonPath.EscapeMarkup()}[/]");
            return ExitCodes.UsageError;
        }
        var model = InriverModelJson.Load(jsonPath);
        CvlValuesWorkbook.Save(model, xlsxPath);
        AnsiConsole.MarkupLine($"[green]Wrote {xlsxPath.EscapeMarkup()}[/]");
        return ExitCodes.Success;
    }

    public static async Task<int> ImportAsync(
        string url, InriverAuth auth, string xlsxPath,
        bool allowDeactivate, bool dryRun, CancellationToken ct)
    {
        if (!File.Exists(xlsxPath))
        {
            AnsiConsole.MarkupLine($"[red]Excel file not found: {xlsxPath.EscapeMarkup()}[/]");
            return ExitCodes.UsageError;
        }
        using var client = new InriverClient(url);
        var rc = await auth.ConnectAsync(client).ConfigureAwait(false);
        if (rc != ExitCodes.Success) return rc;

        // Build a synthetic LiveModel that the sync engine can read from. We capture only what we
        // need (CVLs + values), so this skips the rest of the model.
        var workbookModel = CvlValuesWorkbook.Load(xlsxPath);
        var sourceLive = SnapshotFromJson(workbookModel);

        var sync = new CvlSync(sourceLive, client);
        var totals = (Added: 0, Updated: 0, Deactivated: 0, Errors: 0);
        var opts = new CvlSync.Options(AllowDeactivate: allowDeactivate, OverwriteValues: true, DryRun: dryRun);

        foreach (var cvl in workbookModel.Cvls)
        {
            var plan = sync.PlanFor(cvl.Id, opts);
            if (plan.Total == 0)
            {
                AnsiConsole.MarkupLine($"  [grey]= {cvl.Id}: nothing to change[/]");
                continue;
            }
            AnsiConsole.MarkupLine($"  [yellow]~ {cvl.Id}:[/] +{plan.Add.Count} ~{plan.Update.Count} -{plan.Deactivate.Count}");
            var result = await sync.ApplyAsync(plan, opts, ct).ConfigureAwait(false);
            totals.Added += result.Added;
            totals.Updated += result.Updated;
            totals.Deactivated += result.Deactivated;
            totals.Errors += result.Errors.Count;
            foreach (var e in result.Errors) AnsiConsole.MarkupLine($"    [red]err:[/] {e.EscapeMarkup()}");
        }
        AnsiConsole.MarkupLine($"[green]Done.[/] Added {totals.Added}, updated {totals.Updated}, deactivated {totals.Deactivated}, errors {totals.Errors}.");
        return totals.Errors > 0 ? ExitCodes.OperationFailed : ExitCodes.Success;
    }

    public static async Task<int> SyncAsync(
        string sourceJsonPath, string targetUrl, InriverAuth auth,
        string? singleCvl, bool allowDeactivate, bool dryRun, CancellationToken ct)
    {
        if (!File.Exists(sourceJsonPath))
        {
            AnsiConsole.MarkupLine($"[red]Source JSON file not found: {sourceJsonPath.EscapeMarkup()}[/]");
            AnsiConsole.MarkupLine("[grey]Capture the source first: modelmeister export --url <source> --out source.json[/]");
            return ExitCodes.UsageError;
        }
        using var client = new InriverClient(targetUrl);
        var rc = await auth.ConnectAsync(client).ConfigureAwait(false);
        if (rc != ExitCodes.Success) return rc;

        var sourceModel = InriverModelJson.Load(sourceJsonPath);
        var sourceLive = SnapshotFromJson(sourceModel);
        var sync = new CvlSync(sourceLive, client);
        var opts = new CvlSync.Options(AllowDeactivate: allowDeactivate, OverwriteValues: true, DryRun: dryRun);

        var cvls = singleCvl is null
            ? sourceModel.Cvls.Select(c => c.Id).ToList()
            : [singleCvl];

        int added = 0, updated = 0, deactivated = 0, errors = 0;
        foreach (var id in cvls)
        {
            var plan = sync.PlanFor(id, opts);
            if (plan.Total == 0) continue;
            AnsiConsole.MarkupLine($"  [yellow]~ {id}:[/] +{plan.Add.Count} ~{plan.Update.Count} -{plan.Deactivate.Count}");
            var r = await sync.ApplyAsync(plan, opts, ct).ConfigureAwait(false);
            added += r.Added; updated += r.Updated; deactivated += r.Deactivated; errors += r.Errors.Count;
            foreach (var e in r.Errors) AnsiConsole.MarkupLine($"    [red]err:[/] {e.EscapeMarkup()}");
        }
        AnsiConsole.MarkupLine($"[green]Sync complete.[/] Added {added}, updated {updated}, deactivated {deactivated}, errors {errors}.");
        return errors > 0 ? ExitCodes.OperationFailed : ExitCodes.Success;
    }

    static LiveModel SnapshotFromJson(InriverModelJson json)
    {
        // Build a minimal LiveModel from the JSON model so the sync engine can read values
        // without needing a live connection to the source.
        var cvls = new List<LiveCvl>();
        var valuesByCvl = json.CvlValues.GroupBy(v => v.CvlId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var nextValueId = 1;
        foreach (var cvl in json.Cvls)
        {
            var values = (valuesByCvl.TryGetValue(cvl.Id, out var vs) ? vs : new())
                .OrderBy(v => v.Index).ThenBy(v => v.Key)
                .Select(v => new LiveCvlValue
                {
                    Id = nextValueId++,
                    CvlId = cvl.Id,
                    Key = v.Key,
                    Value = ToLocaleString(v.Value),
                    ParentKey = v.ParentKey,
                    Index = v.Index,
                    Deactivated = v.Deactivated,
                })
                .ToList();
            cvls.Add(new LiveCvl
            {
                Id = cvl.Id,
                DataTypeRaw = cvl.DataType,
                DataType = ParseCvlDataType(cvl.DataType),
                ParentId = cvl.ParentId,
                CustomValueList = cvl.CustomValueList,
                Values = values,
            });
        }
        return new LiveModel
        {
            EnvironmentUrl = "source:" + Path.GetFileName(Environment.CurrentDirectory),
            CapturedUtc = DateTime.UtcNow,
            EntityTypes = Array.Empty<LiveEntityType>(),
            Cvls = cvls,
            Categories = Array.Empty<LiveCategory>(),
            Fieldsets = Array.Empty<LiveFieldset>(),
            LinkTypes = Array.Empty<LiveLinkType>(),
            Roles = Array.Empty<LiveRole>(),
            Permissions = Array.Empty<LivePermission>(),
            CompletenessDefinitions = Array.Empty<LiveCompletenessDefinition>(),
            RestrictedFieldPermissions = Array.Empty<LiveRestrictedFieldPermission>(),
            Languages = json.Languages.Select(l => l.Name).ToList(),
        };
    }

    static ModelMeister.Model.Primitives.LocaleString ToLocaleString(System.Text.Json.JsonElement el)
    {
        if (el.ValueKind == System.Text.Json.JsonValueKind.Object && el.TryGetProperty("StringMap", out var map) && map.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in map.EnumerateObject())
                if (p.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                    dict[p.Name] = p.Value.GetString() ?? string.Empty;
            var def = dict.Values.FirstOrDefault() ?? string.Empty;
            return new ModelMeister.Model.Primitives.LocaleString(def, dict);
        }
        return new ModelMeister.Model.Primitives.LocaleString(
            el.ValueKind == System.Text.Json.JsonValueKind.String ? el.GetString() ?? "" : el.ToString());
    }

    static ModelMeister.Model.Primitives.CvlDataType ParseCvlDataType(string raw) => raw switch
    {
        "String" => ModelMeister.Model.Primitives.CvlDataType.String,
        "LocaleString" => ModelMeister.Model.Primitives.CvlDataType.LocaleString,
        "Integer" => ModelMeister.Model.Primitives.CvlDataType.Integer,
        "Double" => ModelMeister.Model.Primitives.CvlDataType.Double,
        "DateTime" => ModelMeister.Model.Primitives.CvlDataType.DateTime,
        _ => ModelMeister.Model.Primitives.CvlDataType.String,
    };
}
