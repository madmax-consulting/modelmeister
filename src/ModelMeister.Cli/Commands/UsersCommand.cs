using Spectre.Console;
using ModelMeister.Excel;
using ModelMeister.Inriver;
using ModelMeister.Inriver.Users;
using ModelMeister.Rest;

namespace ModelMeister.Cli.Commands;

public static class UsersCommand
{
    public static async Task<int> ListAsync(string url, InriverAuth auth, CancellationToken ct)
    {
        using var client = new InriverClient(url);
        var rc = await auth.ConnectAsync(client).ConfigureAwait(false);
        if (rc != ExitCodes.Success) return rc;

        try
        {
            var users = new UserProvisioning(client).ListUsers();
            var table = new Table().AddColumns("Id", "Username", "Email", "Name", "Roles");
            foreach (var u in users)
                table.AddRow(
                    u.Id.ToString(),
                    u.Username,
                    u.Email ?? "",
                    $"{u.FirstName} {u.LastName}".Trim(),
                    string.Join(", ", u.Roles));
            AnsiConsole.Write(table);
            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]List users failed:[/] {ex.Message.EscapeMarkup()}");
            return ExitCodes.OperationFailed;
        }
    }

    public static async Task<int> ProvisionAsync(
        string url, InriverAuth auth, string? restBaseUrl, string? restApiKey,
        string xlsxPath, bool dryRun, CancellationToken ct)
    {
        if (!File.Exists(xlsxPath))
        {
            AnsiConsole.MarkupLine($"[red]Excel file not found: {xlsxPath.EscapeMarkup()}[/]");
            return ExitCodes.UsageError;
        }

        using var client = new InriverClient(url);
        var rc = await auth.ConnectAsync(client).ConfigureAwait(false);
        if (rc != ExitCodes.Success) return rc;

        InriverRestClient? rest = null;
        if (!string.IsNullOrEmpty(restBaseUrl) && !string.IsNullOrEmpty(restApiKey))
            rest = new InriverRestClient(restBaseUrl, restApiKey);

        try
        {
            var users = UsersWorkbook.Load(xlsxPath);
            var prov = new UserProvisioning(client, rest);

            var availableRoles = prov.ListRoleNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
            int created = 0, updated = 0, errors = 0;

            foreach (var u in users)
            {
                var missingRoles = u.Roles.Where(r => !availableRoles.Contains(r)).ToList();
                if (missingRoles.Count > 0)
                    AnsiConsole.MarkupLine($"  [yellow]warn:[/] {u.Username} references missing roles: {string.Join(", ", missingRoles).EscapeMarkup()}");

                if (dryRun)
                {
                    AnsiConsole.MarkupLine($"  [grey]dry: would provision[/] {u.Username.EscapeMarkup()} -> {string.Join(", ", u.Roles).EscapeMarkup()}");
                    continue;
                }
                var result = await prov.ProvisionAsync(new UserProvisioning.UserSpec(
                    u.Username, u.Email, u.FirstName, u.LastName, u.Company,
                    u.Roles, u.Language, u.GenerateApiKey), ct).ConfigureAwait(false);
                if (result.Created) { created++; AnsiConsole.MarkupLine($"  [green]+ {u.Username.EscapeMarkup()}[/]"); }
                else { updated++; AnsiConsole.MarkupLine($"  [blue]~ {u.Username.EscapeMarkup()}[/]"); }
                if (!string.IsNullOrEmpty(result.ApiKey))
                    AnsiConsole.MarkupLine($"     [grey]api key:[/] {result.ApiKey!.EscapeMarkup()}");
                foreach (var e in result.Errors)
                {
                    errors++;
                    AnsiConsole.MarkupLine($"     [red]err:[/] {e.EscapeMarkup()}");
                }
            }
            AnsiConsole.MarkupLine($"[green]Provisioning complete.[/] Created {created}, updated {updated}, errors {errors}.");
            return errors > 0 ? ExitCodes.OperationFailed : ExitCodes.Success;
        }
        finally
        {
            rest?.Dispose();
        }
    }

    public static async Task<int> ExportTemplateAsync(string url, InriverAuth auth, string xlsxPath, CancellationToken ct)
    {
        using var client = new InriverClient(url);
        var rc = await auth.ConnectAsync(client).ConfigureAwait(false);
        if (rc != ExitCodes.Success) return rc;

        try
        {
            var prov = new UserProvisioning(client);
            var roles = prov.ListRoleNames();
            var users = prov.ListUsers()
                .Select(u => new UsersWorkbook.UserRow
                {
                    Username = u.Username,
                    Email = u.Email ?? "",
                    FirstName = u.FirstName ?? "",
                    LastName = u.LastName ?? "",
                    Company = u.Company ?? "",
                    Roles = u.Roles.ToList(),
                    Language = "en",
                })
                .ToList();
            UsersWorkbook.Save(users, roles, xlsxPath);
            AnsiConsole.MarkupLine($"[green]Wrote {xlsxPath.EscapeMarkup()} ({users.Count} users, {roles.Count} roles)[/]");
            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Export failed:[/] {ex.Message.EscapeMarkup()}");
            return ExitCodes.OperationFailed;
        }
    }
}
