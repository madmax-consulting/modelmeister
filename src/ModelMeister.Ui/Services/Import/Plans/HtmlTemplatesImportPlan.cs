using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ModelMeister.Excel;
using ModelMeister.Inriver.HtmlTemplates;
using ModelMeister.Ui.ViewModels;

namespace ModelMeister.Ui.Services.Import.Plans;

/// <summary>Imports HTML templates (matched by name + type). Verify diffs against the live env via the
/// service planner; each row is one create/update applied individually for live progress. Existing
/// templates not in the workbook are left untouched (no deletes).</summary>
public sealed class HtmlTemplatesImportPlan : ImportPlanBase
{
    public HtmlTemplatesImportPlan(MainWindowViewModel main, Shell shell, IAppLog log) : base(main, shell, log) { }

    public override ImportPlanMetadata Metadata { get; } = new(
        Eyebrow: "HTML TEMPLATES IMPORT",
        Title: "Import HTML templates",
        Subtitle: "Create or update print / ContentStore templates (matched by name + type) in the connected environment from an edited htmltemplates.xlsx. Existing templates not in the workbook are left untouched.",
        ItemNoun: "templates",
        KeyColumnHeader: "Template",
        SuggestedFileName: "htmltemplates.xlsx",
        BackupScope: BackupScope.HtmlTemplates);

    public override Task<VerifyResult> LoadAndVerifyAsync(string workbookPath, CancellationToken ct)
    {
        LastWorkbookPath = workbookPath;
        var templates = HtmlTemplateWorkbook.Load(workbookPath);
        var actions = Shell.PlanHtmlTemplates(templates, allowDeletes: false);

        var rows = actions.Select(a => new ImportRowViewModel
        {
            Key = a.Name,
            Preview = $"type: {a.TemplateType}",
            PlanKind = a.Kind switch
            {
                HtmlTemplateActionKind.Create => RowPlanKind.WillCreate,
                HtmlTemplateActionKind.Update => RowPlanKind.WillUpdate,
                _ => RowPlanKind.WillSkip, // Unchanged
            },
            Reason = a.Kind == HtmlTemplateActionKind.Unchanged ? "unchanged" : null,
            Payload = a,
        }).ToList();

        return Task.FromResult(Summarize(rows));
    }

    public override async Task<string?> BackupAsync(CancellationToken ct)
        => await Main.Backups.CaptureHtmlTemplatesAsync(ct: ct).ConfigureAwait(false);

    public override async Task<RowOutcome> ApplyRowAsync(ImportRowViewModel row, CancellationToken ct)
    {
        var action = (HtmlTemplateAction)row.Payload;
        try
        {
            await Shell.ExecuteHtmlTemplateActionAsync(action, ct).ConfigureAwait(false);
            return new RowOutcome(
                action.Kind == HtmlTemplateActionKind.Create ? RowRunState.Created : RowRunState.Updated,
                $"type: {action.TemplateType}");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return new RowOutcome(RowRunState.Failed, "", ex.Message); }
    }
}
