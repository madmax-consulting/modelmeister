using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Model.Primitives;
using ModelMeister.Ui.Models;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// CVL workbench: capture CVLs from the connected env and export to Excel for editing.
/// </summary>
public partial class CvlWorkbenchViewModel : FeaturePageViewModel
{
    readonly MainWindowViewModel _main;
    readonly Shell _shell;
    readonly IAppLog _log;

    /// <inheritdoc/>
    public override bool SupportsCompare => true;
    /// <inheritdoc/>
    public override BackupScope BackupScope => BackupScope.Cvls;
    /// <inheritdoc/>
    public override ExcelCapability Excel => ExcelCapability.ExportImport;
    /// <inheritdoc/>
    public override bool HasExcelTemplate => true;

    /// <inheritdoc/>
    public override Task BackupAsync()
    {
        _log.Toast(LogLevel.Info, "Backup",
            "CVL-scoped backup ships with the Backup hub migration. For now use Full snapshot from the Dashboard.");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public override Task ExportExcelAsync() => ExportFullWorkbookAsync();

    /// <inheritdoc/>
    public override Task ExportTemplateAsync() => DownloadTemplateAsync();

    /// <inheritdoc/>
    public override async Task ImportExcelAsync()
    {
        if (!_main.IsConnected) { _log.Toast(LogLevel.Warn, "Import CVLs", "Connect first."); return; }
        var vm = await DialogHost.ImportWorkbookAsync(
            "Import CVLs from workbook",
            "Create missing CVLs and sync their values in the connected environment from an edited cvls.xlsx.",
            "cvls.xlsx", _main.Settings.Current.RecentWorkbookPaths).ConfigureAwait(true);
        if (vm is null) return;
        WorkbookPath = vm.WorkbookPath;

        // Always dry-run first; only run the real import if the user approves in the preview, and (when
        // the import would remove values) clears a second destructive confirmation.
        DryRun = true;
        _proceedWithImport = false;
        _pendingRemovals = 0;
        _pendingRemovalCvls.Clear();
        await ProvisionAsync().ConfigureAwait(true);
        if (!_proceedWithImport) return;

        if (_pendingRemovals > 0)
        {
            var ok = await DialogHost.ConfirmBulkAsync(
                "Apply CVL import", "Remove", "value", _pendingRemovalCvls,
                _main.ConnectedEnv?.Name, _main.ConnectedEnv?.Stage ?? Models.EnvironmentStage.Unspecified,
                destructive: true).ConfigureAwait(true);
            if (!ok) { Status = "Import cancelled."; return; }
        }

        DryRun = false;
        await ProvisionAsync().ConfigureAwait(true);
        RememberWorkbook(_main.Settings, WorkbookPath);
    }

    /// <summary>Set by <see cref="ProvisionAsync"/> when the dry-run preview's "Continue with import"
    /// was clicked — tells <see cref="ImportExcelAsync"/> to run the real import.</summary>
    private bool _proceedWithImport;

    /// <summary>Count of CVL values the dry-run found would be removed (live values absent from the
    /// workbook). Drives the extra destructive confirmation before the real import.</summary>
    private int _pendingRemovals;

    /// <summary>"CvlId (N value(s))" labels for each CVL with removals — shown in the destructive confirm.</summary>
    private readonly List<string> _pendingRemovalCvls = [];

    public ObservableCollection<CvlRow> Cvls { get; } = [];

    /// <summary>Checkbox multi-selection over <see cref="Cvls"/> (header select-all + bulk delete).</summary>
    public RowSelectionModel Selection { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportFullWorkbookCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddCvlCommand))]
    private bool _busy;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _dryRun = true;
    [ObservableProperty] private string? _workbookPath;

    public CvlWorkbenchViewModel(MainWindowViewModel main, Shell shell, IAppLog log)
    {
        _main = main;
        _shell = shell;
        _log = log;
        Selection = new RowSelectionModel(Cvls);
        _main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsConnected))
            {
                // Different env → CVL set is different. Flag so EnsureLoadedAsync re-fetches.
                MarkDataDirty();
                if (_main.IsConnected) _ = EnsureLoadedAsync();
                ExportFullWorkbookCommand.NotifyCanExecuteChanged();
                AddCvlCommand.NotifyCanExecuteChanged();
            }
        };
    }

    private bool CanMutate() => !Busy && _main.IsConnected;

    private bool CanExportWorkbook() => !Busy && _main.IsConnected;

    /// <inheritdoc/>
    public override async Task RefreshAsync()
    {
        if (!_main.IsConnected) { Status = "Connect first."; return; }
        Busy = true;
        try
        {
            var snap = await _shell.CaptureSnapshotAsync().ConfigureAwait(true);
            Cvls.Clear();
            foreach (var c in snap.Cvls.OrderBy(c => c.Id, StringComparer.OrdinalIgnoreCase))
                Cvls.Add(new CvlRow(c.Id, c.DataType, c.Values.Count, c.ParentId ?? "", c.CustomValueList));
            Status = $"{Cvls.Count} CVLs · {Cvls.Sum(r => r.ValueCount)} values";
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; _log.Error("Cvl", ex.Message, ex); }
        finally { Busy = false; }
    }

    [RelayCommand] private Task CopyCvlId(CvlRow? row) => ClipboardHelpers.CopyAsync(row?.Id);

    // ----- CRUD: create / edit (definition + values) / delete -----

    /// <summary>Open the CVL editor blank; on Save create the CVL and any value rows.</summary>
    [RelayCommand(CanExecute = nameof(CanMutate))]
    private async Task AddCvlAsync()
    {
        if (!_main.IsConnected) { Status = "Connect first."; return; }
        var vm = await DialogHost.CvlEditorAsync(
            isEdit: false, id: "", dataType: CvlDataType.String, parentId: null, customValueList: false,
            values: [], availableCvlIds: Cvls.Select(c => c.Id).ToList()).ConfigureAwait(true);
        if (vm is null) return;
        if (Cvls.Any(c => string.Equals(c.Id, vm.Id.Trim(), StringComparison.OrdinalIgnoreCase)))
        { Status = $"CVL '{vm.Id.Trim()}' already exists — edit it instead."; return; }
        await ApplyCvlEditAsync(vm, isEdit: false).ConfigureAwait(true);
    }

    /// <summary>Open the CVL editor for <paramref name="row"/> (loads its values live); on Save apply the diff.</summary>
    [RelayCommand]
    private async Task EditCvlAsync(CvlRow? row)
    {
        if (row is null || !_main.IsConnected) return;
        Busy = true;
        Status = $"Loading values for '{row.Id}'…";
        IReadOnlyList<LiveCvlValue> values;
        try { values = await _shell.ListCvlValuesAsync(row.Id).ConfigureAwait(true); }
        catch (Exception ex) { Status = "Failed to load values: " + ex.Message; _log.Error("Cvl", ex.Message, ex); return; }
        finally { Busy = false; }

        var vm = await DialogHost.CvlEditorAsync(
            isEdit: true, id: row.Id, dataType: row.DataTypeEnum,
            parentId: string.IsNullOrEmpty(row.ParentId) ? null : row.ParentId,
            customValueList: row.CustomValueList,
            values: values, availableCvlIds: Cvls.Select(c => c.Id).ToList()).ConfigureAwait(true);
        if (vm is null) return;
        await ApplyCvlEditAsync(vm, isEdit: true).ConfigureAwait(true);
    }

    private async Task ApplyCvlEditAsync(CvlEditorViewModel vm, bool isEdit)
    {
        var id = vm.Id.Trim();
        Busy = true;
        Status = isEdit ? $"Saving CVL '{id}'…" : $"Creating CVL '{id}'…";
        try
        {
            if (isEdit)
                await _shell.UpdateCvlAsync(id, vm.DataType, vm.ResolvedParentId, vm.CustomValueList).ConfigureAwait(true);
            else
                await _shell.AddCvlAsync(id, vm.DataType, vm.ResolvedParentId, vm.CustomValueList).ConfigureAwait(true);

            // Upsert every value row, then delete any value whose key was removed in the editor.
            var desiredKeys = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var v in vm.Values)
            {
                desiredKeys.Add(v.Key);
                await _shell.UpsertCvlValueAsync(id, new LiveCvlValue
                {
                    Id = 0,
                    CvlId = id,
                    Key = v.Key.Trim(),
                    Value = new LocaleString(v.Value ?? ""),
                    ParentKey = string.IsNullOrEmpty(v.ParentKey) ? null : v.ParentKey,
                    Index = v.Index,
                    Deactivated = v.Deactivated,
                }).ConfigureAwait(true);
            }
            foreach (var key in vm.OriginalKeys)
                if (!desiredKeys.Contains(key))
                    await _shell.DeleteCvlValueAsync(id, key).ConfigureAwait(true);

            _log.Success("Cvl", $"{(isEdit ? "Saved" : "Created")} CVL '{id}' ({vm.Values.Count} values).");
            Status = $"{(isEdit ? "Saved" : "Created")} CVL '{id}'.";
            MarkDataDirty();
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; _log.Error("Cvl", ex.Message, ex); }
        finally { Busy = false; }
    }

    /// <summary>Delete a single CVL (and its values) after a confirmation prompt.</summary>
    [RelayCommand]
    private async Task DeleteCvlAsync(CvlRow? row)
    {
        if (row is null || !_main.IsConnected) return;
        await ConfirmAndDeleteCvlsAsync(new[] { row.Id }).ConfigureAwait(true);
    }

    /// <summary>Delete every checked CVL after a single itemized confirmation prompt.</summary>
    [RelayCommand]
    private async Task DeleteSelectedCvlsAsync()
    {
        var ids = Selection.SelectedOf<CvlRow>().Select(c => c.Id).ToList();
        if (ids.Count == 0) { Status = "Select at least one CVL."; return; }
        await ConfirmAndDeleteCvlsAsync(ids).ConfigureAwait(true);
    }

    private async Task ConfirmAndDeleteCvlsAsync(IReadOnlyList<string> ids)
    {
        if (!_main.IsConnected) { Status = "Connect first."; return; }
        var ok = await DialogHost.ConfirmBulkAsync("Delete CVLs", "Delete", "CVL", ids,
            _main.ConnectedEnv?.Name, _main.ConnectedEnv?.Stage ?? Models.EnvironmentStage.Unspecified).ConfigureAwait(true);
        if (!ok) return;
        await DeleteCvlsAsync(ids).ConfigureAwait(true);
    }

    private async Task DeleteCvlsAsync(IReadOnlyList<string> ids)
    {
        if (!_main.IsConnected) { Status = "Connect first."; return; }
        Busy = true;
        int deleted = 0, errors = 0;
        try
        {
            await RunBulkAsync(ids,
                async id =>
                {
                    try { await _shell.DeleteCvlAsync(id).ConfigureAwait(false); deleted++; _log.Success("Cvl", $"Deleted CVL '{id}'."); }
                    catch (Exception ex) { errors++; _log.Warn("Cvl", $"Delete '{id}' failed: {ex.Message}"); }
                },
                (i, total, id) => Status = $"Deleting CVL {i} / {total} ('{id}')…").ConfigureAwait(true);
            Status = errors == 0 ? $"Deleted {deleted} CVL(s)." : $"Deleted {deleted}, {errors} failed.";
            MarkDataDirty();
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; _log.Error("Cvl", ex.Message, ex); }
        finally { Busy = false; }
    }

    /// <summary>Download the current CVL set (definitions + values) as an xlsx workbook.</summary>
    [RelayCommand(CanExecute = nameof(CanExportWorkbook))]
    public async Task ExportFullWorkbookAsync()
    {
        if (!_main.IsConnected) { Status = "Connect first."; return; }
        var path = await FilePickerHelpers.PickSaveAsync("Save CVL workbook", "cvls.xlsx", "xlsx").ConfigureAwait(true);
        if (path is null) return;
        Busy = true;
        try
        {
            var snap = await _shell.CaptureSnapshotAsync().ConfigureAwait(true);
            await _shell.SaveCvlValuesAsExcelAsync(snap, path).ConfigureAwait(true);
            Status = $"Wrote {Path.GetFileName(path)}";
            _log.Success("Cvl", $"Exported CVL workbook: {path}");
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; _log.Error("Cvl", ex.Message, ex); }
        finally { Busy = false; }
    }

    /// <summary>Download a minimal one-CVL example workbook callers can edit + re-import.</summary>
    [RelayCommand(CanExecute = nameof(CanExportWorkbook))]
    public async Task DownloadTemplateAsync()
    {
        var path = await FilePickerHelpers.PickSaveAsync("Save CVL workbook", "cvls-template.xlsx", "xlsx").ConfigureAwait(true);
        if (path is null) return;
        Busy = true;
        try
        {
            await _shell.SaveCvlTemplateAsync(path).ConfigureAwait(true);
            Status = $"Wrote {Path.GetFileName(path)}";
            _log.Success("Cvl", $"Exported CVL template: {path}");
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; _log.Error("Cvl", ex.Message, ex); }
        finally { Busy = false; }
    }

    /// <summary>
    /// Dry-run or apply a CVL workbook import. Mirrors the Roles/Users flow: a dry-run builds a
    /// per-CVL preview (creates + value +/~/- counts) and is shown via the shared
    /// <see cref="ProvisionResultViewModel"/>; the real run creates missing CVL definitions, upserts
    /// every workbook value, and deletes values the workbook dropped — using the same Shell primitives
    /// the in-place CVL editor uses.
    /// </summary>
    public async Task ProvisionAsync()
    {
        if (!_main.IsConnected) { Status = "Connect to an environment first."; return; }
        if (string.IsNullOrEmpty(WorkbookPath) || !File.Exists(WorkbookPath)) { Status = "Pick a workbook."; return; }

        Busy = true;
        try
        {
            var source = await _shell.LoadCvlImportSourceAsync(WorkbookPath).ConfigureAwait(true);
            var live = await _shell.CaptureSnapshotAsync().ConfigureAwait(true);
            var liveById = live.Cvls.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);

            int created = 0, updated = 0, errors = 0;
            if (DryRun) { _pendingRemovals = 0; _pendingRemovalCvls.Clear(); }
            var resultRows = new List<ProvisionResultRow>();

            // Snapshot the work list so progress reporting has a stable total.
            var sourceCvls = source.Cvls.ToList();
            for (var i = 0; i < sourceCvls.Count; i++)
            {
                var sc = sourceCvls[i];
                var exists = liveById.TryGetValue(sc.Id, out var lc);
                var liveKeys = exists
                    ? lc!.Values.ToDictionary(v => v.Key, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, LiveCvlValue>(StringComparer.OrdinalIgnoreCase);
                var srcKeys = new HashSet<string>(sc.Values.Select(v => v.Key), StringComparer.OrdinalIgnoreCase);

                var adds = sc.Values.Count(v => !liveKeys.ContainsKey(v.Key));
                var updates = sc.Values.Count(v => liveKeys.TryGetValue(v.Key, out var lv) && !ValueEquivalent(v, lv));
                var removeKeys = exists ? liveKeys.Keys.Where(k => !srcKeys.Contains(k)).ToList() : new List<string>();

                if (DryRun)
                {
                    if (removeKeys.Count > 0) { _pendingRemovals += removeKeys.Count; _pendingRemovalCvls.Add($"{sc.Id} ({removeKeys.Count} value(s))"); }
                    if (!exists)
                        resultRows.Add(new ProvisionResultRow(sc.Id, "would-create", $"+{sc.Values.Count} values"));
                    else if (adds + updates + removeKeys.Count == 0)
                        resultRows.Add(new ProvisionResultRow(sc.Id, "unchanged", "no changes"));
                    else
                        resultRows.Add(new ProvisionResultRow(sc.Id, "would-update", $"+{adds} ~{updates} -{removeKeys.Count}"));
                    continue;
                }

                Status = $"Importing CVL {i + 1} / {sourceCvls.Count} ('{sc.Id}')…";
                try
                {
                    if (!exists)
                        await _shell.AddCvlAsync(sc.Id, sc.DataType, sc.ParentId, sc.CustomValueList).ConfigureAwait(true);

                    foreach (var v in sc.Values)
                        await _shell.UpsertCvlValueAsync(sc.Id, new LiveCvlValue
                        {
                            Id = 0,
                            CvlId = sc.Id,
                            Key = v.Key.Trim(),
                            Value = v.Value,
                            ParentKey = string.IsNullOrEmpty(v.ParentKey) ? null : v.ParentKey,
                            Index = v.Index,
                            Deactivated = v.Deactivated,
                        }).ConfigureAwait(true);

                    foreach (var key in removeKeys)
                        await _shell.DeleteCvlValueAsync(sc.Id, key).ConfigureAwait(true);

                    if (exists) updated++; else created++;
                    resultRows.Add(new ProvisionResultRow(sc.Id, exists ? "updated" : "created", $"+{adds} ~{updates} -{removeKeys.Count}"));
                }
                catch (Exception ex)
                {
                    errors++;
                    resultRows.Add(new ProvisionResultRow(sc.Id, "error", ex.Message));
                    _log.Warn("Cvl", $"{sc.Id}: {ex.Message}");
                }
            }

            var resultVm = new ProvisionResultViewModel(
                dryRun: DryRun,
                created: DryRun ? sourceCvls.Count(c => !liveById.ContainsKey(c.Id)) : created,
                updated: DryRun ? sourceCvls.Count(c => liveById.ContainsKey(c.Id)) : updated,
                errors: errors,
                warnings: 0,
                rows: resultRows,
                importEyebrow: "CVL IMPORT",
                keyColumnHeader: "CVL",
                itemNoun: "CVLs");
            var proceed = await DialogHost.ShowProvisionResultAsync(resultVm).ConfigureAwait(true);
            _proceedWithImport = DryRun && proceed;

            Status = DryRun
                ? $"Dry run complete · {sourceCvls.Count} CVLs would be processed."
                : $"Imported · created {created}, updated {updated}, errors {errors}";
            if (!DryRun) MarkDataDirty();
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; _log.Error("Cvl", ex.Message, ex); }
        finally { Busy = false; }
    }

    /// <summary>Cheap value-equality used only to count "~updates" in the dry-run preview: compares the
    /// default localised text plus index/parent/deactivated. Authoritative apply is the upsert itself.</summary>
    private static bool ValueEquivalent(LiveCvlValue a, LiveCvlValue b) =>
        string.Equals(a.Value?.DefaultValue ?? "", b.Value?.DefaultValue ?? "", StringComparison.Ordinal)
        && a.Index == b.Index
        && string.Equals(a.ParentKey ?? "", b.ParentKey ?? "", StringComparison.OrdinalIgnoreCase)
        && a.Deactivated == b.Deactivated;
}

/// <summary>Selectable grid row for one CVL in the workbench.</summary>
public sealed partial class CvlRow : SelectableRow
{
    public CvlRow(string id, CvlDataType dataType, int valueCount, string parentId, bool customValueList)
    {
        Id = id;
        DataTypeEnum = dataType;
        ValueCount = valueCount;
        ParentId = parentId;
        CustomValueList = customValueList;
    }

    public string Id { get; }
    /// <summary>The typed datatype (used to seed the editor); <see cref="DataType"/> is its display string.</summary>
    public CvlDataType DataTypeEnum { get; }
    public string DataType => DataTypeEnum.ToString();
    public int ValueCount { get; }
    public string ParentId { get; }
    public bool CustomValueList { get; }
}
