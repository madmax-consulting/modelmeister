using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ModelMeister.Excel;
using ModelMeister.Inriver.Users;
using ModelMeister.Ui.Models;
using ModelMeister.Ui.ViewModels;

namespace ModelMeister.Ui.Services.Import.Plans;

/// <summary>Imports users from a workbook: every row is provisioned (create or update by username =
/// email). Requires the connected env to expose a REST base URL + API key (the Remoting surface can't
/// create users) — gated up front in <see cref="CheckPreconditions"/>.</summary>
public sealed class UsersImportPlan : ImportPlanBase
{
    private EnvironmentEntry? _env;
    private EnvironmentSecret? _secret;

    public UsersImportPlan(MainWindowViewModel main, Shell shell, IAppLog log) : base(main, shell, log) { }

    public override ImportPlanMetadata Metadata { get; } = new(
        Eyebrow: "USERS IMPORT",
        Title: "Import users from workbook",
        Subtitle: "Provision (create / update) users in the connected environment from an edited users.xlsx. Each user is matched by email (the inriver username).",
        ItemNoun: "users",
        KeyColumnHeader: "Username",
        SuggestedFileName: "users.xlsx",
        BackupScope: BackupScope.Users);

    public override string? CheckPreconditions()
    {
        _env = Main.ConnectedEnv;
        _secret = _env is null ? null : Main.Vault.GetSecret(_env.Id);
        if (_env is null || string.IsNullOrWhiteSpace(_env.RestBaseUrl))
            return "Importing users requires a REST base URL on the connected environment.";
        if (_secret is null || string.IsNullOrWhiteSpace(_secret.RestApiKey))
            return "Importing users requires a REST API key on the connected environment.";
        return null;
    }

    public override async Task<VerifyResult> LoadAndVerifyAsync(string workbookPath, CancellationToken ct)
    {
        LastWorkbookPath = workbookPath;
        var users = UsersWorkbook.Load(workbookPath);
        var existing = (await Shell.ListUsersAsync(ct).ConfigureAwait(false))
            .Select(u => u.Username)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rows = users.Select(u =>
        {
            var spec = new UserProvisioning.UserSpec(
                u.Username, u.Email, u.FirstName, u.LastName, u.Roles, u.Language, u.GenerateApiKey);
            var kind = existing.Contains(u.Username) ? RowPlanKind.WillUpdate : RowPlanKind.WillCreate;
            return new ImportRowViewModel
            {
                Key = u.Username,
                Preview = u.Roles.Count == 0 ? "no roles" : $"roles: {string.Join(", ", u.Roles)}",
                PlanKind = kind,
                Payload = spec,
            };
        }).ToList();

        return Summarize(rows);
    }

    public override async Task<string?> BackupAsync(CancellationToken ct)
        => await Main.Backups.CaptureUsersAsync(ct: ct).ConfigureAwait(false);

    public override async Task<RowOutcome> ApplyRowAsync(ImportRowViewModel row, CancellationToken ct)
    {
        var spec = (UserProvisioning.UserSpec)row.Payload;
        var result = await Shell.ProvisionUserAsync(spec, _secret, _env!, ct).ConfigureAwait(false);
        return FromProvision(result.Created, result.Errors,
            spec.Roles.Count == 0 ? "no roles" : $"roles: {string.Join(", ", spec.Roles)}");
    }
}
