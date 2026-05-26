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
        var sourceLive = LiveModelConverter.CvlSourceFromJson(workbookModel);

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
        var sourceLive = LiveModelConverter.CvlSourceFromJson(sourceModel);
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

}
