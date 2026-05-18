using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Inriver.Extensions;
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

public sealed class ExtensionRow
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

public sealed class ExtensionSettingRow
{
    public string Key { get; }
    public string Value { get; }
    public ExtensionSettingRow(string key, string value) { Key = key; Value = value; }
}

public sealed class ExtensionStateRowVm
{
    public ExtensionsService.ExtensionStateRow Row { get; }
    public ExtensionStateRowVm(ExtensionsService.ExtensionStateRow row) => Row = row;
    public int Id => Row.Id;
    public string ConnectorId => Row.ConnectorId;
    public string Data => Row.Data;
    public string ModifiedUtc => Row.Modified.ToString("u");
}
