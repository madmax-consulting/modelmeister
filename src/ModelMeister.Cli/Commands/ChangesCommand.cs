using System.Globalization;
using Spectre.Console;
using ModelMeister.Inriver;
using ModelMeister.Inriver.Snapshot;

namespace ModelMeister.Cli.Commands;

/// <summary>
/// Reports whether an environment's model changed since a given instant (or a snapshot's capture
/// time). Useful as a CI guard: fail the pipeline if prod drifted from the approved snapshot.
/// </summary>
public static class ChangesCommand
{
    /// <summary>
    /// Connect and call <c>GetEnvironmentLatestChanges</c>. The "since" instant comes from
    /// <paramref name="sinceIso"/> or, when null, the <c>CapturedUtc</c> of the snapshot at
    /// <paramref name="snapshotPath"/>. Exits 1 when changes are detected and
    /// <paramref name="failOnChanges"/> is set (CI gate).
    /// </summary>
    public static async Task<int> RunAsync(
        string url, InriverAuth auth, string? sinceIso, string? snapshotPath, bool failOnChanges, CancellationToken ct)
    {
        DateTime sinceUtc;
        if (!string.IsNullOrEmpty(sinceIso))
        {
            if (!DateTime.TryParse(sinceIso, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out sinceUtc))
            {
                AnsiConsole.MarkupLine($"[red]Could not parse --since '{sinceIso.EscapeMarkup()}' as a timestamp.[/]");
                return ExitCodes.UsageError;
            }
        }
        else if (!string.IsNullOrEmpty(snapshotPath))
        {
            try
            {
                var snap = LiveModelJson.Deserialize(await File.ReadAllTextAsync(snapshotPath, ct).ConfigureAwait(false))
                    ?? throw new InvalidDataException("Snapshot JSON did not deserialise.");
                sinceUtc = snap.CapturedUtc;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Could not read snapshot '{snapshotPath.EscapeMarkup()}':[/] {ex.Message.EscapeMarkup()}");
                return ExitCodes.UsageError;
            }
        }
        else
        {
            AnsiConsole.MarkupLine("[red]Provide --since <timestamp> or --snapshot <path>.[/]");
            return ExitCodes.UsageError;
        }

        using var client = new InriverClient(url);
        var rc = await auth.ConnectAsync(client).ConfigureAwait(false);
        if (rc != ExitCodes.Success) return rc;

        try
        {
            var changes = await Task.Run(() => new EnvironmentChangesService(client).Since(sinceUtc), ct).ConfigureAwait(false);
            AnsiConsole.MarkupLine($"[grey]Since {sinceUtc:u} on {url.EscapeMarkup()}:[/]");

            if (!changes.AnyChanges)
            {
                AnsiConsole.MarkupLine("[green]No model changes.[/]");
                return ExitCodes.Success;
            }

            AnsiConsole.MarkupLine($"[yellow]{changes.Summary().EscapeMarkup()}[/]");
            if (changes.ModelReloaded) AnsiConsole.MarkupLine("[yellow]The model was reloaded.[/]");
            foreach (var area in changes.ChangedAreas)
                AnsiConsole.MarkupLine($"  • {area.EscapeMarkup()}");

            // 1 == "changes pending" in the stable CLI exit-code contract (same as model diff).
            return failOnChanges ? ExitCodes.ChangesPending : ExitCodes.Success;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Change check failed:[/] {ex.Message.EscapeMarkup()}");
            return ExitCodes.OperationFailed;
        }
    }
}
