using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
        _main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsConnected))
            {
                if (_main.IsConnected) _ = RefreshAsync();
                AddCommand.NotifyCanExecuteChanged();
                SaveCommand.NotifyCanExecuteChanged();
                DeleteCommand.NotifyCanExecuteChanged();
            }
        };
        if (_main.IsConnected) _ = RefreshAsync();
        else Status = "Connect to an environment first.";
    }

    private bool CanAdd() =>
        !Busy && _main.IsConnected
             && !string.IsNullOrWhiteSpace(NewKey)
             && !_allRows.Any(r => string.Equals(r.Key, NewKey.Trim(), StringComparison.Ordinal));

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
            _log.Error("ServerSettings", ex.Message);
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
        var key = (NewKey ?? "").Trim();
        if (string.IsNullOrEmpty(key)) { Status = "Key is required."; return; }
        if (_allRows.Any(r => string.Equals(r.Key, key, StringComparison.Ordinal)))
        {
            Status = $"Key '{key}' already exists — edit the existing row.";
            return;
        }

        Busy = true;
        Status = $"Creating '{key}'…";
        try
        {
            var ok = await _shell.SetServerSettingAsync(key, NewValue ?? "").ConfigureAwait(true);
            if (!ok) { Status = $"Create '{key}' failed."; return; }

            _allRows.Add(new ServerSettingEditRow(key, NewValue ?? ""));
            _allRows = _allRows.OrderBy(r => r.Key, StringComparer.Ordinal).ToList();
            RebuildVisible();
            NewKey = "";
            NewValue = "";
            _log.Success("ServerSettings", $"Created '{key}'.");
            Status = $"Created '{key}'.";
        }
        catch (Exception ex)
        {
            Status = "Create failed: " + ex.Message;
            _log.Error("ServerSettings", ex.Message);
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
            _log.Error("ServerSettings", ex.Message);
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
            _log.Success("ServerSettings", $"Deleted '{row.Key}'.");
            Status = $"Deleted '{row.Key}'.";
        }
        catch (Exception ex)
        {
            Status = "Delete failed: " + ex.Message;
            _log.Error("ServerSettings", ex.Message);
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
            _log.Error("Backup", $"Server settings backup failed: {ex.Message}");
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
            _log.Error("Export", ex.Message);
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
            supportsDryRun: false).ConfigureAwait(true);
        if (vm?.WorkbookPath is null) return;
        try
        {
            var dict = ModelMeister.Excel.ServerSettingsWorkbook.Load(vm.WorkbookPath);
            var entries = dict.Select(kvp => new KeyValuePair<string, string?>(kvp.Key, kvp.Value));
            var result = await _shell.BulkApplyServerSettingsAsync(entries).ConfigureAwait(true);
            _log.Success("Import", $"Applied {result.Applied.Count} keys, {result.Failed.Count} failed.");
            _log.Toast(LogLevel.Success, "Import complete", $"{result.Applied.Count} keys applied");
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.Error("Import", ex.Message);
            _log.Toast(LogLevel.Error, "Import failed", ex.Message);
        }
    }
}

/// <summary>One row in the server-settings CRUD grid. Tracks the on-server value vs the in-flight edit.</summary>
public partial class ServerSettingEditRow : ObservableObject
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
