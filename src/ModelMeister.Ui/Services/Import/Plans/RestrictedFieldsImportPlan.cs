using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ModelMeister.Excel;
using ModelMeister.Inriver.Users;
using ModelMeister.Ui.ViewModels;

namespace ModelMeister.Ui.Services.Import.Plans;

/// <summary>Imports restricted-field permissions. Add-only (no update op): rows are validated up front,
/// deduped by natural key against the live set and within the file (existing → skip), and the rest are
/// created.</summary>
public sealed class RestrictedFieldsImportPlan : ImportPlanBase
{
    public RestrictedFieldsImportPlan(MainWindowViewModel main, Shell shell, IAppLog log) : base(main, shell, log) { }

    public override ImportPlanMetadata Metadata { get; } = new(
        Eyebrow: "RESTRICTED FIELDS IMPORT",
        Title: "Import restricted-field permissions",
        Subtitle: "Add restricted-field permissions in the connected environment from an edited restricted-fields.xlsx. There is no update operation, so rows that already exist are skipped.",
        ItemNoun: "restrictions",
        KeyColumnHeader: "Restriction",
        SuggestedFileName: "restricted-fields.xlsx",
        BackupScope: BackupScope.RestrictedFields);

    public override async Task<VerifyResult> LoadAndVerifyAsync(string workbookPath, CancellationToken ct)
    {
        LastWorkbookPath = workbookPath;
        var fileRows = RestrictedFieldsWorkbook.Load(workbookPath);
        var existingKeys = (await Shell.ListRestrictedFieldsAsync(ct).ConfigureAwait(false))
            .Select(r => r.NaturalKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rows = new List<ImportRowViewModel>();
        foreach (var row in fileRows)
        {
            var entity = RestrictedFieldProvisioning.NullIfEmpty(row.EntityTypeId);
            var field = RestrictedFieldProvisioning.NullIfEmpty(row.FieldTypeId);
            var category = RestrictedFieldProvisioning.NullIfEmpty(row.CategoryId);
            var normType = RestrictedFieldProvisioning.NormalizeRestrictionType(row.RestrictionType);

            // inriver requires a role, a valid restriction type, an entity type, and at least one of
            // field-type / category. Reject bad rows up front instead of letting the backend fail.
            var problem =
                string.IsNullOrWhiteSpace(row.RoleName) ? "Role name is required."
                : normType is null ? $"Restriction type '{row.RestrictionType}' is invalid — must be 'Readonly' or 'Hidden'."
                : string.IsNullOrWhiteSpace(entity) ? "Entity type is required."
                : (field is null && category is null) ? "At least one of Field type or Category is required."
                : null;

            if (problem is not null)
            {
                rows.Add(new ImportRowViewModel
                {
                    Key = $"{row.RoleName} · {row.RestrictionType}",
                    Preview = problem,
                    PlanKind = RowPlanKind.Invalid,
                    Reason = problem,
                    Payload = row,
                });
                continue;
            }

            var key = RestrictedFieldProvisioning.NaturalKey(row.RoleName, normType!, entity, field, category);
            if (existingKeys.Contains(key))
            {
                rows.Add(new ImportRowViewModel
                {
                    Key = key, Preview = $"role: {row.RoleName}", PlanKind = RowPlanKind.WillSkip,
                    Reason = "already exists", Payload = row,
                });
                continue;
            }

            existingKeys.Add(key); // dedupe within the file too
            rows.Add(new ImportRowViewModel
            {
                Key = key,
                Preview = $"role: {row.RoleName}",
                PlanKind = RowPlanKind.WillCreate,
                Payload = new RestrictedFieldProvisioning.RestrictedFieldSpec(row.RoleName, normType!, entity, field, category),
            });
        }

        return Summarize(rows);
    }

    public override async Task<string?> BackupAsync(CancellationToken ct)
        => await Main.Backups.CaptureRestrictedFieldsAsync(ct: ct).ConfigureAwait(false);

    public override async Task<RowOutcome> ApplyRowAsync(ImportRowViewModel row, CancellationToken ct)
    {
        var spec = (RestrictedFieldProvisioning.RestrictedFieldSpec)row.Payload;
        var result = await Shell.AddRestrictedFieldAsync(spec, ct).ConfigureAwait(false);
        return result.Errors.Count > 0
            ? new RowOutcome(RowRunState.Failed, "", string.Join(" · ", result.Errors))
            : new RowOutcome(RowRunState.Created, $"role: {spec.RoleName}");
    }
}
