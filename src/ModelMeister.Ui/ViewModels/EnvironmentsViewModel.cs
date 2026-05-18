using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Ui.Models;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// View-model for the Environments page. Surfaces the persisted vault as a sortable grid and
/// drives the Connect/Disconnect / Add / Edit / Delete flows. Also tracks which row is "default"
/// (auto-connect on startup); that flag is set from the edit dialog and the row's context menu.
/// </summary>
public partial class EnvironmentsViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _main;
    private readonly IEnvironmentVault _vault;
    private readonly ISettingsStore _settings;
    private readonly IConnectionLifecycle _connection;
    private readonly IAppLog _log;

    /// <summary>One row per <see cref="EnvironmentEntry"/> in the vault, sorted alphabetically by name.</summary>
    public ObservableCollection<EnvironmentRow> Rows { get; } = [];

    /// <summary>Row the user has clicked on (drives Connect/Edit/Delete enabling).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetDefaultCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConnectOrDisconnectCommand))]
    private EnvironmentRow? _selectedRow;
    /// <summary>Short feedback shown at the bottom of the page after an action.</summary>
    [ObservableProperty] private string _statusMessage = "";
    /// <summary>True while a connect is in flight; disables buttons.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetDefaultCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConnectOrDisconnectCommand))]
    private bool _busy;
    /// <summary>True when a connection is active (mirrors <see cref="IConnectionLifecycle.State"/>).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectOrDisconnectCommand))]
    private bool _isConnected;

    public EnvironmentsViewModel(
        MainWindowViewModel main,
        IEnvironmentVault vault,
        ISettingsStore settings,
        IConnectionLifecycle connection,
        IAppLog log)
    {
        _main = main;
        _vault = vault;
        _settings = settings;
        _connection = connection;
        _log = log;
        _isConnected = connection.State == ConnectionState.Connected;
        connection.Changed += () =>
        {
            IsConnected = _connection.State == ConnectionState.Connected;
            RefreshConnectedFlags();
        };
        Refresh();
    }

    private void Refresh()
    {
        var preservedId = SelectedRow?.Entry.Id;
        var defaultId = _settings.Current.DefaultEnvId;

        Rows.Clear();
        foreach (var entry in _vault.List().OrderBy(x => x.Name))
            Rows.Add(new EnvironmentRow(entry, _vault.SecretMissing(entry.Id), entry.Id == defaultId));

        if (preservedId is Guid pid)
            SelectedRow = Rows.FirstOrDefault(r => r.Entry.Id == pid);

        RefreshConnectedFlags();
    }

    private void RefreshConnectedFlags()
    {
        var connectedId = _connection.Connected?.Id;
        foreach (var r in Rows)
            r.IsConnected = connectedId is Guid id && r.Entry.Id == id;
    }

    private bool CanAct() => !Busy;
    private bool CanActOnRow() => !Busy && SelectedRow is not null;
    private bool CanConnectOrDisconnect() =>
        !Busy && (IsConnected || SelectedRow is not null);

    [RelayCommand(CanExecute = nameof(CanActOnRow))]
    private Task CopyRemotingUrl() => Services.ClipboardHelpers.CopyAsync(SelectedRow?.Entry.Url);

    /// <summary>Toggle whether the selected row is the auto-connect default. Driven by the row's context menu.</summary>
    [RelayCommand(CanExecute = nameof(CanActOnRow))]
    private void SetDefault()
    {
        if (SelectedRow is null) return;
        ApplyDefault(SelectedRow.Entry.Id, !SelectedRow.IsDefault);
    }

    private void ApplyDefault(Guid id, bool isDefault)
    {
        _settings.Current.DefaultEnvId = isDefault ? id : null;
        _settings.Save();
        Refresh();

        var name = Rows.FirstOrDefault(r => r.Entry.Id == id)?.Entry.Name ?? "(unknown)";
        var msg = isDefault
            ? $"'{name}' will auto-connect on startup."
            : "Default cleared — no auto-connect on startup.";
        StatusMessage = msg;
        _log.Info("Environments", msg);
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task AddAsync()
    {
        var entry = new EnvironmentEntry { Name = "", Url = EnvEditorViewModel.DefaultUrl };
        var dlg = new EnvEditorViewModel(entry, new EnvironmentSecret(), isDefault: false, _connection, _log);
        if (!await DialogHost.ShowAsync(dlg).ConfigureAwait(true)) return;

        _vault.Upsert(dlg.Entry, dlg.Secret);
        if (dlg.IsDefault) _settings.Current.DefaultEnvId = dlg.Entry.Id;
        else if (_settings.Current.DefaultEnvId == dlg.Entry.Id) _settings.Current.DefaultEnvId = null;
        _settings.Save();
        Refresh();
        StatusMessage = $"Saved environment '{dlg.Entry.Name}'.";
        _log.Success("Environments", StatusMessage);
    }

    [RelayCommand(CanExecute = nameof(CanActOnRow))]
    private async Task EditAsync()
    {
        if (SelectedRow is null) return;

        var existingSecret = _vault.GetSecret(SelectedRow.Entry.Id) ?? new EnvironmentSecret();
        var dlg = new EnvEditorViewModel(
            Clone(SelectedRow.Entry),
            Clone(existingSecret),
            isDefault: SelectedRow.IsDefault,
            _connection,
            _log);
        if (!await DialogHost.ShowAsync(dlg).ConfigureAwait(true)) return;

        _vault.Upsert(dlg.Entry, dlg.Secret);
        if (dlg.IsDefault) _settings.Current.DefaultEnvId = dlg.Entry.Id;
        else if (_settings.Current.DefaultEnvId == dlg.Entry.Id) _settings.Current.DefaultEnvId = null;
        _settings.Save();
        Refresh();
        StatusMessage = $"Updated environment '{dlg.Entry.Name}'.";
        _log.Success("Environments", StatusMessage);
    }

    [RelayCommand(CanExecute = nameof(CanActOnRow))]
    private void Delete()
    {
        if (SelectedRow is null) return;
        var id = SelectedRow.Entry.Id;
        var name = SelectedRow.Entry.Name;
        _vault.Delete(id);

        // Clear any setting pointers to the just-removed env.
        var settingsDirty = false;
        if (_settings.Current.LastUsedEnvId == id) { _settings.Current.LastUsedEnvId = null; settingsDirty = true; }
        if (_settings.Current.DefaultEnvId == id)  { _settings.Current.DefaultEnvId = null;  settingsDirty = true; }
        if (settingsDirty) _settings.Save();

        Refresh();
        StatusMessage = "Environment deleted.";
        _log.Info("Environments", $"Deleted environment '{name}'.");
    }

    /// <summary>
    /// Toggle: when connected to the *selected* env, disconnect. When connected to a *different*
    /// env (or none), connect to the selected env — disconnecting first if needed. Single click
    /// always achieves the user's intent.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanConnectOrDisconnect))]
    public async Task ConnectOrDisconnectAsync()
    {
        if (_connection.State == ConnectionState.Connected)
        {
            // Same env selected → disconnect. Different env (or no selection) → switch.
            if (SelectedRow is null || _connection.Connected?.Id == SelectedRow.Entry.Id)
            {
                await DisconnectAsync().ConfigureAwait(true);
                return;
            }
            await DisconnectAsync().ConfigureAwait(true);
        }
        await ConnectAsync().ConfigureAwait(true);
    }

    private async Task ConnectAsync()
    {
        if (SelectedRow is null) return;

        var secret = _vault.GetSecret(SelectedRow.Entry.Id);
        if (secret is null)
        {
            StatusMessage = "No stored secret for this environment — open Edit and re-enter it.";
            _log.Warn("Connection", $"No stored secret for '{SelectedRow.Entry.Name}'.");
            return;
        }

        Busy = true;
        SelectedRow.IsTesting = true;
        StatusMessage = $"Connecting to {SelectedRow.Entry.Url}…";
        _log.Info("Connection", $"Connecting to '{SelectedRow.Entry.Name}' ({SelectedRow.Entry.Url})…");

        try
        {
            await _connection.ConnectAsync(SelectedRow.Entry, secret).ConfigureAwait(true);
            if (_connection.State == ConnectionState.Connected)
            {
                _vault.Touch(SelectedRow.Entry.Id);
                _settings.Current.LastUsedEnvId = SelectedRow.Entry.Id;
                _settings.Save();
                StatusMessage = $"Connected to '{SelectedRow.Entry.Name}'.";
                Refresh();
            }
            else
            {
                StatusMessage = "Connect failed: " + (_connection.LastError ?? "unknown error");
            }
        }
        finally
        {
            if (SelectedRow is not null) SelectedRow.IsTesting = false;
            Busy = false;
        }
    }

    private async Task DisconnectAsync()
    {
        Busy = true;
        try
        {
            await _connection.DisconnectAsync().ConfigureAwait(true);
            StatusMessage = "Disconnected.";
            _log.Info("Connection", "Disconnected.");
        }
        finally
        {
            Busy = false;
        }
    }

    private static EnvironmentEntry Clone(EnvironmentEntry e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Url = e.Url,
        Stage = e.Stage,
        Notes = e.Notes,
        LastUsedUtc = e.LastUsedUtc,
    };

    private static EnvironmentSecret Clone(EnvironmentSecret s) => new()
    {
        ApiKey = s.ApiKey,
    };
}

/// <summary>
/// Display projection of an <see cref="EnvironmentEntry"/>. Names of the public properties are
/// load-bearing — they are referenced from <c>EnvironmentsView.axaml</c> column bindings.
/// </summary>
public sealed partial class EnvironmentRow : ObservableObject
{
    public EnvironmentRow(EnvironmentEntry entry, bool secretMissing, bool isDefault)
    {
        Entry = entry;
        SecretMissing = secretMissing;
        IsDefault = isDefault;
    }

    public EnvironmentEntry Entry { get; }
    public bool SecretMissing { get; }
    public bool IsDefault { get; }

    public string Name => Entry.Name;
    public string Stage => Entry.Stage.ToString();
    public string LastUsed => Entry.LastUsedUtc == default
        ? "—"
        : Entry.LastUsedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string Notes => Entry.Notes ?? "";
    public string DefaultMarker => IsDefault ? "★" : "";

    [ObservableProperty] private bool _isTesting;
    [ObservableProperty] private bool _testSucceeded;
    [ObservableProperty] private string _testResult = "";
    /// <summary>True when this row's environment is the one currently connected — drives the light-green row tint.</summary>
    [ObservableProperty] private bool _isConnected;
}
