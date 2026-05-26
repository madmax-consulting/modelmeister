using Spectre.Console;
using ModelMeister.Excel;
using ModelMeister.Inriver;
using ModelMeister.Inriver.WorkAreas;

namespace ModelMeister.Cli.Commands;

/// <summary>
/// CLI surface for shared work-area folders + saved queries: list, export/import an Excel workbook, and
/// promote folders (with their queries) from one environment to another. Queries are carried verbatim on
/// promote and as an opaque JSON blob in the workbook.
/// </summary>
public static class WorkAreasCommand
{
    public static async Task<int> ListAsync(string url, InriverAuth auth, CancellationToken ct)
    {
        using var client = new InriverClient(url);
        var rc = await auth.ConnectAsync(client).ConfigureAwait(false);
        if (rc != ExitCodes.Success) return rc;
        try
        {
            var folders = new WorkAreaService(client).List();
            var table = new Table().AddColumns("Path", "Query", "Syndication");
            foreach (var f in folders)
                table.AddRow(
                    f.Path.EscapeMarkup(),
                    f.IsQuery ? "[green]yes[/]" : "",
                    f.IsSyndication ? "[blue]yes[/]" : "");
            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"[grey]{folders.Count} folder(s).[/]");
            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]List failed:[/] {ex.Message.EscapeMarkup()}");
            return ExitCodes.OperationFailed;
        }
    }

    public static async Task<int> ExportAsync(string url, InriverAuth auth, string outPath, CancellationToken ct)
    {
        using var client = new InriverClient(url);
        var rc = await auth.ConnectAsync(client).ConfigureAwait(false);
        if (rc != ExitCodes.Success) return rc;
        try
        {
            var folders = new WorkAreaService(client).List();
            WorkAreaWorkbook.Save(folders, outPath);
            AnsiConsole.MarkupLine($"[green]Wrote {folders.Count} folder(s) to[/] {outPath.EscapeMarkup()}");
            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Export failed:[/] {ex.Message.EscapeMarkup()}");
            return ExitCodes.OperationFailed;
        }
    }

    public static async Task<int> ImportAsync(string url, InriverAuth auth, string excelPath, bool allowDeletes, bool dryRun, CancellationToken ct)
    {
        var desired = WorkAreaWorkbook.Load(excelPath);
        if (dryRun)
        {
            AnsiConsole.MarkupLine($"[yellow]DRY-RUN[/] — {desired.Count} folder(s) in workbook:");
            foreach (var f in desired) AnsiConsole.MarkupLine($"  {f.Path.EscapeMarkup()}{(f.IsQuery ? " [grey](query)[/]" : "")}");
            return ExitCodes.Success;
        }

        using var client = new InriverClient(url);
        var rc = await auth.ConnectAsync(client).ConfigureAwait(false);
        if (rc != ExitCodes.Success) return rc;
        try
        {
            var result = await new WorkAreaService(client).ApplyAsync(desired, allowDeletes, ct).ConfigureAwait(false);
            return Report(result);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Import failed:[/] {ex.Message.EscapeMarkup()}");
            return ExitCodes.OperationFailed;
        }
    }

    public static async Task<int> PromoteAsync(
        string fromUrl, InriverAuth fromAuth, string toUrl, InriverAuth toAuth, bool allowDeletes, CancellationToken ct)
    {
        // Read the source env fully into memory (keeps the live query objects), then connect the target.
        IReadOnlyList<inRiver.Remoting.Objects.WorkAreaFolder> source;
        using (var src = new InriverClient(fromUrl))
        {
            var rc = await fromAuth.ConnectAsync(src).ConfigureAwait(false);
            if (rc != ExitCodes.Success) return rc;
            try { source = new WorkAreaService(src).GetRawFolders(); }
            catch (Exception ex) { AnsiConsole.MarkupLine($"[red]Read source failed:[/] {ex.Message.EscapeMarkup()}"); return ExitCodes.OperationFailed; }
        }

        using var tgt = new InriverClient(toUrl);
        var trc = await toAuth.ConnectAsync(tgt).ConfigureAwait(false);
        if (trc != ExitCodes.Success) return trc;
        try
        {
            var result = await new WorkAreaService(tgt).ApplyAsync(source, allowDeletes, ct).ConfigureAwait(false);
            return Report(result);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Promote failed:[/] {ex.Message.EscapeMarkup()}");
            return ExitCodes.OperationFailed;
        }
    }

    private static int Report(WorkAreaApplyResult r)
    {
        AnsiConsole.MarkupLine(
            $"[green]Created {r.Created}[/], [blue]updated {r.Updated}[/], [yellow]deleted {r.Deleted}[/]" +
            (r.Failed > 0 ? $", [red]{r.Failed} failed[/]" : ""));
        return r.Failed > 0 ? ExitCodes.OperationFailed : ExitCodes.Success;
    }
}
