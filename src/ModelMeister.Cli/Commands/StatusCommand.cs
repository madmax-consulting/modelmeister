using Spectre.Console;
using ModelMeister.Inriver;
using ModelMeister.Inriver.Snapshot;

namespace ModelMeister.Cli.Commands;

/// <summary>Pings an inriver environment and prints concept counts.</summary>
public static class StatusCommand
{
    /// <summary>Connects, snapshots, and renders a count table.</summary>
    public static async Task<int> RunAsync(string url, InriverAuth auth, CancellationToken ct)
    {
        using var client = new InriverClient(url);
        var rc = await auth.ConnectAsync(client).ConfigureAwait(false);
        if (rc != ExitCodes.Success) return rc;

        try
        {
            var snap = new InriverSnapshot(client).Capture();
            var rows = new (string Concept, int Count)[]
            {
                ("Entity types", snap.EntityTypes.Count),
                ("CVLs",         snap.Cvls.Count),
                ("CVL values",   snap.Cvls.Sum(c => c.Values.Count)),
                ("Link types",   snap.LinkTypes.Count),
                ("Categories",   snap.Categories.Count),
                ("Fieldsets",    snap.Fieldsets.Count),
                ("Roles",        snap.Roles.Count),
                ("Permissions",  snap.Permissions.Count),
                ("Languages",    snap.Languages.Count),
            };

            var table = new Table()
                .Title($"[bold green]Connected to {url.EscapeMarkup()}[/]")
                .AddColumn("Concept")
                .AddColumn("Count");

            foreach (var (concept, count) in rows)
                table.AddRow(concept, count.ToString());

            AnsiConsole.Write(table);
            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Connection or snapshot failed:[/] {ex.Message.EscapeMarkup()}");
            return ExitCodes.OperationFailed;
        }
    }
}
