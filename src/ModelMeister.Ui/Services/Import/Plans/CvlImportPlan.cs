using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Model.Primitives;
using ModelMeister.Ui.ViewModels;

namespace ModelMeister.Ui.Services.Import.Plans;

/// <summary>Imports CVLs (one row per CVL): creates missing definitions, upserts every workbook value,
/// and deletes values the workbook dropped. When the import would remove values, Verify reports them so
/// the workflow shows a destructive-removal confirmation before writing.</summary>
public sealed class CvlImportPlan : ImportPlanBase
{
    /// <summary>Everything needed to apply one CVL, precomputed at Verify so apply just executes.</summary>
    private sealed record Payload(
        string Id, CvlDataType DataType, string? ParentId, bool CustomValueList,
        IReadOnlyList<LiveCvlValue> Values, bool Exists, IReadOnlyList<string> RemoveKeys);

    public CvlImportPlan(MainWindowViewModel main, Shell shell, IAppLog log) : base(main, shell, log) { }

    public override ImportPlanMetadata Metadata { get; } = new(
        Eyebrow: "CVL IMPORT",
        Title: "Import CVLs from workbook",
        Subtitle: "Create missing CVLs and sync their values in the connected environment from an edited cvls.xlsx. Values present live but absent from the workbook are removed.",
        ItemNoun: "CVLs",
        KeyColumnHeader: "CVL",
        SuggestedFileName: "cvls.xlsx",
        BackupScope: BackupScope.Cvls);

    public override async Task<VerifyResult> LoadAndVerifyAsync(string workbookPath, CancellationToken ct)
    {
        LastWorkbookPath = workbookPath;
        var source = await Shell.LoadCvlImportSourceAsync(workbookPath, ct).ConfigureAwait(false);
        var live = await Shell.CaptureSnapshotAsync(ct).ConfigureAwait(false);
        var liveById = live.Cvls.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);

        var rows = new List<ImportRowViewModel>();
        var removalLabels = new List<string>();

        foreach (var sc in source.Cvls)
        {
            var exists = liveById.TryGetValue(sc.Id, out var lc);
            var liveKeys = exists
                ? lc!.Values.ToDictionary(v => v.Key, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, LiveCvlValue>(StringComparer.OrdinalIgnoreCase);
            var srcKeys = new HashSet<string>(sc.Values.Select(v => v.Key), StringComparer.OrdinalIgnoreCase);

            var adds = sc.Values.Count(v => !liveKeys.ContainsKey(v.Key));
            var updates = sc.Values.Count(v => liveKeys.TryGetValue(v.Key, out var lv) && !ValueEquivalent(v, lv));
            var removeKeys = exists ? liveKeys.Keys.Where(k => !srcKeys.Contains(k)).ToList() : new List<string>();
            if (removeKeys.Count > 0) removalLabels.Add($"{sc.Id} ({removeKeys.Count} value(s))");

            RowPlanKind kind;
            string preview;
            if (!exists) { kind = RowPlanKind.WillCreate; preview = $"+{sc.Values.Count} values"; }
            else if (adds + updates + removeKeys.Count == 0) { kind = RowPlanKind.WillSkip; preview = "no changes"; }
            else { kind = RowPlanKind.WillUpdate; preview = $"+{adds} ~{updates} -{removeKeys.Count}"; }

            rows.Add(new ImportRowViewModel
            {
                Key = sc.Id,
                Preview = preview,
                PlanKind = kind,
                Reason = kind == RowPlanKind.WillSkip ? "no changes" : null,
                Payload = new Payload(sc.Id, sc.DataType, sc.ParentId, sc.CustomValueList, sc.Values, exists, removeKeys),
            });
        }

        return Summarize(rows,
            destructiveTitle: removalLabels.Count > 0 ? "Apply CVL import" : null,
            destructiveVerb: removalLabels.Count > 0 ? "Remove" : null,
            destructiveNoun: removalLabels.Count > 0 ? "value" : null,
            destructiveItems: removalLabels.Count > 0 ? removalLabels : null);
    }

    public override async Task<string?> BackupAsync(CancellationToken ct)
        => await Main.Backups.CaptureCvlsAsync(ct: ct).ConfigureAwait(false);

    public override async Task<RowOutcome> ApplyRowAsync(ImportRowViewModel row, CancellationToken ct)
    {
        var p = (Payload)row.Payload;
        try
        {
            if (!p.Exists)
                await Shell.AddCvlAsync(p.Id, p.DataType, p.ParentId, p.CustomValueList, ct).ConfigureAwait(false);

            foreach (var v in p.Values)
                await Shell.UpsertCvlValueAsync(p.Id, new LiveCvlValue
                {
                    Id = 0,
                    CvlId = p.Id,
                    Key = v.Key.Trim(),
                    Value = v.Value,
                    ParentKey = string.IsNullOrEmpty(v.ParentKey) ? null : v.ParentKey,
                    Index = v.Index,
                    Deactivated = v.Deactivated,
                }, ct).ConfigureAwait(false);

            foreach (var key in p.RemoveKeys)
                await Shell.DeleteCvlValueAsync(p.Id, key, ct).ConfigureAwait(false);

            return new RowOutcome(p.Exists ? RowRunState.Updated : RowRunState.Created, row.Preview);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new RowOutcome(RowRunState.Failed, "", ex.Message);
        }
    }

    /// <summary>Cheap value-equality used to count "~updates" in Verify: compares the default localised
    /// text plus index/parent/deactivated. Authoritative apply is the upsert itself.</summary>
    private static bool ValueEquivalent(LiveCvlValue a, LiveCvlValue b) =>
        string.Equals(a.Value?.DefaultValue ?? "", b.Value?.DefaultValue ?? "", StringComparison.Ordinal)
        && a.Index == b.Index
        && string.Equals(a.ParentKey ?? "", b.ParentKey ?? "", StringComparison.OrdinalIgnoreCase)
        && a.Deactivated == b.Deactivated;
}
