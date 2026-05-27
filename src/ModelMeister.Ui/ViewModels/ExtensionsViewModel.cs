using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Inriver.Extensions;
using ModelMeister.Ui.Models;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// Extensions page view-model. Wraps the inriver Connector/Extension surface: list, start/stop, run,
/// inspect/edit settings, manage connector states, and view recent events (per-extension and
/// system-wide). Uses the connected env's REST credentials when available, falling back to Remoting
/// otherwise. REST-only features (run trigger) are disabled when no REST key is configured.
/// </summary>
public partial class ExtensionsViewModel : FeaturePageViewModel
{
    /// <inheritdoc/>
    public override bool SupportsCompare => true;
    /// <inheritdoc/>
    public override BackupScope BackupScope => BackupScope.Extensions;
    /// <inheritdoc/>
    public override ExcelCapability Excel => ExcelCapability.None;

    /// <inheritdoc/>
    public override async Task BackupAsync()
    {
        if (!_main.IsConnected)
        {
            _log.Toast(LogLevel.Warn, "Backup", "Connect to an environment first.");
            return;
        }
        try
        {
            var path = await _main.Backups.CaptureExtensionsAsync().ConfigureAwait(true);
            _log.Success("Backup", $"Extensions backup saved → {path}");
            _log.Toast(LogLevel.Success, "Extensions backup saved", System.IO.Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            _log.Error("Backup", $"Extensions backup failed: {ex.Message}", ex);
            _log.Toast(LogLevel.Error, "Backup failed", ex.Message);
        }
    }

    readonly MainWindowViewModel _main;
    readonly Shell _shell;
    readonly IAppLog _log;

    public ObservableCollection<ExtensionRow> Items { get; } = [];
    public ObservableCollection<ExtensionEventRow> Events { get; } = [];
    public ObservableCollection<ExtensionEventRow> AllEvents { get; } = [];
    public ObservableCollection<ExtensionSettingRow> Settings { get; } = [];
    public ObservableCollection<ExtensionStateRowVm> States { get; } = [];

    /// <summary>Checkbox selection for the extensions list, settings grid, and states grid.</summary>
    public RowSelectionModel ItemsSelection { get; }
    public RowSelectionModel SettingsSelection { get; }
    public RowSelectionModel StatesSelection { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshAllEventsCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(ViewEventsCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadStatesCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddSettingCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSettingCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddStateCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteStateCommand))]
    private bool _busy;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddSettingCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddStateCommand))]
    private ExtensionRow? _selected;

    // Inline-edit fields for the Settings panel.
    [ObservableProperty] private string _newSettingKey = "";
    [ObservableProperty] private string _newSettingValue = "";

    // Inline-edit fields for the States panel.
    [ObservableProperty] private string _newStateData = "";

    /// <summary>True when the connected env has REST credentials — gates the "Run" action.</summary>
    public bool HasRestKey
    {
        get
        {
            var env = _main.ConnectedEnv;
            if (env is null || string.IsNullOrEmpty(env.RestBaseUrl)) return false;
            var secret = _main.Vault.GetSecret(env.Id);
            return secret is not null && !string.IsNullOrEmpty(secret.RestApiKey);
        }
    }

    public ExtensionsViewModel(MainWindowViewModel main, Shell shell, IAppLog log)
    {
        _main = main;
        _shell = shell;
        _log = log;
        ItemsSelection = new RowSelectionModel(Items);
        SettingsSelection = new RowSelectionModel(Settings);
        StatesSelection = new RowSelectionModel(States);
        _main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsConnected))
            {
                OnPropertyChanged(nameof(HasRestKey));
                // Connection change invalidates the cache; flag so EnsureLoadedAsync re-fetches.
                MarkDataDirty();
                if (_main.IsConnected) _ = EnsureLoadedAsync();
            }
        };
    }

    private bool NotBusy() => !Busy;

    /// <inheritdoc/>
    public override async Task RefreshAsync()
    {
        if (!_main.IsConnected) { StatusMessage = "Connect to an environment first."; return; }
        Busy = true;
        StatusMessage = "Loading extensions…";
        try
        {
            var env = _main.ConnectedEnv;
            var secret = env is null ? null : _main.Vault.GetSecret(env.Id);
            var items = await _shell.ListExtensionsAsync(env, secret).ConfigureAwait(true);
            Items.Clear();
            foreach (var i in items.OrderBy(i => i.Id, StringComparer.OrdinalIgnoreCase))
                Items.Add(new ExtensionRow(i));
            StatusMessage = $"{Items.Count} extensions";
            OnPropertyChanged(nameof(HasRestKey));
        }
        catch (Exception ex)
        {
            StatusMessage = "Failed: " + ex.Message;
            _log.Error("Extensions", ex.Message, ex);
        }
        finally { Busy = false; }
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    public async Task RefreshAllEventsAsync()
    {
        if (!_main.IsConnected) { StatusMessage = "Connect to an environment first."; return; }
        Busy = true;
        try
        {
            var events = await _shell.LatestExtensionEventsAsync(200).ConfigureAwait(true);
            AllEvents.Clear();
            foreach (var ev in events) AllEvents.Add(new ExtensionEventRow(ev));
            StatusMessage = $"{AllEvents.Count} events across all extensions";
        }
        catch (Exception ex)
        {
            StatusMessage = "Latest events failed: " + ex.Message;
            _log.Error("Extensions", ex.Message, ex);
        }
        finally { Busy = false; }
    }

    [RelayCommand] private Task CopyExtensionId(ExtensionRow? row) => ClipboardHelpers.CopyAsync(row?.Info.Id);
    [RelayCommand] private Task CopyExtensionType(ExtensionRow? row) => ClipboardHelpers.CopyAsync(row?.Info.TypeName);

    [RelayCommand(CanExecute = nameof(NotBusy))]
    public async Task StartAsync(ExtensionRow? row)
    {
        if (row is null) return;
        Busy = true;
        var env = _main.ConnectedEnv;
        var secret = env is null ? null : _main.Vault.GetSecret(env.Id);
        var ok = await _shell.StartExtensionAsync(row.Info.Id, env, secret).ConfigureAwait(true);
        _log.Info("Extensions", $"Start {row.Info.Id}: {(ok ? "ok" : "failed")}");
        await RefreshAsync().ConfigureAwait(true);
        Busy = false;
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    public async Task StopAsync(ExtensionRow? row)
    {
        if (row is null) return;
        Busy = true;
        var env = _main.ConnectedEnv;
        var secret = env is null ? null : _main.Vault.GetSecret(env.Id);
        var ok = await _shell.StopExtensionAsync(row.Info.Id, env, secret).ConfigureAwait(true);
        _log.Info("Extensions", $"Stop {row.Info.Id}: {(ok ? "ok" : "failed")}");
        await RefreshAsync().ConfigureAwait(true);
        Busy = false;
    }

    /// <summary>POST <c>:run</c> via REST. Disabled when no REST key is configured.</summary>
    [RelayCommand(CanExecute = nameof(NotBusy))]
    public async Task RunAsync(ExtensionRow? row)
    {
        if (row is null) return;
        if (!HasRestKey) { StatusMessage = "REST key required for run trigger."; return; }
        Busy = true;
        try
        {
            var env = _main.ConnectedEnv;
            var secret = env is null ? null : _main.Vault.GetSecret(env.Id);
            var ok = await _shell.RunExtensionAsync(row.Info.Id, env, secret).ConfigureAwait(true);
            _log.Info("Extensions", $"Run {row.Info.Id}: {(ok ? "triggered" : "failed")}");
            StatusMessage = ok ? $"Run triggered for {row.Info.Id}." : $"Run failed for {row.Info.Id}.";
        }
        finally { Busy = false; }
    }

    // ----- Bulk start / stop / run over the checked extensions -----

    /// <summary>Start every checked extension.</summary>
    [RelayCommand(CanExecute = nameof(NotBusy))]
    public Task StartSelectedAsync() => BulkExtensionActionAsync("Start",
        (id, env, secret) => _shell.StartExtensionAsync(id, env, secret));

    /// <summary>Stop every checked extension.</summary>
    [RelayCommand(CanExecute = nameof(NotBusy))]
    public Task StopSelectedAsync() => BulkExtensionActionAsync("Stop",
        (id, env, secret) => _shell.StopExtensionAsync(id, env, secret));

    /// <summary>Trigger a run on every checked extension (REST; gated on a configured REST key).</summary>
    [RelayCommand(CanExecute = nameof(NotBusy))]
    public Task RunSelectedAsync()
    {
        if (!HasRestKey) { StatusMessage = "REST key required for run trigger."; return Task.CompletedTask; }
        return BulkExtensionActionAsync("Run", (id, env, secret) => _shell.RunExtensionAsync(id, env, secret));
    }

    private async Task BulkExtensionActionAsync(
        string verb, Func<string, Models.EnvironmentEntry?, Models.EnvironmentSecret?, Task<bool>> action)
    {
        var ids = ItemsSelection.SelectedOf<ExtensionRow>().Select(r => r.Info.Id).ToList();
        if (ids.Count == 0) { StatusMessage = "Select at least one extension."; return; }
        Busy = true;
        var env = _main.ConnectedEnv;
        var secret = env is null ? null : _main.Vault.GetSecret(env.Id);
        int ok = 0, failed = 0;
        try
        {
            foreach (var id in ids)
            {
                StatusMessage = $"{verb} '{id}'…";
                try { if (await action(id, env, secret).ConfigureAwait(true)) ok++; else failed++; }
                catch (Exception ex) { failed++; _log.Warn("Extensions", $"{verb} '{id}' failed: {ex.Message}"); }
            }
            StatusMessage = failed == 0 ? $"{verb} · {ok} extension(s)." : $"{verb} · {ok} ok, {failed} failed.";
            _log.Info("Extensions", StatusMessage);
            await RefreshAsync().ConfigureAwait(true);
        }
        finally { Busy = false; }
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    public async Task ViewEventsAsync(ExtensionRow? row)
    {
        if (row is null) return;
        Busy = true;
        try
        {
            var events = await _shell.ExtensionEventsAsync(row.Info.Id, 100).ConfigureAwait(true);
            Events.Clear();
            foreach (var ev in events) Events.Add(new ExtensionEventRow(ev));
            StatusMessage = $"{Events.Count} recent events for {row.Info.Id}";
        }
        catch (Exception ex)
        {
            StatusMessage = "Events failed: " + ex.Message;
            _log.Error("Extensions", ex.Message, ex);
        }
        finally { Busy = false; }
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    public async Task LoadStatesAsync(ExtensionRow? row)
    {
        if (row is null) return;
        Busy = true;
        try
        {
            var states = await _shell.ListExtensionStatesAsync(row.Info.Id).ConfigureAwait(true);
            States.Clear();
            foreach (var s in states) States.Add(new ExtensionStateRowVm(s));
            StatusMessage = $"{States.Count} connector states for {row.Info.Id}";
        }
        catch (Exception ex)
        {
            StatusMessage = "States failed: " + ex.Message;
            _log.Error("Extensions", ex.Message, ex);
        }
        finally { Busy = false; }
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    public async Task AddSettingAsync()
    {
        if (Selected is null) return;
        var key = NewSettingKey.Trim();
        if (string.IsNullOrEmpty(key)) { StatusMessage = "Setting key required."; return; }

        Busy = true;
        try
        {
            var ok = await _shell.SetExtensionSettingAsync(Selected.Info.Id, key, NewSettingValue ?? "").ConfigureAwait(true);
            if (ok)
            {
                _log.Success("Extensions", $"Set '{key}' on {Selected.Info.Id}.");
                NewSettingKey = ""; NewSettingValue = "";
                await ReloadSelectedDetailsAsync().ConfigureAwait(true);
            }
            else
            {
                StatusMessage = "Set setting failed."; _log.Warn("Extensions", $"Set '{key}' on {Selected.Info.Id} returned false.");
            }
        }
        finally { Busy = false; }
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    public async Task DeleteSettingAsync(ExtensionSettingRow? row)
    {
        if (Selected is null || row is null) return;
        Busy = true;
        try
        {
            var ok = await _shell.DeleteExtensionSettingAsync(Selected.Info.Id, row.Key).ConfigureAwait(true);
            if (ok)
            {
                _log.Success("Extensions", $"Deleted '{row.Key}' from {Selected.Info.Id}.");
                await ReloadSelectedDetailsAsync().ConfigureAwait(true);
            }
            else { StatusMessage = "Delete setting failed."; }
        }
        finally { Busy = false; }
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    public async Task AddStateAsync()
    {
        if (Selected is null) return;
        if (string.IsNullOrEmpty(NewStateData)) { StatusMessage = "State data required."; return; }
        Busy = true;
        try
        {
            var saved = await _shell.AddExtensionStateAsync(Selected.Info.Id, NewStateData).ConfigureAwait(true);
            if (saved is not null)
            {
                NewStateData = "";
                await LoadStatesAsync(Selected).ConfigureAwait(true);
            }
            else { StatusMessage = "Add state failed."; }
        }
        finally { Busy = false; }
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    public async Task DeleteStateAsync(ExtensionStateRowVm? row)
    {
        if (row is null) return;
        Busy = true;
        try
        {
            var ok = await _shell.DeleteExtensionStateAsync(row.Row.Id).ConfigureAwait(true);
            if (ok) { States.Remove(row); _log.Success("Extensions", $"Deleted state #{row.Row.Id}."); }
            else { StatusMessage = "Delete state failed."; }
        }
        finally { Busy = false; }
    }

    // ----- Delete extension (Connector) -----

    /// <summary>Delete a single extension after a confirmation prompt.</summary>
    [RelayCommand(CanExecute = nameof(NotBusy))]
    public async Task DeleteExtensionAsync(ExtensionRow? row)
    {
        if (row is null || !_main.IsConnected) return;
        await ConfirmAndDeleteExtensionsAsync(new[] { row.Info.Id }).ConfigureAwait(true);
    }

    /// <summary>Delete every checked extension after a single itemized confirmation prompt.</summary>
    [RelayCommand(CanExecute = nameof(NotBusy))]
    public async Task DeleteSelectedExtensionsAsync()
    {
        var ids = ItemsSelection.SelectedOf<ExtensionRow>().Select(r => r.Info.Id).ToList();
        if (ids.Count == 0) { StatusMessage = "Select at least one extension."; return; }
        await ConfirmAndDeleteExtensionsAsync(ids).ConfigureAwait(true);
    }

    private async Task ConfirmAndDeleteExtensionsAsync(IReadOnlyList<string> ids)
    {
        if (!_main.IsConnected) { StatusMessage = "Connect first."; return; }
        var ok = await DialogHost.ConfirmBulkAsync("Delete extensions", "Delete", "extension", ids,
            _main.ConnectedEnv?.Name, _main.ConnectedEnv?.TypeKey).ConfigureAwait(true);
        if (!ok) return;
        await DeleteExtensionsAsync(ids).ConfigureAwait(true);
    }

    private async Task DeleteExtensionsAsync(IReadOnlyList<string> ids)
    {
        Busy = true;
        int deleted = 0, errors = 0;
        try
        {
            await RunBulkAsync(ids,
                async id =>
                {
                    try { if (await _shell.DeleteExtensionAsync(id).ConfigureAwait(false)) { deleted++; _log.Success("Extensions", $"Deleted '{id}'."); } else errors++; }
                    catch (Exception ex) { errors++; _log.Warn("Extensions", $"Delete '{id}' failed: {ex.Message}"); }
                },
                (i, total, id) => StatusMessage = $"Deleting extension {i} / {total} ('{id}')…").ConfigureAwait(true);
            StatusMessage = errors == 0 ? $"Deleted {deleted} extension(s)." : $"Deleted {deleted}, {errors} failed.";
            MarkDataDirty();
            await RefreshAsync().ConfigureAwait(true);
        }
        finally { Busy = false; }
    }

    // ----- Settings edit + bulk delete -----

    /// <summary>Edit a setting value via the shared key/value dialog, then persist.</summary>
    [RelayCommand(CanExecute = nameof(NotBusy))]
    public async Task EditSettingAsync(ExtensionSettingRow? row)
    {
        if (Selected is null || row is null) return;
        var vm = await DialogHost.AddServerSettingAsync(row.Key, row.Value, isEdit: true).ConfigureAwait(true);
        if (vm is null) return;
        Busy = true;
        try
        {
            var ok = await _shell.SetExtensionSettingAsync(Selected.Info.Id, row.Key, vm.Value ?? "").ConfigureAwait(true);
            if (ok) { _log.Success("Extensions", $"Updated '{row.Key}' on {Selected.Info.Id}."); await ReloadSelectedDetailsAsync().ConfigureAwait(true); }
            else StatusMessage = "Set setting failed.";
        }
        finally { Busy = false; }
    }

    /// <summary>Delete every checked setting from the selected extension.</summary>
    [RelayCommand(CanExecute = nameof(NotBusy))]
    public async Task DeleteSelectedSettingsAsync()
    {
        if (Selected is null) return;
        var keys = SettingsSelection.SelectedOf<ExtensionSettingRow>().Select(r => r.Key).ToList();
        if (keys.Count == 0) { StatusMessage = "Select at least one setting."; return; }
        var ok = await DialogHost.ConfirmBulkAsync($"Delete settings from {Selected.Info.Id}", "Delete", "setting", keys,
            _main.ConnectedEnv?.Name, _main.ConnectedEnv?.TypeKey).ConfigureAwait(true);
        if (!ok) return;
        Busy = true;
        try
        {
            foreach (var key in keys) await _shell.DeleteExtensionSettingAsync(Selected.Info.Id, key).ConfigureAwait(true);
            _log.Success("Extensions", $"Deleted {keys.Count} setting(s) from {Selected.Info.Id}.");
            await ReloadSelectedDetailsAsync().ConfigureAwait(true);
        }
        finally { Busy = false; }
    }

    // ----- State edit + bulk delete -----

    /// <summary>Edit a connector state's data via the shared dialog, then persist.</summary>
    [RelayCommand(CanExecute = nameof(NotBusy))]
    public async Task EditStateAsync(ExtensionStateRowVm? row)
    {
        if (row is null) return;
        var vm = await DialogHost.AddServerSettingAsync($"State #{row.Row.Id}", row.Data, isEdit: true).ConfigureAwait(true);
        if (vm is null) return;
        Busy = true;
        try
        {
            var ok = await _shell.UpdateExtensionStateAsync(row.Row.Id, row.ConnectorId, vm.Value ?? "").ConfigureAwait(true);
            if (ok && Selected is not null) { _log.Success("Extensions", $"Updated state #{row.Row.Id}."); await LoadStatesAsync(Selected).ConfigureAwait(true); }
            else if (!ok) StatusMessage = "Update state failed.";
        }
        finally { Busy = false; }
    }

    /// <summary>Delete every checked connector state.</summary>
    [RelayCommand(CanExecute = nameof(NotBusy))]
    public async Task DeleteSelectedStatesAsync()
    {
        var rows = StatesSelection.SelectedOf<ExtensionStateRowVm>().ToList();
        if (rows.Count == 0) { StatusMessage = "Select at least one state."; return; }
        var ok = await DialogHost.ConfirmBulkAsync("Delete connector states", "Delete", "state",
            rows.Select(r => $"State #{r.Row.Id}").ToList(),
            _main.ConnectedEnv?.Name, _main.ConnectedEnv?.TypeKey).ConfigureAwait(true);
        if (!ok) return;
        Busy = true;
        try
        {
            foreach (var row in rows)
                if (await _shell.DeleteExtensionStateAsync(row.Row.Id).ConfigureAwait(true)) States.Remove(row);
            _log.Success("Extensions", $"Deleted {rows.Count} state(s).");
        }
        finally { Busy = false; }
    }

    partial void OnSelectedChanged(ExtensionRow? value)
    {
        if (value is null) return;
        _ = ReloadSelectedDetailsAsync();
    }

    private async Task ReloadSelectedDetailsAsync()
    {
        if (Selected is null) return;
        // Refresh settings from the freshly-fetched list (the List() call already populated Settings dict).
        // Re-list extensions so we pick up any changes the user just made to settings/state.
        var env = _main.ConnectedEnv;
        var secret = env is null ? null : _main.Vault.GetSecret(env.Id);
        try
        {
            var items = await _shell.ListExtensionsAsync(env, secret).ConfigureAwait(true);
            var fresh = items.FirstOrDefault(i => string.Equals(i.Id, Selected.Info.Id, StringComparison.Ordinal));
            if (fresh is not null)
            {
                Settings.Clear();
                foreach (var kvp in fresh.Settings.OrderBy(k => k.Key, StringComparer.Ordinal))
                    Settings.Add(new ExtensionSettingRow(kvp.Key, kvp.Value));
            }
            await ViewEventsAsync(Selected).ConfigureAwait(true);
            await LoadStatesAsync(Selected).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.Warn("Extensions", $"Reload details failed: {ex.Message}", ex);
        }
    }
}

public sealed partial class ExtensionRow : SelectableRow
{
    public ExtensionsService.ExtensionInfo Info { get; }
    public ExtensionRow(ExtensionsService.ExtensionInfo info) => Info = info;
    public string Id => Info.Id;
    public string TypeName => Info.TypeName ?? "";
    public bool IsStarted => Info.IsStarted;
    public string Status => Info.IsStarted ? "running" : "stopped";
    public string LastEventUtc => Info.LastEventUtc?.ToString("u") ?? "";
    public string LastEventMessage => Info.LastEventMessage ?? "";
    public int RecentErrorCount => Info.RecentErrorCount;
    public int SettingsCount => Info.Settings.Count;
}

public sealed class ExtensionEventRow
{
    public ExtensionsService.ExtensionEvent Event { get; }
    public ExtensionEventRow(ExtensionsService.ExtensionEvent e) => Event = e;
    public string Utc => Event.Utc.ToString("u");
    public string Severity => Event.IsError ? "ERROR" : "info";
    public string Message => Event.Message;
    public string ConnectorId => Event.ConnectorId ?? "";
}

public sealed partial class ExtensionSettingRow : SelectableRow
{
    public string Key { get; }
    public string Value { get; }
    public ExtensionSettingRow(string key, string value) { Key = key; Value = value; }
}

public sealed partial class ExtensionStateRowVm : SelectableRow
{
    public ExtensionsService.ExtensionStateRow Row { get; }
    public ExtensionStateRowVm(ExtensionsService.ExtensionStateRow row) => Row = row;
    public int Id => Row.Id;
    public string ConnectorId => Row.ConnectorId;
    public string Data => Row.Data;
    public string ModifiedUtc => Row.Modified.ToString("u");
}
