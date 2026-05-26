using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Ui.Models;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// Single-env CRUD editor for the server-settings dictionary on the connected env. Lists every
/// key, lets the user edit values inline (dirty rows show a Save button), add new keys, and delete
/// rows. The two-env diff/promote workflow lives on the sibling Compare sub-page
/// (<see cref="ServerSettingsViewModel"/>).
/// </summary>
public partial class ServerSettingsManageViewModel : FeaturePageViewModel
{
    public override bool SupportsCompare => false;
    public override BackupScope BackupScope => BackupScope.ServerSettings;
    public override ExcelCapability Excel => ExcelCapability.ExportImport;

    private readonly MainWindowViewModel _main;
    private readonly Shell _shell;
    private readonly IAppLog _log;

    private List<ServerSettingEditRow> _allRows = new();

    public ObservableCollection<ServerSettingEditRow> Rows { get; } = [];

    /// <summary>Checkbox multi-selection over <see cref="Rows"/> (header select-all + bulk delete).</summary>
    public RowSelectionModel Selection { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(RevertRowCommand))]
    private bool _busy;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _filterText = "";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private string _newKey = "";
    [ObservableProperty] private string _newValue = "";

    public ServerSettingsManageViewModel(MainWindowViewModel main, Shell shell, IAppLog log)
    {
        _main = main;
        _shell = shell;
        _log = log;
        Selection = new RowSelectionModel(Rows);
        _main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsConnected))
            {
                // Connection change invalidates the cache (different env's settings).
                MarkDataDirty();
                if (_main.IsConnected) _ = EnsureLoadedAsync();
                AddCommand.NotifyCanExecuteChanged();
                SaveCommand.NotifyCanExecuteChanged();
                DeleteCommand.NotifyCanExecuteChanged();
            }
        };
        if (_main.IsConnected) _ = EnsureLoadedAsync();
        else Status = "Connect to an environment first.";
    }

    private bool CanAdd() => !Busy && _main.IsConnected;

    // Note: not gated on row.IsDirty. The Save button is already hidden via IsVisible when the row
    // is clean, and the row's PropertyChanged doesn't notify SaveCommand — gating on IsDirty here
    // leaves the button visible-but-disabled after edits (no NotifyCanExecuteChanged trigger).
    private bool CanSave(ServerSettingEditRow? row) => !Busy && _main.IsConnected && row is not null;

    private bool CanDelete(ServerSettingEditRow? row) => !Busy && _main.IsConnected && row is not null;

    public override async Task RefreshAsync()
    {
        if (!_main.IsConnected) { Status = "Connect to an environment first."; return; }
        Busy = true;
        Status = "Loading server settings…";
        try
        {
            var dict = await _shell.ListServerSettingsAsync().ConfigureAwait(true);
            _allRows = dict
                .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
                .Select(kvp => new ServerSettingEditRow(kvp.Key, kvp.Value))
                .ToList();
            RebuildVisible();
            Status = $"{_allRows.Count} settings on '{_main.ConnectedEnv?.Name ?? "(env)"}'.";
        }
        catch (Exception ex)
        {
            Status = "Load failed: " + ex.Message;
            _log.Error("ServerSettings", ex.Message, ex);
        }
        finally { Busy = false; }
    }

    partial void OnFilterTextChanged(string value) => RebuildVisible();

    private void RebuildVisible()
    {
        Rows.Clear();
        var filter = FilterText?.Trim() ?? "";
        foreach (var row in _allRows)
        {
            if (!string.IsNullOrEmpty(filter)
                && row.Key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0
                && (row.Value?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) ?? -1) < 0)
                continue;
            Rows.Add(row);
        }
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    public async Task AddAsync()
    {
        if (!_main.IsConnected) { Status = "Connect first."; return; }

        // Modal dialog (Create / Abort) replaces the previous inline form at the top of the page.
        var vm = await DialogHost.AddServerSettingAsync().ConfigureAwait(true);
        if (vm is null) return;
        var key = vm.Key.Trim();
        if (_allRows.Any(r => string.Equals(r.Key, key, StringComparison.Ordinal)))
        {
            Status = $"Key '{key}' already exists — edit the existing row.";
            return;
        }

        Busy = true;
        Status = $"Creating '{key}'…";
        try
        {
            var ok = await _shell.SetServerSettingAsync(key, vm.Value ?? "").ConfigureAwait(true);
            if (!ok) { Status = $"Create '{key}' failed."; return; }

            _allRows.Add(new ServerSettingEditRow(key, vm.Value ?? ""));
            _allRows = _allRows.OrderBy(r => r.Key, StringComparer.Ordinal).ToList();
            RebuildVisible();
            MarkDataDirty(); // server-side may have applied side effects; next nav back re-fetches
            _log.Success("ServerSettings", $"Created '{key}'.");
            Status = $"Created '{key}'.";
        }
        catch (Exception ex)
        {
            Status = "Create failed: " + ex.Message;
            _log.Error("ServerSettings", ex.Message, ex);
        }
        finally { Busy = false; }
    }

    /// <summary>Open the edit dialog pre-populated with <paramref name="row"/>'s value; save on Confirm.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    public async Task EditAsync(ServerSettingEditRow? row)
    {
        if (row is null || !_main.IsConnected) return;
        var vm = await DialogHost.AddServerSettingAsync(row.Key, row.Value, isEdit: true).ConfigureAwait(true);
        if (vm is null) return;

        Busy = true;
        Status = $"Saving '{row.Key}'…";
        try
        {
            var ok = await _shell.SetServerSettingAsync(row.Key, vm.Value ?? "").ConfigureAwait(true);
            if (!ok) { Status = $"Save '{row.Key}' failed."; return; }
            row.Value = vm.Value;
            row.CommitValue();
            MarkDataDirty();
            _log.Success("ServerSettings", $"Updated '{row.Key}'.");
            Status = $"Saved '{row.Key}'.";
        }
        catch (Exception ex)
        {
            Status = "Save failed: " + ex.Message;
            _log.Error("ServerSettings", ex.Message, ex);
        }
        finally { Busy = false; }
    }

    /// <summary>Delete <paramref name="row"/> after a confirmation prompt.</summary>
    [RelayCommand(CanExecute = nameof(CanDelete))]
    public async Task ConfirmAndDeleteAsync(ServerSettingEditRow? row)
    {
        if (row is null || !_main.IsConnected) return;
        var ok = await DialogHost.ConfirmBulkAsync(
            "Delete setting", "Delete", "setting", new[] { row.Key },
            _main.ConnectedEnv?.Name, _main.ConnectedEnv?.Stage ?? Models.EnvironmentStage.Unspecified).ConfigureAwait(true);
        if (!ok) return;
        await DeleteAsync(row).ConfigureAwait(true);
    }

    /// <summary>Delete every checked setting after a single itemized confirmation prompt.</summary>
    [RelayCommand]
    public async Task DeleteSelectedAsync()
    {
        if (!_main.IsConnected) { Status = "Connect first."; return; }
        var rows = Selection.SelectedOf<ServerSettingEditRow>();
        if (rows.Count == 0) { Status = "Select at least one setting."; return; }
        var ok = await DialogHost.ConfirmBulkAsync(
            "Delete settings", "Delete", "setting", rows.Select(r => r.Key).ToList(),
            _main.ConnectedEnv?.Name, _main.ConnectedEnv?.Stage ?? Models.EnvironmentStage.Unspecified).ConfigureAwait(true);
        if (!ok) return;

        Busy = true;
        int deleted = 0, errors = 0;
        try
        {
            foreach (var row in rows)
            {
                Status = $"Deleting '{row.Key}'…";
                try
                {
                    var success = await _shell.DeleteServerSettingAsync(row.Key).ConfigureAwait(true);
                    if (!success) { errors++; _log.Warn("ServerSettings", $"Delete '{row.Key}' failed."); continue; }
                    _allRows.Remove(row);
                    Rows.Remove(row);
                    deleted++;
                    _log.Success("ServerSettings", $"Deleted '{row.Key}'.");
                }
                catch (Exception ex) { errors++; _log.Error("ServerSettings", $"Delete '{row.Key}' failed: {ex.Message}", ex); }
            }
            MarkDataDirty();
            Status = errors == 0 ? $"Deleted {deleted} setting(s)." : $"Deleted {deleted}, {errors} failed.";
        }
        finally { Busy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    public async Task SaveAsync(ServerSettingEditRow? row)
    {
        if (row is null || !row.IsDirty) return;
        if (!_main.IsConnected) { Status = "Connect first."; return; }

        Busy = true;
        Status = $"Saving '{row.Key}'…";
        try
        {
            var ok = await _shell.SetServerSettingAsync(row.Key, row.Value ?? "").ConfigureAwait(true);
            if (!ok) { Status = $"Save '{row.Key}' failed."; return; }
            row.CommitValue();
            _log.Success("ServerSettings", $"Updated '{row.Key}'.");
            Status = $"Saved '{row.Key}'.";
        }
        catch (Exception ex)
        {
            Status = "Save failed: " + ex.Message;
            _log.Error("ServerSettings", ex.Message, ex);
        }
        finally { Busy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    public async Task DeleteAsync(ServerSettingEditRow? row)
    {
        if (row is null) return;
        if (!_main.IsConnected) { Status = "Connect first."; return; }

        Busy = true;
        Status = $"Deleting '{row.Key}'…";
        try
        {
            var ok = await _shell.DeleteServerSettingAsync(row.Key).ConfigureAwait(true);
            if (!ok) { Status = $"Delete '{row.Key}' failed."; return; }

            _allRows.Remove(row);
            Rows.Remove(row);
            MarkDataDirty();
            _log.Success("ServerSettings", $"Deleted '{row.Key}'.");
            Status = $"Deleted '{row.Key}'.";
        }
        catch (Exception ex)
        {
            Status = "Delete failed: " + ex.Message;
            _log.Error("ServerSettings", ex.Message, ex);
        }
        finally { Busy = false; }
    }

    [RelayCommand]
    public void RevertRow(ServerSettingEditRow? row) => row?.Revert();

    [RelayCommand] private Task CopyKey(ServerSettingEditRow? row)   => ClipboardHelpers.CopyAsync(row?.Key);
    [RelayCommand] private Task CopyValue(ServerSettingEditRow? row) => ClipboardHelpers.CopyAsync(row?.Value);

    // ----- FeaturePage overrides -----

    public override async Task BackupAsync()
    {
        if (!_main.IsConnected) { _log.Toast(LogLevel.Warn, "Backup", "Connect first."); return; }
        try
        {
            var path = await _main.Backups.CaptureServerSettingsAsync().ConfigureAwait(true);
            _log.Success("Backup", $"Server settings backup saved → {path}");
            _log.Toast(LogLevel.Success, "Backup saved", System.IO.Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            _log.Error("Backup", $"Server settings backup failed: {ex.Message}", ex);
            _log.Toast(LogLevel.Error, "Backup failed", ex.Message);
        }
    }

    public override async Task ExportExcelAsync()
    {
        if (!_main.IsConnected) { _log.Toast(LogLevel.Warn, "Export", "Connect first."); return; }
        var path = await FilePickerHelpers.PickSaveAsync("Save server settings workbook", "serversettings.xlsx", "xlsx").ConfigureAwait(true);
        if (path is null) return;
        try
        {
            var dict = await _shell.ListServerSettingsAsync().ConfigureAwait(true);
            ModelMeister.Excel.ServerSettingsWorkbook.Save(dict, path);
            _log.Success("Export", $"Server settings exported → {path}");
            _log.Toast(LogLevel.Success, "Export saved", System.IO.Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            _log.Error("Export", ex.Message, ex);
            _log.Toast(LogLevel.Error, "Export failed", ex.Message);
        }
    }

    public override async Task ImportExcelAsync()
    {
        if (!_main.IsConnected) { _log.Toast(LogLevel.Warn, "Import", "Connect first."); return; }
        var vm = await DialogHost.ImportWorkbookAsync(
            "Import server settings from workbook",
            "Bulk-apply server settings to the connected environment from an edited workbook.",
            "serversettings.xlsx",
            _main.Settings.Current.RecentWorkbookPaths).ConfigureAwait(true);
        if (vm?.WorkbookPath is null) return;
        try
        {
            var dict = await Task.Run(() => ModelMeister.Excel.ServerSettingsWorkbook.Load(vm.WorkbookPath)).ConfigureAwait(true);

            // Verify + dry-run preview first, then require explicit approval before writing.
            var previewRows = dict.Select(kvp => new ProvisionResultRow(
                kvp.Key, "would-set", string.IsNullOrEmpty(kvp.Value) ? "(clear)" : kvp.Value)).ToList();
            var previewVm = new ProvisionResultViewModel(
                dryRun: true, created: dict.Count, updated: 0, errors: 0, warnings: 0, rows: previewRows,
                importEyebrow: "SERVER SETTINGS IMPORT", keyColumnHeader: "Key", itemNoun: "settings");
            if (!await DialogHost.ShowProvisionResultAsync(previewVm).ConfigureAwait(true)) return;

            var entries = dict.Select(kvp => new KeyValuePair<string, string?>(kvp.Key, kvp.Value));
            var result = await _shell.BulkApplyServerSettingsAsync(entries).ConfigureAwait(true);
            _log.Success("Import", $"Applied {result.Applied.Count} keys, {result.Failed.Count} failed.");
            _log.Toast(LogLevel.Success, "Import complete", $"{result.Applied.Count} keys applied");
            RememberWorkbook(_main.Settings, vm.WorkbookPath);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.Error("Import", ex.Message, ex);
            _log.Toast(LogLevel.Error, "Import failed", ex.Message);
        }
    }
}

/// <summary>One row in the server-settings CRUD grid. Tracks the on-server value vs the in-flight edit.</summary>
public partial class ServerSettingEditRow : SelectableRow
{
    public string Key { get; }

    [ObservableProperty] private string? _value;

    /// <summary>The value currently persisted on the server. Updated by <see cref="CommitValue"/>.</summary>
    public string? OriginalValue { get; private set; }

    public ServerSettingEditRow(string key, string? value)
    {
        Key = key;
        _value = value;
        OriginalValue = value;
    }

    public bool IsDirty => !string.Equals(Value, OriginalValue, StringComparison.Ordinal);

    partial void OnValueChanged(string? value) => OnPropertyChanged(nameof(IsDirty));

    public void CommitValue()
    {
        OriginalValue = Value;
        OnPropertyChanged(nameof(IsDirty));
    }

    public void Revert()
    {
        Value = OriginalValue;
    }
}
