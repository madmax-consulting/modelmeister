using Spectre.Console;
using ModelMeister.Excel;
using ModelMeister.Inriver;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Scaffolder;

namespace ModelMeister.Cli.Commands;

/// <summary>
/// Generates a starter C# model project from either a static inriver JSON export
/// or a live environment snapshot.
/// </summary>
public static class ScaffoldCommand
{
    /// <summary>Scaffolds from a local inriver JSON export.</summary>
    public static int Run(string jsonPath, string outDir, string rootNamespace, bool detectBaseClasses, bool emitCvlValues = true)
    {
        if (!File.Exists(jsonPath))
        {
            AnsiConsole.MarkupLine($"[red]JSON file not found: {jsonPath.EscapeMarkup()}[/]");
            return ExitCodes.UsageError;
        }

        AnsiConsole.MarkupLine($"Scaffolding [green]{jsonPath.EscapeMarkup()}[/] -> [blue]{outDir.EscapeMarkup()}[/]");
        var result = new ProjectScaffolder().Scaffold(jsonPath, outDir, rootNamespace, detectBaseClasses, emitCvlValues);
        RenderResult(result);
        return ExitCodes.Success;
    }

    /// <summary>Scaffolds from an Excel workbook produced by <c>excel export</c>.</summary>
    public static int RunFromExcel(string xlsxPath, string outDir, string rootNamespace, bool detectBaseClasses, bool emitCvlValues = true)
    {
        if (!File.Exists(xlsxPath))
        {
            AnsiConsole.MarkupLine($"[red]Excel file not found: {xlsxPath.EscapeMarkup()}[/]");
            return ExitCodes.UsageError;
        }

        AnsiConsole.MarkupLine($"Scaffolding [green]{xlsxPath.EscapeMarkup()}[/] (Excel) -> [blue]{outDir.EscapeMarkup()}[/]");
        var result = ExcelScaffolder.ScaffoldFromExcel(xlsxPath, outDir, rootNamespace, detectBaseClasses, emitCvlValues);
        RenderResult(result);
        return ExitCodes.Success;
    }

    /// <summary>Scaffolds from a live inriver environment.</summary>
    public static async Task<int> RunFromEnvAsync(
        string url, InriverAuth auth, string outDir, string rootNamespace,
        bool detectBaseClasses, CancellationToken ct, bool emitCvlValues = true)
    {
        using var client = new InriverClient(url);
        var rc = await auth.ConnectAsync(client).ConfigureAwait(false);
        if (rc != ExitCodes.Success) return rc;

        AnsiConsole.MarkupLine($"Scaffolding [green]{url.EscapeMarkup()}[/] -> [blue]{outDir.EscapeMarkup()}[/]");
        try
        {
            var snapshot = new InriverSnapshot(client).Capture();
            var jsonModel = LiveModelConverter.ToJsonModel(snapshot);
            var result = new ProjectScaffolder().Scaffold(jsonModel, outDir, rootNamespace, detectBaseClasses, emitCvlValues);
            RenderResult(result);
            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            // Anything thrown here is transport- or snapshot-level — surface as operational failure.
            AnsiConsole.MarkupLine($"[red]Connection or snapshot failed:[/] {ex.Message.EscapeMarkup()}");
            return ExitCodes.OperationFailed;
        }
    }

    private static void RenderResult(ScaffoldResult result)
    {
        AnsiConsole.MarkupLine($"[green]Generated {result.Files.Count} files.[/]");

        const int MaxWarningsShown = 20;
        var warnings = result.WarningsFromExpressions;
        foreach (var w in warnings.Take(MaxWarningsShown))
            AnsiConsole.MarkupLine($"[yellow]warn:[/] {w.EscapeMarkup()}");

        var overflow = warnings.Count - MaxWarningsShown;
        if (overflow > 0)
            AnsiConsole.MarkupLine($"[yellow]... and {overflow} more[/]");
    }
}
