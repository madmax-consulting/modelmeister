using Spectre.Console;
using ModelMeister.Cli.Interactive;
using ModelMeister.Inriver;
using ModelMeister.Inriver.Apply;
using ModelMeister.Inriver.Diff;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Loading;

namespace ModelMeister.Cli.Commands;

/// <summary>Applies a code-defined model to a live inriver environment.</summary>
public static class ApplyCommand
{
    private const string BackupRoot = ".modelmeister";

    /// <summary>
    /// Runs a full diff/apply cycle: snapshot for backup, diff, optionally confirm, apply,
    /// then write a receipt. Returns a CI-friendly exit code.
    /// </summary>
    public static async Task<int> RunAsync(
        string modelPath, string url, InriverAuth auth,
        bool yes, bool dryRun, MergePolicy policy, CancellationToken ct)
    {
        var code = new ModelAssemblyLoader().LoadFromPath(modelPath);

        using var client = new InriverClient(url);
        var rc = await auth.ConnectAsync(client).ConfigureAwait(false);
        if (rc != ExitCodes.Success) return rc;

        AnsiConsole.MarkupLine("[grey]Capturing pre-apply snapshot for backup...[/]");
        var snap = new InriverSnapshot(client).Capture();

        var env = SafeName(url);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var backupPath = Path.Combine(BackupRoot, "backups", env, $"{timestamp}.model.json");

        if (!dryRun)
        {
            LiveModelJson.Save(snap, backupPath);
            AnsiConsole.MarkupLine($"[grey]Backup: {backupPath.EscapeMarkup()}[/]");
        }

        var changes = ModelDiffer.Diff(code, snap, policy);
        DiffRenderer.Render(changes);

        if (changes.IsEmpty)
        {
            AnsiConsole.MarkupLine("[green]✓ No changes — nothing to apply.[/]");
            return ExitCodes.Success;
        }

        if (dryRun)
        {
            // Dry-run signals pending changes (1) — the same code CI uses to gate merges.
            AnsiConsole.MarkupLine("[yellow]Dry-run — nothing applied.[/]");
            return ExitCodes.ChangesPending;
        }

        if (!yes && !AnsiConsole.Confirm($"Apply {changes.Changes.Count} change(s) to [yellow]{url}[/]?"))
        {
            // User aborted: there are still changes pending, so signal 1.
            AnsiConsole.MarkupLine("[yellow]Aborted.[/]");
            return ExitCodes.ChangesPending;
        }

        var receipt = await new ChangeApplier(client)
            .ApplyAsync(changes, code, snap, dryRun: false, backupPath, ct)
            .ConfigureAwait(false);

        var receiptPath = Path.Combine(BackupRoot, "receipts", env, $"{timestamp}.json");
        receipt.SaveTo(receiptPath);
        AnsiConsole.MarkupLine($"[grey]Receipt: {receiptPath.EscapeMarkup()}[/]");

        if (receipt.Failed > 0)
        {
            // Partial apply -> operational failure (4) so CI distinguishes it from a clean rollback.
            AnsiConsole.MarkupLine($"[red]{receipt.Failed} change(s) failed, {receipt.Succeeded} succeeded.[/]");
            return ExitCodes.OperationFailed;
        }

        AnsiConsole.MarkupLine($"[green]✓ Apply complete. {receipt.Succeeded} change(s).[/]");
        return ExitCodes.Success;
    }

    /// <summary>Sanitises a URL into a filesystem-safe folder name.</summary>
    private static string SafeName(string url) =>
        new(url.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
}
