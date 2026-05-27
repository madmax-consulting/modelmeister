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
    public override async Task BackupAsync()
    {
        if (!_main.IsConnected) { _log.Toast(LogLevel.Warn, "Backup", "Connect first."); return; }
        try
        {
            var path = await _main.Backups.CaptureCvlsAsync().ConfigureAwait(true);
            _log.Success("Backup", $"CVLs backup saved → {path}");
            _log.Toast(LogLevel.Success, "CVLs backup saved", System.IO.Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            _log.Error("Backup", $"CVLs backup failed: {ex.Message}", ex);
            _log.Toast(LogLevel.Error, "Backup failed", ex.Message);
        }
    }

    /// <inheritdoc/>
    public override Task ExportExcelAsync() => ExportFullWorkbookAsync();

    /// <inheritdoc/>
    public override Task ExportTemplateAsync() => DownloadTemplateAsync();

    /// <inheritdoc/>
    public override async Task ImportExcelAsync()
    {
        if (!_main.IsConnected) { _log.Toast(LogLevel.Warn, "Import CVLs", "Connect first."); return; }
        var plan = new ModelMeister.Ui.Services.Import.Plans.CvlImportPlan(_main, _shell, _log);
        // CVL imports can remove values — pass a stage-aware gate so the destructive confirmation
        // between Verify and Import shows the connected env + prod banner.
        var gate = new ModelMeister.Ui.Services.Import.ImportConfirmGate(
            _main.ConnectedEnv?.Name, _main.ConnectedEnv?.TypeKey);
        var ran = await DialogHost.ShowImportWorkflowAsync(
            plan, _log, _main.Settings.Current.RecentWorkbookPaths, gate).ConfigureAwait(true);
        if (!ran) return;
        RememberWorkbook(_main.Settings, plan.LastWorkbookPath);
        MarkDataDirty();
        await RefreshAsync().ConfigureAwait(true);
    }

    public ObservableCollection<CvlRow> Cvls { get; } = [];

    /// <summary>Checkbox multi-selection over <see cref="Cvls"/> (header select-all + bulk delete).</summary>
    public RowSelectionModel Selection { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportFullWorkbookCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddCvlCommand))]
    private bool _busy;
    [ObservableProperty] private string _status = "";

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
            _main.ConnectedEnv?.Name, _main.ConnectedEnv?.TypeKey).ConfigureAwait(true);
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
