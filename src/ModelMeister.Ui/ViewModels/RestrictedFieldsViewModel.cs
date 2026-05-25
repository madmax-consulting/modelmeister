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
    public override Task ExportExcelAsync() => DownloadListAsync();

    /// <inheritdoc/>
    public override async Task ImportExcelAsync()
    {
        if (!_main.IsConnected) { _log.Toast(LogLevel.Warn, "Import restricted fields", "Connect first."); return; }
        var vm = await DialogHost.ImportWorkbookAsync(
            "Import restricted-field permissions from workbook",
            "Add restricted-field permissions in the connected environment from an edited restricted-fields.xlsx. Existing rows are skipped.",
            "restricted-fields.xlsx").ConfigureAwait(true);
        if (vm is null) return;
        WorkbookPath = vm.WorkbookPath;
        DryRun = vm.DryRun;
        await ProvisionAsync().ConfigureAwait(true);
    }

    public ObservableCollection<RestrictedFieldSummary> Rows { get; } = [];

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

    public RestrictedFieldsViewModel(MainWindowViewModel main, Shell shell, IAppLog log)
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
            var rows = await _shell.ListRestrictedFieldsAsync().ConfigureAwait(true);
            Rows.Clear();
            foreach (var r in rows) Rows.Add(r);
            Status = $"{Rows.Count} restricted-field permissions";
        }
        catch (Exception ex)
        {
            Status = "Failed: " + ex.Message;
            _log.Error("RestrictedFields", ex.Message, ex);
        }
        finally { Busy = false; }
    }

    [RelayCommand] private Task CopyRole(RestrictedFieldSummary? r) => ClipboardHelpers.CopyAsync(r?.RoleName);
    [RelayCommand] private Task CopyKey(RestrictedFieldSummary? r) => ClipboardHelpers.CopyAsync(r?.NaturalKey);

    /// <summary>Delete a single restricted-field permission (after confirmation).</summary>
    [RelayCommand]
    public async Task DeleteAsync(RestrictedFieldSummary? row)
    {
        if (row is null) return;
        if (!_main.IsConnected) { Status = "Connect to an environment first."; return; }
        var confirmed = await DialogHost.ConfirmAsync(
            "Delete restricted-field permission",
            $"Delete the restriction for role '{row.RoleName}' ({row.RestrictionType})?",
            "Delete", "Cancel").ConfigureAwait(true);
        if (!confirmed) return;

        Busy = true;
        try
        {
            var result = await _shell.DeleteRestrictedFieldAsync(row.Id).ConfigureAwait(true);
            if (result.Errors.Count == 0)
            {
                Status = $"Deleted restriction for '{row.RoleName}'.";
                _log.Success("RestrictedFields", $"Deleted #{row.Id} ({row.RoleName}).");
            }
            else
            {
                Status = string.Join("; ", result.Errors);
                _log.Error("RestrictedFields", Status);
            }
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
                        RestrictionType = "ReadOnly",
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

    [RelayCommand(CanExecute = nameof(CanProvision))]
    public async Task ProvisionAsync()
    {
        if (!_main.IsConnected) { Status = "Connect to an environment first."; return; }
        if (string.IsNullOrEmpty(WorkbookPath) || !File.Exists(WorkbookPath)) { Status = "Pick a workbook."; return; }

        Busy = true;
        try
        {
            var fileRows = RestrictedFieldsWorkbook.Load(WorkbookPath);
            // Dedupe against what's already in the env (and within the file) — there is no update op.
            var existingKeys = (await _shell.ListRestrictedFieldsAsync().ConfigureAwait(true))
                .Select(r => r.NaturalKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            int added = 0, skipped = 0, errors = 0;
            var resultRows = new List<ProvisionResultRow>();

            foreach (var row in fileRows)
            {
                var key = RestrictedFieldProvisioning.NaturalKey(
                    row.RoleName, row.RestrictionType,
                    RestrictedFieldProvisioning.NullIfEmpty(row.EntityTypeId), RestrictedFieldProvisioning.NullIfEmpty(row.FieldTypeId), RestrictedFieldProvisioning.NullIfEmpty(row.CategoryId));

                if (existingKeys.Contains(key))
                {
                    skipped++;
                    resultRows.Add(new ProvisionResultRow(key, "skipped", "already exists"));
                    continue;
                }

                if (DryRun)
                {
                    added++;
                    existingKeys.Add(key);
                    resultRows.Add(new ProvisionResultRow(key, "would-create", $"role: {row.RoleName}"));
                    continue;
                }

                var result = await _shell.AddRestrictedFieldAsync(new RestrictedFieldProvisioning.RestrictedFieldSpec(
                    row.RoleName, row.RestrictionType,
                    RestrictedFieldProvisioning.NullIfEmpty(row.EntityTypeId), RestrictedFieldProvisioning.NullIfEmpty(row.FieldTypeId), RestrictedFieldProvisioning.NullIfEmpty(row.CategoryId))).ConfigureAwait(true);
                if (result.Errors.Count > 0)
                {
                    errors += result.Errors.Count;
                    resultRows.Add(new ProvisionResultRow(key, "error", string.Join(" · ", result.Errors)));
                    foreach (var err in result.Errors) _log.Warn("RestrictedFields", $"{key}: {err}");
                }
                else
                {
                    added++;
                    existingKeys.Add(key);
                    resultRows.Add(new ProvisionResultRow(key, "created", $"role: {row.RoleName}"));
                }
            }

            var resultVm = new ProvisionResultViewModel(
                dryRun: DryRun,
                created: added,
                updated: skipped,
                errors: errors,
                warnings: 0,
                rows: resultRows,
                importEyebrow: "RESTRICTED FIELDS IMPORT");
            await DialogHost.ShowProvisionResultAsync(resultVm).ConfigureAwait(true);

            Status = DryRun
                ? $"Dry run complete · {added} would be added, {skipped} already present."
                : $"Import complete · added {added}, skipped {skipped}, errors {errors}";
            if (!DryRun) MarkDataDirty();
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; _log.Error("RestrictedFields", ex.Message, ex); }
        finally { Busy = false; }
    }

}
