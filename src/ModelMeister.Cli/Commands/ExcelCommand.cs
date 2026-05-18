using Spectre.Console;
using ModelMeister.Excel;
using ModelMeister.Inriver;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Scaffolder;

namespace ModelMeister.Cli.Commands;

/// <summary>Excel export/import. The workbook mirrors <see cref="InriverModelJson"/> shape.</summary>
public static class ExcelCommand
{
    public static int ExportFromJson(string jsonPath, string xlsxPath)
    {
        if (!File.Exists(jsonPath))
        {
            AnsiConsole.MarkupLine($"[red]JSON file not found: {jsonPath.EscapeMarkup()}[/]");
            return ExitCodes.UsageError;
        }
        var model = InriverModelJson.Load(jsonPath);
        ModelWorkbook.Save(model, xlsxPath);
        AnsiConsole.MarkupLine($"[green]Wrote {xlsxPath.EscapeMarkup()}[/]");
        return ExitCodes.Success;
    }

    public static async Task<int> ExportFromEnvAsync(string url, InriverAuth auth, string xlsxPath, CancellationToken ct)
    {
        using var client = new InriverClient(url);
        var rc = await auth.ConnectAsync(client).ConfigureAwait(false);
        if (rc != ExitCodes.Success) return rc;
        try
        {
            var snapshot = new InriverSnapshot(client).Capture();
            var jsonModel = LiveModelConverter.ToJsonModel(snapshot);
            ModelWorkbook.Save(jsonModel, xlsxPath);
            AnsiConsole.MarkupLine($"[green]Exported {url.EscapeMarkup()} to {xlsxPath.EscapeMarkup()}[/]");
            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Export failed:[/] {ex.Message.EscapeMarkup()}");
            return ExitCodes.OperationFailed;
        }
    }

    public static int ImportToJson(string xlsxPath, string jsonPath)
    {
        if (!File.Exists(xlsxPath))
        {
            AnsiConsole.MarkupLine($"[red]Excel file not found: {xlsxPath.EscapeMarkup()}[/]");
            return ExitCodes.UsageError;
        }
        var model = ModelWorkbook.Load(xlsxPath);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(jsonPath))!);
        File.WriteAllText(jsonPath, System.Text.Json.JsonSerializer.Serialize(model, InriverModelJson.Options));
        AnsiConsole.MarkupLine($"[green]Wrote {jsonPath.EscapeMarkup()}[/]");
        return ExitCodes.Success;
    }
}
