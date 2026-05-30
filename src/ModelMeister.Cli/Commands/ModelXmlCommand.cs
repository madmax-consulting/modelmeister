using Spectre.Console;
using ModelMeister.Inriver;
using ModelMeister.Inriver.ModelXml;
using ModelMeister.Inriver.Snapshot;

namespace ModelMeister.Cli.Commands;

/// <summary>
/// Native inriver model XML lift-and-shift from the command line: export the whole model as the
/// platform's own XML, or import one into an environment. Import captures a JSON backup first.
/// </summary>
public static class ModelXmlCommand
{
    /// <summary>Export the connected env's model to inriver-native XML at <paramref name="outPath"/>.</summary>
    public static async Task<int> ExportAsync(string url, InriverAuth auth, string outPath, bool includeCvlValues, CancellationToken ct)
    {
        using var client = new InriverClient(url);
        var rc = await auth.ConnectAsync(client).ConfigureAwait(false);
        if (rc != ExitCodes.Success) return rc;

        try
        {
            var xml = await Task.Run(() => new ModelXmlService(client).Export(includeCvlValues), ct).ConfigureAwait(false);
            var dir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(outPath, xml, ct).ConfigureAwait(false);
            AnsiConsole.MarkupLine($"[green]Wrote model XML ({xml.Length:N0} chars) to {outPath.EscapeMarkup()}[/]");
            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Model XML export failed:[/] {ex.Message.EscapeMarkup()}");
            return ExitCodes.OperationFailed;
        }
    }

    /// <summary>
    /// Import an inriver-native model XML at <paramref name="xmlPath"/> into the connected env. Unless
    /// <paramref name="noBackup"/> is set, a JSON snapshot of the current model is captured first.
    /// </summary>
    public static async Task<int> ImportAsync(string url, InriverAuth auth, string xmlPath, bool yes, bool noBackup, CancellationToken ct)
    {
        if (!File.Exists(xmlPath))
        {
            AnsiConsole.MarkupLine($"[red]File not found: {xmlPath.EscapeMarkup()}[/]");
            return ExitCodes.UsageError;
        }
        if (!yes)
        {
            AnsiConsole.MarkupLine(
                $"[yellow]This merges the model in {Path.GetFileName(xmlPath).EscapeMarkup()} into {url.EscapeMarkup()} "
                + "and cannot be dry-run. Re-run with --yes to proceed.[/]");
            return ExitCodes.UsageError;
        }

        using var client = new InriverClient(url);
        var rc = await auth.ConnectAsync(client).ConfigureAwait(false);
        if (rc != ExitCodes.Success) return rc;

        try
        {
            if (!noBackup)
            {
                var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                var backupPath = Path.Combine(
                    Path.GetDirectoryName(Path.GetFullPath(xmlPath)) ?? ".",
                    $"pre-xml-import-{stamp}.model.json");
                var snap = await Task.Run(() => new InriverSnapshot(client).Capture(), ct).ConfigureAwait(false);
                LiveModelJson.Save(snap, backupPath);
                AnsiConsole.MarkupLine($"[grey]Backed up current model to {backupPath.EscapeMarkup()}[/]");
            }

            var xml = await File.ReadAllTextAsync(xmlPath, ct).ConfigureAwait(false);
            var ok = await new ModelXmlService(client).ImportAsync(xml, ct).ConfigureAwait(false);
            if (ok)
            {
                AnsiConsole.MarkupLine($"[green]Model XML imported into {url.EscapeMarkup()}.[/]");
                return ExitCodes.Success;
            }
            AnsiConsole.MarkupLine("[red]inriver reported the import did not succeed.[/]");
            return ExitCodes.OperationFailed;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Model XML import failed:[/] {ex.Message.EscapeMarkup()}");
            return ExitCodes.OperationFailed;
        }
    }
}
