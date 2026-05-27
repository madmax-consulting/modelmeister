using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ModelMeister.Excel;
using ModelMeister.Inriver.WorkAreas;
using ModelMeister.Ui.ViewModels;

namespace ModelMeister.Ui.Services.Import.Plans;

/// <summary>Imports shared work-area folders (matched by tree path). Verify builds a stateful reconcile
/// session (parents before children); rows are applied in that order so a child create resolves its
/// freshly-created parent. Existing folders not in the workbook are left untouched (no deletes).</summary>
public sealed class WorkAreasImportPlan : ImportPlanBase
{
    private WorkAreaReconcileSession? _session;

    public WorkAreasImportPlan(MainWindowViewModel main, Shell shell, IAppLog log) : base(main, shell, log) { }

    public override ImportPlanMetadata Metadata { get; } = new(
        Eyebrow: "WORK AREAS IMPORT",
        Title: "Import work-area folders",
        Subtitle: "Create or update shared folders (matched by path) in the connected environment from an edited workareas.xlsx. Existing folders not in the workbook are left untouched.",
        ItemNoun: "folders",
        KeyColumnHeader: "Path",
        SuggestedFileName: "workareas.xlsx",
        BackupScope: BackupScope.WorkAreas);

    public override Task<VerifyResult> LoadAndVerifyAsync(string workbookPath, CancellationToken ct)
    {
        LastWorkbookPath = workbookPath;
        var folders = WorkAreaWorkbook.Load(workbookPath);
        _session = Shell.PlanWorkAreas(folders, allowDeletes: false);

        // Keep the planner's order (parents-before-children) so a child create resolves its parent.
        var rows = _session.Actions.Select(a => new ImportRowViewModel
        {
            Key = a.Path,
            Preview = a.IsSyndication ? "syndication" : a.IsQuery ? "query" : "folder",
            PlanKind = a.Kind == WorkAreaActionKind.Create ? RowPlanKind.WillCreate : RowPlanKind.WillUpdate,
            Payload = a,
        }).ToList();

        return Task.FromResult(Summarize(rows));
    }

    public override async Task<string?> BackupAsync(CancellationToken ct)
        => await Main.Backups.CaptureWorkAreasAsync(ct: ct).ConfigureAwait(false);

    public override async Task<RowOutcome> ApplyRowAsync(ImportRowViewModel row, CancellationToken ct)
    {
        var action = (WorkAreaAction)row.Payload;
        try
        {
            await _session!.ExecuteAsync(action, ct).ConfigureAwait(false);
            return new RowOutcome(
                action.Kind == WorkAreaActionKind.Create ? RowRunState.Created : RowRunState.Updated,
                row.Preview);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return new RowOutcome(RowRunState.Failed, "", ex.Message); }
    }
}
