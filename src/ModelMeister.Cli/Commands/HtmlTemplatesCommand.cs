using Spectre.Console;
using ModelMeister.Excel;
using ModelMeister.Inriver;
using ModelMeister.Inriver.HtmlTemplates;

namespace ModelMeister.Cli.Commands;

/// <summary>
/// CLI surface for HTML templates: list, export/import an Excel workbook, and promote templates from one
/// environment to another. Templates are matched by name + template type (inriver ids differ per env).
/// Oversize bodies spill to a sidecar folder next to the workbook (see <see cref="HtmlTemplateWorkbook"/>).
/// </summary>
public static class HtmlTemplatesCommand
{
    public static async Task<int> ListAsync(string url, InriverAuth auth, CancellationToken ct)
    {
        using var client = new InriverClient(url);
        var rc = await auth.ConnectAsync(client).ConfigureAwait(false);
        if (rc != ExitCodes.Success) return rc;
        try
        {
            var templates = new HtmlTemplateService(client).List();
            var table = new Table().AddColumns("Name", "Type", "Size");
            foreach (var t in templates)
                table.AddRow(t.Name.EscapeMarkup(), t.TemplateType.EscapeMarkup(), $"{t.Content.Length:n0} chars");
            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"[grey]{templates.Count} template(s).[/]");
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
            var templates = new HtmlTemplateService(client).List();
            HtmlTemplateWorkbook.Save(templates, outPath);
            AnsiConsole.MarkupLine($"[green]Wrote {templates.Count} template(s) to[/] {outPath.EscapeMarkup()}");
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
        var desired = HtmlTemplateWorkbook.Load(excelPath);
        if (dryRun)
        {
            AnsiConsole.MarkupLine($"[yellow]DRY-RUN[/] — {desired.Count} template(s) in workbook:");
            foreach (var t in desired) AnsiConsole.MarkupLine($"  {t.Name.EscapeMarkup()} [grey]({t.TemplateType.EscapeMarkup()})[/]");
            return ExitCodes.Success;
        }

        using var client = new InriverClient(url);
        var rc = await auth.ConnectAsync(client).ConfigureAwait(false);
        if (rc != ExitCodes.Success) return rc;
        try
        {
            var result = await new HtmlTemplateService(client).ApplyAsync(desired, allowDeletes, ct).ConfigureAwait(false);
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
        // Read the source env fully into DTOs (body included), then connect the target and reconcile by name+type.
        IReadOnlyList<HtmlTemplateDto> source;
        using (var src = new InriverClient(fromUrl))
        {
            var rc = await fromAuth.ConnectAsync(src).ConfigureAwait(false);
            if (rc != ExitCodes.Success) return rc;
            try { source = new HtmlTemplateService(src).List(); }
            catch (Exception ex) { AnsiConsole.MarkupLine($"[red]Read source failed:[/] {ex.Message.EscapeMarkup()}"); return ExitCodes.OperationFailed; }
        }

        using var tgt = new InriverClient(toUrl);
        var trc = await toAuth.ConnectAsync(tgt).ConfigureAwait(false);
        if (trc != ExitCodes.Success) return trc;
        try
        {
            var result = await new HtmlTemplateService(tgt).ApplyAsync(source, allowDeletes, ct).ConfigureAwait(false);
            return Report(result);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Promote failed:[/] {ex.Message.EscapeMarkup()}");
            return ExitCodes.OperationFailed;
        }
    }

    private static int Report(HtmlTemplateApplyResult r)
    {
        AnsiConsole.MarkupLine(
            $"[green]Created {r.Created}[/], [blue]updated {r.Updated}[/], [grey]unchanged {r.Unchanged}[/], [yellow]deleted {r.Deleted}[/]" +
            (r.Failed > 0 ? $", [red]{r.Failed} failed[/]" : ""));
        return r.Failed > 0 ? ExitCodes.OperationFailed : ExitCodes.Success;
    }
}
