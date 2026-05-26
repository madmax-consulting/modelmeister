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
using ModelMeister.Ui.Models;
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

    public ObservableCollection<RoleListRow> Roles { get; } = [];
    public ObservableCollection<string> Permissions { get; } = [];

    /// <summary>Checkbox multi-selection over <see cref="Roles"/> (header select-all + bulk delete).</summary>
    public RowSelectionModel Selection { get; }

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
        Selection = new RowSelectionModel(Roles);
        _main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsConnected))
            {
                MarkDataDirty();
                if (_main.IsConnected) _ = EnsureLoadedAsync();
                DownloadListCommand.NotifyCanExecuteChanged();
                DownloadTemplateCommand.NotifyCanExecuteChanged();
                ProvisionCommand.NotifyCanExecuteChanged();
                AddRoleCommand.NotifyCanExecuteChanged();
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
            foreach (var r in roles) Roles.Add(new RoleListRow(r));
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

    [RelayCommand] private Task CopyName(RoleListRow? r) => ClipboardHelpers.CopyAsync(r?.Name);
    [RelayCommand] private Task CopyDescription(RoleListRow? r) => ClipboardHelpers.CopyAsync(r?.Description);
    [RelayCommand] private Task CopyPermissions(RoleListRow? r) => ClipboardHelpers.CopyAsync(r is null ? null : string.Join(", ", r.Permissions));

    // ----- CRUD: create / edit / delete (Remoting supports full role CRUD) -----

    private bool CanMutate() => !Busy && _main.IsConnected;

    /// <summary>Open the role editor blank; on Save provision the new role.</summary>
    [RelayCommand(CanExecute = nameof(CanMutate))]
    private async Task AddRoleAsync()
    {
        if (!_main.IsConnected) { Status = "Connect first."; return; }
        var vm = await DialogHost.RoleEditorAsync(null, null, [], Permissions.ToList(), isEdit: false).ConfigureAwait(true);
        if (vm is null) return;
        await ProvisionRoleSpecAsync(vm.Name.Trim(), vm.Description, vm.SelectedPermissions, "Created").ConfigureAwait(true);
    }

    /// <summary>Open the role editor pre-filled; on Save provision the changes (upsert by name).</summary>
    [RelayCommand]
    private async Task EditRoleAsync(RoleListRow? row)
    {
        if (row is null || !_main.IsConnected) return;
        var vm = await DialogHost.RoleEditorAsync(row.Name, row.Description, row.Permissions, Permissions.ToList(), isEdit: true).ConfigureAwait(true);
        if (vm is null) return;
        await ProvisionRoleSpecAsync(row.Name, vm.Description, vm.SelectedPermissions, "Updated").ConfigureAwait(true);
    }

    private async Task ProvisionRoleSpecAsync(string name, string? description, IReadOnlyList<string> permissions, string verb)
    {
        Busy = true;
        Status = $"Saving role '{name}'…";
        try
        {
            var result = await _shell.ProvisionRoleAsync(new RoleProvisioning.RoleSpec(name, description, permissions)).ConfigureAwait(true);
            if (result.Errors.Count > 0)
            {
                Status = $"'{name}': {string.Join("; ", result.Errors)}";
                _log.Warn("Roles", Status);
            }
            else
            {
                _log.Success("Roles", $"{verb} role '{name}'.");
                Status = $"{verb} role '{name}'.";
            }
            MarkDataDirty();
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; _log.Error("Roles", ex.Message, ex); }
        finally { Busy = false; }
    }

    /// <summary>Delete a single role after an itemized confirmation prompt.</summary>
    [RelayCommand]
    private async Task DeleteRoleAsync(RoleListRow? row)
    {
        if (row is null || !_main.IsConnected) return;
        await ConfirmAndDeleteAsync(new[] { row.Name }).ConfigureAwait(true);
    }

    /// <summary>Delete every checked role after a single itemized confirmation prompt.</summary>
    [RelayCommand]
    private async Task DeleteSelectedRolesAsync()
    {
        var names = Selection.SelectedOf<RoleListRow>().Select(r => r.Name).ToList();
        if (names.Count == 0) { Status = "Select at least one role."; return; }
        await ConfirmAndDeleteAsync(names).ConfigureAwait(true);
    }

    private async Task ConfirmAndDeleteAsync(IReadOnlyList<string> names)
    {
        if (!_main.IsConnected) { Status = "Connect first."; return; }
        var ok = await DialogHost.ConfirmBulkAsync("Delete roles", "Delete", "role", names,
            _main.ConnectedEnv?.Name, _main.ConnectedEnv?.Stage ?? Models.EnvironmentStage.Unspecified).ConfigureAwait(true);
        if (!ok) return;
        await DeleteRolesAsync(names).ConfigureAwait(true);
    }

    private async Task DeleteRolesAsync(IReadOnlyList<string> names)
    {
        if (!_main.IsConnected) { Status = "Connect first."; return; }
        Busy = true;
        int deleted = 0, errors = 0;
        try
        {
            foreach (var name in names)
            {
                Status = $"Deleting role '{name}'…";
                var result = await _shell.DeleteRoleAsync(name).ConfigureAwait(true);
                if (result.Errors.Count > 0) { errors++; _log.Warn("Roles", $"{name}: {string.Join("; ", result.Errors)}"); }
                else { deleted++; _log.Success("Roles", $"Deleted role '{name}'."); }
            }
            Status = errors == 0 ? $"Deleted {deleted} role(s)." : $"Deleted {deleted}, {errors} failed.";
            MarkDataDirty();
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; _log.Error("Roles", ex.Message, ex); }
        finally { Busy = false; }
    }

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

/// <summary>Selectable grid row wrapping a <see cref="RoleSummary"/> for the Roles page.</summary>
public sealed partial class RoleListRow : SelectableRow
{
    public RoleListRow(RoleSummary source) => Source = source;
    public RoleSummary Source { get; }
    public string Name => Source.Name;
    public string Description => Source.Description;
    public IReadOnlyList<string> Permissions => Source.Permissions;
}
