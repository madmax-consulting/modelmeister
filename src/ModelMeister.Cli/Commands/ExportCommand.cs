using Spectre.Console;
using ModelMeister.Inriver;
using ModelMeister.Inriver.Snapshot;

namespace ModelMeister.Cli.Commands;

/// <summary>Snapshots a live inriver environment to a JSON file.</summary>
public static class ExportCommand
{
    /// <summary>Connects, captures the model, and writes it to <paramref name="outPath"/>.</summary>
    public static async Task<int> RunAsync(string url, InriverAuth auth, string outPath, CancellationToken ct)
    {
        using var client = new InriverClient(url);
        var rc = await auth.ConnectAsync(client).ConfigureAwait(false);
        if (rc != ExitCodes.Success) return rc;

        var snap = new InriverSnapshot(client).Capture();
        LiveModelJson.Save(snap, outPath);

        AnsiConsole.MarkupLine($"[green]Wrote snapshot to {outPath.EscapeMarkup()}[/]");
        return ExitCodes.Success;
    }
}
