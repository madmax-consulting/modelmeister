using System.Globalization;
using System.Text.Json;
using Spectre.Console;
using ModelMeister.Inriver;
using ModelMeister.Inriver.Statistics;

namespace ModelMeister.Cli.Commands;

/// <summary>Prints per-entity-type instance counts from a live environment (data-volume at a glance).</summary>
public static class StatsCommand
{
    /// <summary>Connect, read <c>GetAllEntityTypeStatistics</c>, and render a table (or JSON for CI).</summary>
    public static async Task<int> RunAsync(string url, InriverAuth auth, bool json, CancellationToken ct)
    {
        using var client = new InriverClient(url);
        var rc = await auth.ConnectAsync(client).ConfigureAwait(false);
        if (rc != ExitCodes.Success) return rc;

        try
        {
            var stats = await Task.Run(() => new EntityStatisticsService(client).Capture(), ct).ConfigureAwait(false);

            if (json)
            {
                var payload = new
                {
                    url,
                    capturedUtc = stats.CapturedUtc,
                    totalEntities = stats.TotalEntities,
                    types = stats.Types.Select(t => new { t.EntityTypeId, t.Name, t.Total, t.NewLastWeek, t.UpdatedLastWeek }),
                };
                Console.Out.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
                return ExitCodes.Success;
            }

            var table = new Table()
                .Title($"[bold green]Entity statistics · {url.EscapeMarkup()}[/]")
                .AddColumn("Entity type")
                .AddColumn(new TableColumn("Instances").RightAligned())
                .AddColumn(new TableColumn("New (7d)").RightAligned())
                .AddColumn(new TableColumn("Updated (7d)").RightAligned());

            foreach (var t in stats.Types)
                table.AddRow(
                    t.EntityTypeId.EscapeMarkup(),
                    t.Total.ToString("N0", CultureInfo.InvariantCulture),
                    t.NewLastWeek.ToString("N0", CultureInfo.InvariantCulture),
                    t.UpdatedLastWeek.ToString("N0", CultureInfo.InvariantCulture));

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine(
                $"[grey]{stats.TotalEntities.ToString("N0", CultureInfo.InvariantCulture)} instances across {stats.Types.Count} type(s).[/]");
            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Statistics read failed:[/] {ex.Message.EscapeMarkup()}");
            return ExitCodes.OperationFailed;
        }
    }
}
