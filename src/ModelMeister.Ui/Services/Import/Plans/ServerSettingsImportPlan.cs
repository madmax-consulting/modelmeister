using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ModelMeister.Excel;
using ModelMeister.Ui.ViewModels;

namespace ModelMeister.Ui.Services.Import.Plans;

/// <summary>Imports server settings (a flat key/value sheet). Each key is set individually so the user
/// sees per-key progress; the value is diffed against the live setting to mark create / update / skip.</summary>
public sealed class ServerSettingsImportPlan : ImportPlanBase
{
    public ServerSettingsImportPlan(MainWindowViewModel main, Shell shell, IAppLog log) : base(main, shell, log) { }

    public override ImportPlanMetadata Metadata { get; } = new(
        Eyebrow: "SERVER SETTINGS IMPORT",
        Title: "Import server settings from workbook",
        Subtitle: "Set server settings in the connected environment from an edited serversettings.xlsx. Each key is matched by name; an empty value clears the setting.",
        ItemNoun: "settings",
        KeyColumnHeader: "Key",
        SuggestedFileName: "serversettings.xlsx",
        BackupScope: BackupScope.ServerSettings);

    public override async Task<VerifyResult> LoadAndVerifyAsync(string workbookPath, CancellationToken ct)
    {
        LastWorkbookPath = workbookPath;
        var dict = ServerSettingsWorkbook.Load(workbookPath);
        var live = await Shell.ListServerSettingsAsync(ct).ConfigureAwait(false);

        var rows = dict.Select(kvp =>
        {
            var value = kvp.Value ?? "";
            var preview = string.IsNullOrEmpty(value) ? "(clear)" : value;
            RowPlanKind kind;
            if (!live.TryGetValue(kvp.Key, out var current)) kind = RowPlanKind.WillCreate;
            else if (string.Equals(current, value, StringComparison.Ordinal)) kind = RowPlanKind.WillSkip;
            else kind = RowPlanKind.WillUpdate;

            return new ImportRowViewModel
            {
                Key = kvp.Key,
                Preview = preview,
                PlanKind = kind,
                Reason = kind == RowPlanKind.WillSkip ? "unchanged" : null,
                Payload = (kvp.Key, value),
            };
        }).ToList();

        return Summarize(rows);
    }

    public override async Task<string?> BackupAsync(CancellationToken ct)
        => await Main.Backups.CaptureServerSettingsAsync(ct: ct).ConfigureAwait(false);

    public override async Task<RowOutcome> ApplyRowAsync(ImportRowViewModel row, CancellationToken ct)
    {
        var (key, value) = ((string, string))row.Payload;
        var ok = await Shell.SetServerSettingAsync(key, value, ct).ConfigureAwait(false);
        var detail = string.IsNullOrEmpty(value) ? "(cleared)" : value;
        if (!ok) return new RowOutcome(RowRunState.Failed, "", "apply failed");
        return new RowOutcome(row.PlanKind == RowPlanKind.WillCreate ? RowRunState.Created : RowRunState.Updated, detail);
    }
}
