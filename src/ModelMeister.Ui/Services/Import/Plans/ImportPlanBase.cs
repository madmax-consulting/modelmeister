using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ModelMeister.Ui.ViewModels;

namespace ModelMeister.Ui.Services.Import.Plans;

/// <summary>
/// Shared scaffolding for the per-feature <see cref="IImportPlan"/>s: holds the hub/shell/log, the
/// last workbook path, and a default (no-op) preconditions check. Subclasses implement load/verify,
/// the scoped backup, and apply-one-row.
/// </summary>
public abstract class ImportPlanBase : IImportPlan
{
    protected MainWindowViewModel Main { get; }
    protected Shell Shell { get; }
    protected IAppLog Log { get; }

    protected ImportPlanBase(MainWindowViewModel main, Shell shell, IAppLog log)
    {
        Main = main;
        Shell = shell;
        Log = log;
    }

    /// <inheritdoc/>
    public abstract ImportPlanMetadata Metadata { get; }

    /// <inheritdoc/>
    public string? LastWorkbookPath { get; protected set; }

    /// <inheritdoc/>
    public virtual string? CheckPreconditions() => null;

    /// <inheritdoc/>
    public abstract Task<VerifyResult> LoadAndVerifyAsync(string workbookPath, CancellationToken ct);

    /// <inheritdoc/>
    public abstract Task<string?> BackupAsync(CancellationToken ct);

    /// <inheritdoc/>
    public abstract Task<RowOutcome> ApplyRowAsync(ImportRowViewModel row, CancellationToken ct);

    /// <summary>Build a <see cref="VerifyResult"/> from the categorised rows (counts derived from
    /// <see cref="ImportRowViewModel.PlanKind"/>).</summary>
    protected static VerifyResult Summarize(
        IReadOnlyList<ImportRowViewModel> rows,
        string? destructiveTitle = null, string? destructiveVerb = null,
        string? destructiveNoun = null, IReadOnlyList<string>? destructiveItems = null)
    {
        int create = 0, update = 0, skip = 0, invalid = 0;
        foreach (var r in rows)
        {
            switch (r.PlanKind)
            {
                case RowPlanKind.WillCreate: create++; break;
                case RowPlanKind.WillUpdate: update++; break;
                case RowPlanKind.WillSkip: skip++; break;
                case RowPlanKind.Invalid: invalid++; break;
            }
        }
        return new VerifyResult(rows, create, update, skip, invalid,
            destructiveTitle, destructiveVerb, destructiveNoun, destructiveItems);
    }

    /// <summary>Map a provision result (created/updated + errors) to a row outcome.</summary>
    protected static RowOutcome FromProvision(bool created, IReadOnlyList<string> errors, string detail)
        => errors.Count > 0
            ? new RowOutcome(RowRunState.Failed, "", string.Join(" · ", errors))
            : new RowOutcome(created ? RowRunState.Created : RowRunState.Updated, detail);
}
