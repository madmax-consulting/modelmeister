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
/// Restricted-field permissions page view-model. Lists field restrictions in the connected env, lets
/// the user export/import a workbook, and add/delete restrictions. Restricted-field permissions have
/// no update operation, so import is add-only (rows whose natural key already exists are skipped) and
/// the only in-place edit is delete.
/// </summary>
public partial class RestrictedFieldsViewModel : FeaturePageViewModel
{
    readonly MainWindowViewModel _main;
    readonly Shell _shell;
    readonly IAppLog _log;

    /// <inheritdoc/>
    public override bool SupportsCompare => true;
    /// <inheritdoc/>
    public override BackupScope BackupScope => BackupScope.RestrictedFields;
    /// <inheritdoc/>
    public override ExcelCapability Excel => ExcelCapability.ExportImport;

    /// <inheritdoc/>
    public override async Task BackupAsync()
    {
        if (!_main.IsConnected) { _log.Toast(LogLevel.Warn, "Backup", "Connect first."); return; }
        try
        {
            var path = await _main.Backups.CaptureRestrictedFieldsAsync().ConfigureAwait(true);
            _log.Success("Backup", $"Restricted-fields backup saved → {path}");
            _log.Toast(LogLevel.Success, "Restricted-fields backup saved", Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            _log.Error("Backup", $"Restricted-fields backup failed: {ex.Message}", ex);
            _log.Toast(LogLevel.Error, "Backup failed", ex.Message);
        }
    }

    /// <inheritdoc/>
    public override bool HasExcelTemplate => true;

    /// <inheritdoc/>
    public override Task ExportExcelAsync() => DownloadListAsync();

    /// <inheritdoc/>
    public override Task ExportTemplateAsync() => DownloadTemplateAsync();

    /// <inheritdoc/>
    public override async Task ImportExcelAsync()
    {
        if (!_main.IsConnected) { _log.Toast(LogLevel.Warn, "Import restricted fields", "Connect first."); return; }
        var plan = new ModelMeister.Ui.Services.Import.Plans.RestrictedFieldsImportPlan(_main, _shell, _log);
        var ran = await DialogHost.ShowImportWorkflowAsync(
            plan, _log, _main.Settings.Current.RecentWorkbookPaths).ConfigureAwait(true);
        if (!ran) return;
        RememberWorkbook(_main.Settings, plan.LastWorkbookPath);
        MarkDataDirty();
        await RefreshAsync().ConfigureAwait(true);
    }

    public ObservableCollection<RestrictedFieldListRow> Rows { get; } = [];

    /// <summary>Checkbox multi-selection over <see cref="Rows"/> (header select-all + shift-click + bulk delete).</summary>
    public RowSelectionModel Selection { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadListCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadTemplateCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private bool _busy;
    [ObservableProperty] private string _status = "";

    public RestrictedFieldsViewModel(MainWindowViewModel main, Shell shell, IAppLog log)
    {
        _main = main;
        _shell = shell;
        _log = log;
        Selection = new RowSelectionModel(Rows);
        _main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsConnected))
            {
                MarkDataDirty();
                if (_main.IsConnected) _ = EnsureLoadedAsync();
                DownloadListCommand.NotifyCanExecuteChanged();
                DownloadTemplateCommand.NotifyCanExecuteChanged();
                AddCommand.NotifyCanExecuteChanged();
            }
        };
    }

    private bool CanExport() => !Busy && _main.IsConnected;

    /// <inheritdoc/>
    public override async Task RefreshAsync()
    {
        if (!_main.IsConnected) { Status = "Connect to an environment first."; return; }
        Busy = true;
        try
        {
            var rows = await _shell.ListRestrictedFieldsAsync().ConfigureAwait(true);
            Rows.Clear();
            foreach (var r in rows) Rows.Add(new RestrictedFieldListRow(r));
            Status = $"{Rows.Count} restricted-field permissions";
        }
        catch (Exception ex)
        {
            Status = "Failed: " + ex.Message;
            _log.Error("RestrictedFields", ex.Message, ex);
        }
        finally { Busy = false; }
    }

    [RelayCommand] private Task CopyRole(RestrictedFieldListRow? r) => ClipboardHelpers.CopyAsync(r?.RoleName);
    [RelayCommand] private Task CopyKey(RestrictedFieldListRow? r) => ClipboardHelpers.CopyAsync(r?.NaturalKey);

    private bool CanMutate() => !Busy && _main.IsConnected;

    /// <summary>Open the add-restriction editor; on Add provision a single restricted-field permission.
    /// Restricted fields have no update op, so this is add-only and dedupes against the live set by
    /// natural key (same identity the import path uses).</summary>
    [RelayCommand(CanExecute = nameof(CanMutate))]
    private async Task AddAsync()
    {
        if (!_main.IsConnected) { Status = "Connect first."; return; }

        IReadOnlyList<string> roleNames;
        Busy = true;
        Status = "Loading roles…";
        try { roleNames = (await _shell.ListRolesAsync().ConfigureAwait(true)).Select(r => r.Name).ToList(); }
        catch (Exception ex) { Status = "Failed to load roles: " + ex.Message; _log.Error("RestrictedFields", ex.Message, ex); return; }
        finally { Busy = false; }

        var vm = await DialogHost.RestrictedFieldEditorAsync(roleNames).ConfigureAwait(true);
        if (vm is null || string.IsNullOrWhiteSpace(vm.SelectedRole)) return;

        var role = vm.SelectedRole!;
        var entity = RestrictedFieldProvisioning.NullIfEmpty(vm.EntityTypeId);
        var field = RestrictedFieldProvisioning.NullIfEmpty(vm.FieldTypeId);
        var category = RestrictedFieldProvisioning.NullIfEmpty(vm.CategoryId);

        var key = RestrictedFieldProvisioning.NaturalKey(role, vm.RestrictionType, entity, field, category);
        if (Rows.Any(r => string.Equals(r.NaturalKey, key, StringComparison.OrdinalIgnoreCase)))
        {
            Status = "That restriction already exists.";
            return;
        }

        Busy = true;
        Status = $"Adding restriction for '{role}'…";
        try
        {
            var result = await _shell.AddRestrictedFieldAsync(new RestrictedFieldProvisioning.RestrictedFieldSpec(
                role, vm.RestrictionType, entity, field, category)).ConfigureAwait(true);
            if (result.Errors.Count > 0)
            {
                Status = string.Join("; ", result.Errors);
                _log.Warn("RestrictedFields", $"{key}: {Status}");
            }
            else
            {
                Status = $"Added restriction for '{role}'.";
                _log.Success("RestrictedFields", Status);
            }
            MarkDataDirty();
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; _log.Error("RestrictedFields", ex.Message, ex); }
        finally { Busy = false; }
    }

    /// <summary>Delete a single restricted-field permission (after confirmation).</summary>
    [RelayCommand]
    public async Task DeleteAsync(RestrictedFieldListRow? row)
    {
        if (row is null) return;
        if (!_main.IsConnected) { Status = "Connect to an environment first."; return; }
        await ConfirmAndDeleteRowsAsync(new[] { row }).ConfigureAwait(true);
    }

    /// <summary>Delete every checked restricted-field permission after a single itemized prompt.</summary>
    [RelayCommand]
    public async Task DeleteSelectedAsync()
    {
        if (!_main.IsConnected) { Status = "Connect to an environment first."; return; }
        var rows = Selection.SelectedOf<RestrictedFieldListRow>();
        if (rows.Count == 0) { Status = "Select at least one restriction."; return; }
        await ConfirmAndDeleteRowsAsync(rows).ConfigureAwait(true);
    }

    private async Task ConfirmAndDeleteRowsAsync(IReadOnlyList<RestrictedFieldListRow> rows)
    {
        var names = rows.Select(r => $"{r.RoleName} · {r.NaturalKey} ({r.RestrictionType})").ToList();
        var confirmed = await DialogHost.ConfirmBulkAsync(
            "Delete restricted-field permissions", "Delete", "restriction", names,
            _main.ConnectedEnv?.Name, _main.ConnectedEnv?.TypeKey).ConfigureAwait(true);
        if (!confirmed) return;
        await DeleteRowsAsync(rows).ConfigureAwait(true);
    }

    private async Task DeleteRowsAsync(IReadOnlyList<RestrictedFieldListRow> rows)
    {
        if (!_main.IsConnected) { Status = "Connect to an environment first."; return; }
        Busy = true;
        int deleted = 0, errors = 0;
        try
        {
            await RunBulkAsync(rows,
                async row =>
                {
                    var result = await _shell.DeleteRestrictedFieldAsync(row.Id).ConfigureAwait(false);
                    if (result.Errors.Count == 0)
                    {
                        deleted++;
                        _log.Success("RestrictedFields", $"Deleted #{row.Id} ({row.RoleName}).");
                    }
                    else
                    {
                        errors++;
                        _log.Error("RestrictedFields", $"#{row.Id} ({row.RoleName}): {string.Join("; ", result.Errors)}");
                    }
                },
                (i, total, row) => Status = $"Deleting restriction {i} / {total} ({row.RoleName})…").ConfigureAwait(true);
            Status = errors == 0
                ? (deleted == 1 ? $"Deleted restriction for '{rows[0].RoleName}'." : $"Deleted {deleted} restriction(s).")
                : $"Deleted {deleted}, {errors} failed.";
            MarkDataDirty();
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; _log.Error("RestrictedFields", ex.Message, ex); }
        finally { Busy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    public async Task DownloadListAsync() => await ExportWorkbookAsync(seedSingleExample: false).ConfigureAwait(true);

    [RelayCommand(CanExecute = nameof(CanExport))]
    public async Task DownloadTemplateAsync() => await ExportWorkbookAsync(seedSingleExample: true).ConfigureAwait(true);

    private async Task ExportWorkbookAsync(bool seedSingleExample)
    {
        if (!_main.IsConnected) { Status = "Connect to an environment first."; return; }
        var path = await FilePickerHelpers.PickSaveAsync(
            "Save restricted-fields workbook",
            seedSingleExample ? "restricted-fields-template.xlsx" : "restricted-fields.xlsx",
            "xlsx").ConfigureAwait(true);
        if (path is null) return;

        Busy = true;
        try
        {
            var roleNames = (await _shell.ListRolesAsync().ConfigureAwait(true)).Select(r => r.Name).ToList();
            List<RestrictedFieldsWorkbook.RestrictedFieldRow> rows;
            if (seedSingleExample)
            {
                rows = new List<RestrictedFieldsWorkbook.RestrictedFieldRow>
                {
                    new()
                    {
                        RoleName = roleNames.FirstOrDefault() ?? "Example Role",
                        RestrictionType = "Readonly",
                        EntityTypeId = "ExampleEntityTypeId",
                        FieldTypeId = "ExampleFieldTypeId",
                    },
                };
            }
            else
            {
                var current = await _shell.ListRestrictedFieldsAsync().ConfigureAwait(true);
                rows = current.Select(r => new RestrictedFieldsWorkbook.RestrictedFieldRow
                {
                    RoleName = r.RoleName,
                    RestrictionType = r.RestrictionType,
                    EntityTypeId = r.EntityTypeId ?? "",
                    FieldTypeId = r.FieldTypeId ?? "",
                    CategoryId = r.CategoryId ?? "",
                }).ToList();
            }
            RestrictedFieldsWorkbook.Save(rows, roleNames, path);
            Status = $"Wrote {Path.GetFileName(path)}";
            _log.Success("RestrictedFields", $"Exported {(seedSingleExample ? "template" : "list")}: {path}");
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; _log.Error("RestrictedFields", ex.Message, ex); }
        finally { Busy = false; }
    }


}

/// <summary>Selectable grid row wrapping a <see cref="RestrictedFieldSummary"/> for the Restricted Fields page.</summary>
public sealed partial class RestrictedFieldListRow : SelectableRow
{
    public RestrictedFieldListRow(RestrictedFieldSummary source) => Source = source;
    public RestrictedFieldSummary Source { get; }

    /// <summary>Env-specific live id consumed by <see cref="RestrictedFieldsViewModel.DeleteAsync"/>.</summary>
    public int Id => Source.Id;
    public string RoleName => Source.RoleName;
    public string RestrictionType => Source.RestrictionType;
    public string? EntityTypeId => Source.EntityTypeId;
    public string? FieldTypeId => Source.FieldTypeId;
    public string? CategoryId => Source.CategoryId;
    public string NaturalKey => Source.NaturalKey;
}
