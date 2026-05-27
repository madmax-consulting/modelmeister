using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ModelMeister.Excel;
using ModelMeister.Inriver.Users;
using ModelMeister.Ui.ViewModels;

namespace ModelMeister.Ui.Services.Import.Plans;

/// <summary>Imports roles from a workbook: each row is provisioned (create or update by name). Pure
/// Remoting — no REST endpoint required.</summary>
public sealed class RolesImportPlan : ImportPlanBase
{
    public RolesImportPlan(MainWindowViewModel main, Shell shell, IAppLog log) : base(main, shell, log) { }

    public override ImportPlanMetadata Metadata { get; } = new(
        Eyebrow: "ROLES IMPORT",
        Title: "Import roles from workbook",
        Subtitle: "Provision (create / update) roles in the connected environment from an edited roles.xlsx. Each role is matched by name.",
        ItemNoun: "roles",
        KeyColumnHeader: "Role",
        SuggestedFileName: "roles.xlsx",
        BackupScope: BackupScope.Roles);

    public override async Task<VerifyResult> LoadAndVerifyAsync(string workbookPath, CancellationToken ct)
    {
        LastWorkbookPath = workbookPath;
        var roles = RolesWorkbook.Load(workbookPath);
        var existing = (await Shell.ListRolesAsync(ct).ConfigureAwait(false))
            .Select(r => r.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rows = roles.Select(r =>
        {
            var spec = new RoleProvisioning.RoleSpec(r.Name, r.Description, r.Permissions);
            var kind = existing.Contains(r.Name) ? RowPlanKind.WillUpdate : RowPlanKind.WillCreate;
            return new ImportRowViewModel
            {
                Key = r.Name,
                Preview = r.Permissions.Count == 0 ? "no permissions" : $"permissions: {string.Join(", ", r.Permissions)}",
                PlanKind = kind,
                Payload = spec,
            };
        }).ToList();

        return Summarize(rows);
    }

    public override async Task<string?> BackupAsync(CancellationToken ct)
        => await Main.Backups.CaptureRolesAsync(ct: ct).ConfigureAwait(false);

    public override async Task<RowOutcome> ApplyRowAsync(ImportRowViewModel row, CancellationToken ct)
    {
        var spec = (RoleProvisioning.RoleSpec)row.Payload;
        var result = await Shell.ProvisionRoleAsync(spec, ct).ConfigureAwait(false);
        return FromProvision(result.Created, result.Errors,
            spec.Permissions.Count == 0 ? "no permissions" : $"permissions: {string.Join(", ", spec.Permissions)}");
    }
}
