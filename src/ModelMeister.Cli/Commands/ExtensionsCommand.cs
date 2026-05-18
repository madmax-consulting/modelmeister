using Spectre.Console;
using ModelMeister.Inriver;
using ModelMeister.Inriver.Extensions;
using ModelMeister.Rest;

namespace ModelMeister.Cli.Commands;

public static class ExtensionsCommand
{
    public static async Task<int> ListAsync(string url, InriverAuth auth, string? restBaseUrl, string? restKey, CancellationToken ct)
    {
        using var client = new InriverClient(url);
        var rc = await auth.ConnectAsync(client).ConfigureAwait(false);
        if (rc != ExitCodes.Success) return rc;
        using var rest = (string.IsNullOrEmpty(restBaseUrl) || string.IsNullOrEmpty(restKey)) ? null : new InriverRestClient(restBaseUrl, restKey);
        try
        {
            var items = new ExtensionsService(client, rest).List();
            var table = new Table().AddColumns("Id", "TypeName", "Started", "Last event", "Errors", "Settings");
            foreach (var e in items)
                table.AddRow(
                    e.Id,
                    e.TypeName ?? "",
                    e.IsStarted ? "[green]yes[/]" : "[red]no[/]",
                    e.LastEventUtc?.ToString("u") ?? "",
                    e.RecentErrorCount.ToString(),
                    e.Settings.Count.ToString());
            AnsiConsole.Write(table);
            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]List failed:[/] {ex.Message.EscapeMarkup()}");
            return ExitCodes.OperationFailed;
        }
    }

    public static async Task<int> StartAsync(string url, InriverAuth auth, string? restBaseUrl, string? restKey, string id, CancellationToken ct)
    {
        using var client = new InriverClient(url);
        var rc = await auth.ConnectAsync(client).ConfigureAwait(false);
        if (rc != ExitCodes.Success) return rc;
        using var rest = (string.IsNullOrEmpty(restBaseUrl) || string.IsNullOrEmpty(restKey)) ? null : new InriverRestClient(restBaseUrl, restKey);
        var ok = await new ExtensionsService(client, rest).StartAsync(id, ct).ConfigureAwait(false);
        AnsiConsole.MarkupLine(ok ? $"[green]Started {id.EscapeMarkup()}[/]" : $"[red]Start failed: {id.EscapeMarkup()}[/]");
        return ok ? ExitCodes.Success : ExitCodes.OperationFailed;
    }

    public static async Task<int> StopAsync(string url, InriverAuth auth, string? restBaseUrl, string? restKey, string id, CancellationToken ct)
    {
        using var client = new InriverClient(url);
        var rc = await auth.ConnectAsync(client).ConfigureAwait(false);
        if (rc != ExitCodes.Success) return rc;
        using var rest = (string.IsNullOrEmpty(restBaseUrl) || string.IsNullOrEmpty(restKey)) ? null : new InriverRestClient(restBaseUrl, restKey);
        var ok = await new ExtensionsService(client, rest).StopAsync(id, ct).ConfigureAwait(false);
        AnsiConsole.MarkupLine(ok ? $"[green]Stopped {id.EscapeMarkup()}[/]" : $"[red]Stop failed: {id.EscapeMarkup()}[/]");
        return ok ? ExitCodes.Success : ExitCodes.OperationFailed;
    }

    public static async Task<int> LogsAsync(string url, InriverAuth auth, string id, int count, CancellationToken ct)
    {
        using var client = new InriverClient(url);
        var rc = await auth.ConnectAsync(client).ConfigureAwait(false);
        if (rc != ExitCodes.Success) return rc;
        try
        {
            var events = new ExtensionsService(client).Events(id, count);
            foreach (var e in events)
                AnsiConsole.MarkupLine($"[grey]{e.Utc:u}[/] {(e.IsError ? "[red]ERR[/]" : "[blue]ok[/] ")} {e.Message.EscapeMarkup()}");
            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Logs failed:[/] {ex.Message.EscapeMarkup()}");
            return ExitCodes.OperationFailed;
        }
    }

    public static async Task<int> SetSettingAsync(string url, InriverAuth auth, string id, string key, string value, CancellationToken ct)
    {
        using var client = new InriverClient(url);
        var rc = await auth.ConnectAsync(client).ConfigureAwait(false);
        if (rc != ExitCodes.Success) return rc;
        var ok = await new ExtensionsService(client).SetSettingAsync(id, key, value, ct).ConfigureAwait(false);
        AnsiConsole.MarkupLine(ok ? "[green]ok[/]" : "[red]failed[/]");
        return ok ? ExitCodes.Success : ExitCodes.OperationFailed;
    }
}
