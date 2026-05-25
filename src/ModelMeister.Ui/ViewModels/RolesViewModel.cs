using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Excel;
using ModelMeister.Inriver.Users;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// Roles page view-model. Lists roles + their permission bindings in the connected env, lets the user
/// export a workbook (pre-seeded with current roles + available permissions), and provision/update
/// roles from an edited workbook. Pure Remoting — no REST endpoint required (unlike Users).
/// </summary>
public partial class RolesViewModel : FeaturePageViewModel
{
    readonly MainWindowViewModel _main;
    readonly Shell _shell;
    readonly IAppLog _log;

    /// <inheritdoc/>
    public override bool SupportsCompare => true;
    /// <inheritdoc/>
    public override BackupScope BackupScope => BackupScope.Roles;
    /// <inheritdoc/>
    public override ExcelCapability Excel => ExcelCapability.ExportImport;

    /// <inheritdoc/>
    public override async Task BackupAsync()
    {
        if (!_main.IsConnected) { _log.Toast(LogLevel.Warn, "Backup", "Connect first."); return; }
        try
        {
            var path = await _main.Backups.CaptureRolesAsync().ConfigureAwait(true);
            _log.Success("Backup", $"Roles backup saved → {path}");
            _log.Toast(LogLevel.Success, "Roles backup saved", Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            _log.Error("Backup", $"Roles backup failed: {ex.Message}", ex);
            _log.Toast(LogLevel.Error, "Backup failed", ex.Message);
        }
    }

    /// <inheritdoc/>
    public override Task ExportExcelAsync() => DownloadListAsync();

    /// <inheritdoc/>
    public override async Task ImportExcelAsync()
    {
        if (!_main.IsConnected) { _log.Toast(LogLevel.Warn, "Import roles", "Connect first."); return; }
        var vm = await DialogHost.ImportWorkbookAsync(
            "Import roles from workbook",
            "Provision (create/update) roles in the connected environment from an edited roles.xlsx.",
            "roles.xlsx").ConfigureAwait(true);
        if (vm is null) return;
        WorkbookPath = vm.WorkbookPath;
        DryRun = vm.DryRun;
        await ProvisionAsync().ConfigureAwait(true);
    }

    public ObservableCollection<RoleSummary> Roles { get; } = [];
    public ObservableCollection<string> Permissions { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadListCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadTemplateCommand))]
    [NotifyCanExecuteChangedFor(nameof(ProvisionCommand))]
    private bool _busy;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _dryRun = true;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ProvisionCommand))]
    private string? _workbookPath;

    public RolesViewModel(MainWindowViewModel main, Shell shell, IAppLog log)
    {
        _main = main;
        _shell = shell;
        _log = log;
        _main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsConnected))
            {
                MarkDataDirty();
                if (_main.IsConnected) _ = EnsureLoadedAsync();
                DownloadListCommand.NotifyCanExecuteChanged();
                DownloadTemplateCommand.NotifyCanExecuteChanged();
                ProvisionCommand.NotifyCanExecuteChanged();
            }
        };
    }

    private bool CanExport() => !Busy && _main.IsConnected;
    private bool CanProvision() =>
        !Busy && _main.IsConnected && !string.IsNullOrEmpty(WorkbookPath) && File.Exists(WorkbookPath);

    /// <inheritdoc/>
    public override async Task RefreshAsync()
    {
        if (!_main.IsConnected) { Status = "Connect to an environment first."; return; }
        Busy = true;
        try
        {
            var roles = await _shell.ListRolesAsync().ConfigureAwait(true);
            var perms = await _shell.ListPermissionNamesAsync().ConfigureAwait(true);
            Roles.Clear();
            foreach (var r in roles) Roles.Add(r);
            Permissions.Clear();
            foreach (var p in perms) Permissions.Add(p);
            Status = $"{Roles.Count} roles · {Permissions.Count} permissions";
        }
        catch (Exception ex)
        {
            Status = "Failed: " + ex.Message;
            _log.Error("Roles", ex.Message, ex);
        }
        finally { Busy = false; }
    }

    [RelayCommand] private Task CopyName(RoleSummary? r) => ClipboardHelpers.CopyAsync(r?.Name);
    [RelayCommand] private Task CopyDescription(RoleSummary? r) => ClipboardHelpers.CopyAsync(r?.Description);
    [RelayCommand] private Task CopyPermissions(RoleSummary? r) => ClipboardHelpers.CopyAsync(r is null ? null : string.Join(", ", r.Permissions));

    /// <summary>Download the current role list as an xlsx workbook.</summary>
    [RelayCommand(CanExecute = nameof(CanExport))]
    public async Task DownloadListAsync() => await ExportWorkbookAsync(seedSingleExample: false).ConfigureAwait(true);

    /// <summary>Download a minimal example workbook (one row) callers can edit + re-import.</summary>
    [RelayCommand(CanExecute = nameof(CanExport))]
    public async Task DownloadTemplateAsync() => await ExportWorkbookAsync(seedSingleExample: true).ConfigureAwait(true);

    private async Task ExportWorkbookAsync(bool seedSingleExample)
    {
        if (!_main.IsConnected) { Status = "Connect to an environment first."; return; }
        var path = await FilePickerHelpers.PickSaveAsync(
            "Save roles workbook",
            seedSingleExample ? "roles-template.xlsx" : "roles.xlsx",
            "xlsx").ConfigureAwait(true);
        if (path is null) return;

        Busy = true;
        try
        {
            var perms = await _shell.ListPermissionNamesAsync().ConfigureAwait(true);
            List<RolesWorkbook.RoleRow> rows;
            if (seedSingleExample)
            {
                rows = new List<RolesWorkbook.RoleRow>
                {
                    new() { Name = "Example Role", Description = "Example role description", Permissions = new List<string>() },
                };
            }
            else
            {
                var roles = await _shell.ListRolesAsync().ConfigureAwait(true);
                rows = roles.Select(r => new RolesWorkbook.RoleRow
                {
                    Name = r.Name,
                    Description = r.Description,
                    Permissions = r.Permissions.ToList(),
                }).ToList();
            }
            RolesWorkbook.Save(rows, perms.ToList(), path);
            Status = $"Wrote {Path.GetFileName(path)}";
            _log.Success("Roles", $"Exported {(seedSingleExample ? "template" : "list")}: {path}");
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; _log.Error("Roles", ex.Message, ex); }
        finally { Busy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanProvision))]
    public async Task ProvisionAsync()
    {
        if (!_main.IsConnected) { Status = "Connect to an environment first."; return; }
        if (string.IsNullOrEmpty(WorkbookPath) || !File.Exists(WorkbookPath)) { Status = "Pick a workbook."; return; }

        Busy = true;
        try
        {
            var roleRows = RolesWorkbook.Load(WorkbookPath);
            int created = 0, updated = 0, errors = 0;
            var resultRows = new List<ProvisionResultRow>();

            foreach (var row in roleRows)
            {
                if (DryRun)
                {
                    resultRows.Add(new ProvisionResultRow(row.Name, "would-create", $"permissions: {string.Join(", ", row.Permissions)}"));
                    continue;
                }
                var result = await _shell.ProvisionRoleAsync(
                    new RoleProvisioning.RoleSpec(row.Name, row.Description, row.Permissions)).ConfigureAwait(true);
                if (result.Created) created++; else updated++;
                if (result.Errors.Count > 0) errors += result.Errors.Count;
                var outcome = result.Errors.Count > 0 ? "error" : (result.Created ? "created" : "updated");
                var detail = result.Errors.Count > 0 ? string.Join(" · ", result.Errors) : $"permissions: {string.Join(", ", row.Permissions)}";
                resultRows.Add(new ProvisionResultRow(row.Name, outcome, detail));
                foreach (var err in result.Errors) _log.Warn("Roles", $"{row.Name}: {err}");
            }

            var resultVm = new ProvisionResultViewModel(
                dryRun: DryRun,
                created: DryRun ? roleRows.Count : created,
                updated: DryRun ? 0 : updated,
                errors: errors,
                warnings: 0,
                rows: resultRows,
                importEyebrow: "ROLES IMPORT");
            await DialogHost.ShowProvisionResultAsync(resultVm).ConfigureAwait(true);

            Status = DryRun
                ? $"Dry run complete · {roleRows.Count} roles would be processed."
                : $"Provisioned · created {created}, updated {updated}, errors {errors}";
            if (!DryRun) MarkDataDirty();
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; _log.Error("Roles", ex.Message, ex); }
        finally { Busy = false; }
    }
}
