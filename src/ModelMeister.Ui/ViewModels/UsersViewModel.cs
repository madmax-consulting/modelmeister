using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Excel;
using ModelMeister.Inriver.Users;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// Users page view-model. Lists users in the connected env, lets the user export a workbook
/// (pre-seeded with current users + roles), and provision/update users from an edited workbook.
/// </summary>
public partial class UsersViewModel : FeaturePageViewModel
{
    readonly MainWindowViewModel _main;
    readonly Shell _shell;
    readonly IAppLog _log;

    /// <inheritdoc/>
    public override bool SupportsCompare => true;
    /// <inheritdoc/>
    public override BackupScope BackupScope => BackupScope.Users;
    /// <inheritdoc/>
    public override ExcelCapability Excel => ExcelCapability.ExportImport;

    /// <inheritdoc/>
    public override async Task BackupAsync()
    {
        if (!_main.IsConnected) { _log.Toast(LogLevel.Warn, "Backup", "Connect first."); return; }
        try
        {
            var path = await _main.Backups.CaptureUsersAsync().ConfigureAwait(true);
            _log.Success("Backup", $"Users backup saved → {path}");
            _log.Toast(LogLevel.Success, "Users backup saved", Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            _log.Error("Backup", $"Users backup failed: {ex.Message}");
            _log.Toast(LogLevel.Error, "Backup failed", ex.Message);
        }
    }

    /// <inheritdoc/>
    public override Task ExportExcelAsync() => ExportTemplateAsync();

    /// <inheritdoc/>
    public override async Task ImportExcelAsync()
    {
        if (!_main.IsConnected) { _log.Toast(LogLevel.Warn, "Import users", "Connect first."); return; }
        var vm = await DialogHost.ImportWorkbookAsync(
            "Import users from workbook",
            "Provision (create/update) users in the connected environment from an edited users.xlsx.",
            "users.xlsx").ConfigureAwait(true);
        if (vm is null) return;
        WorkbookPath = vm.WorkbookPath;
        DryRun = vm.DryRun;
        await ProvisionAsync().ConfigureAwait(true);
    }

    public ObservableCollection<UserSummary> Users { get; } = [];
    public ObservableCollection<string> Roles { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportTemplateCommand))]
    [NotifyCanExecuteChangedFor(nameof(ProvisionCommand))]
    private bool _busy;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _dryRun = true;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ProvisionCommand))]
    private string? _workbookPath;

    public UsersViewModel(MainWindowViewModel main, Shell shell, IAppLog log)
    {
        _main = main;
        _shell = shell;
        _log = log;
        _main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsConnected))
            {
                ExportTemplateCommand.NotifyCanExecuteChanged();
                ProvisionCommand.NotifyCanExecuteChanged();
            }
        };
    }

    private bool CanExportTemplate() => !Busy && _main.IsConnected;
    private bool CanProvision() =>
        !Busy && _main.IsConnected
              && !string.IsNullOrEmpty(WorkbookPath) && File.Exists(WorkbookPath);

    /// <inheritdoc/>
    public override async Task RefreshAsync()
    {
        if (!_main.IsConnected) { Status = "Connect to an environment first."; return; }
        Busy = true;
        try
        {
            var users = await _shell.ListUsersAsync().ConfigureAwait(true);
            var roles = await _shell.ListRoleNamesAsync().ConfigureAwait(true);
            Users.Clear();
            foreach (var u in users.OrderBy(u => u.Username, StringComparer.OrdinalIgnoreCase)) Users.Add(u);
            Roles.Clear();
            foreach (var r in roles) Roles.Add(r);
            Status = $"{Users.Count} users · {Roles.Count} roles";
        }
        catch (Exception ex)
        {
            Status = "Failed: " + ex.Message;
            _log.Error("Users", ex.Message);
        }
        finally { Busy = false; }
    }

    [RelayCommand] private Task CopyUsername(UserSummary? u) => ClipboardHelpers.CopyAsync(u?.Username);
    [RelayCommand] private Task CopyEmail(UserSummary? u)    => ClipboardHelpers.CopyAsync(u?.Email);
    [RelayCommand] private Task CopyRoles(UserSummary? u)    => ClipboardHelpers.CopyAsync(u is null ? null : string.Join(", ", u.Roles));

    [RelayCommand(CanExecute = nameof(CanExportTemplate))]
    public async Task ExportTemplateAsync()
    {
        if (!_main.IsConnected) { Status = "Connect to an environment first."; return; }
        var path = await PickSaveAsync("users.xlsx").ConfigureAwait(true);
        if (path is null) return;

        Busy = true;
        try
        {
            var users = await _shell.ListUsersAsync().ConfigureAwait(true);
            var roles = await _shell.ListRoleNamesAsync().ConfigureAwait(true);
            var rows = users.Select(u => new UsersWorkbook.UserRow
            {
                Username = u.Username,
                Email = u.Email ?? "",
                FirstName = u.FirstName ?? "",
                LastName = u.LastName ?? "",
                Company = u.Company ?? "",
                Roles = u.Roles.ToList(),
                Language = "en",
            }).ToList();
            UsersWorkbook.Save(rows, roles.ToList(), path);
            Status = $"Wrote {Path.GetFileName(path)}";
            _log.Success("Users", $"Exported template: {path}");
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; _log.Error("Users", ex.Message); }
        finally { Busy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanProvision))]
    public async Task ProvisionAsync()
    {
        if (!_main.IsConnected) { Status = "Connect to an environment first."; return; }
        if (string.IsNullOrEmpty(WorkbookPath) || !File.Exists(WorkbookPath))
        {
            Status = "Pick a workbook.";
            return;
        }

        Busy = true;
        try
        {
            var env = _main.ConnectedEnv;
            var secret = env is null ? null : _main.Vault.GetSecret(env.Id);
            var users = UsersWorkbook.Load(WorkbookPath);
            int created = 0, updated = 0, errors = 0;

            foreach (var u in users)
            {
                if (DryRun)
                {
                    _log.Info("Users", $"dry: would provision {u.Username} -> {string.Join(", ", u.Roles)}");
                    continue;
                }
                var result = await _shell.ProvisionUserAsync(new UserProvisioning.UserSpec(
                    u.Username, u.Email, u.FirstName, u.LastName, u.Company,
                    u.Roles, u.Language, u.GenerateApiKey), secret, env!).ConfigureAwait(true);
                if (result.Created) created++;
                else updated++;
                if (result.Errors.Count > 0) errors += result.Errors.Count;
                foreach (var err in result.Errors) _log.Warn("Users", $"{u.Username}: {err}");
            }
            Status = DryRun
                ? $"Dry run complete · {users.Count} users would be processed."
                : $"Provisioned · created {created}, updated {updated}, errors {errors}";
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; _log.Error("Users", ex.Message); }
        finally { Busy = false; }
    }

    static Window? MainWindowOrNull()
        => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d ? d.MainWindow : null;

    static async Task<string?> PickSaveAsync(string suggested)
    {
        var w = MainWindowOrNull();
        if (w is null) return null;
        var pick = await w.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save users workbook",
            SuggestedFileName = suggested,
            DefaultExtension = "xlsx",
        }).ConfigureAwait(true);
        return pick?.TryGetLocalPath();
    }
}
