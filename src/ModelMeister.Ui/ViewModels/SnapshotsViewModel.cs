using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Ui.Models;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// Central backup-library view-model. Lists every scoped + full backup the app has produced,
/// regardless of which feature created it, and restores any of them back into the connected
/// environment (per-row Restore → itemized destructive confirm → result table).
/// </summary>
public partial class SnapshotsViewModel : FeaturePageViewModel
{
    private readonly MainWindowViewModel _main;
    private readonly IFileOpener _fileOpener;
    private readonly IAppLog _log;

    /// <summary>All backup files in the library, filtered + sorted by capture time.</summary>
    public ObservableCollection<BackupRow> Rows { get; } = [];

    /// <summary>Checkbox multi-selection over <see cref="Rows"/> (header select-all + bulk delete).</summary>
    public RowSelectionModel Selection { get; }

    [ObservableProperty] private string _scopeFilter = "All";
    [ObservableProperty] private string _summary = "";
    /// <summary>True while a backup capture or delete is in flight. Disables both buttons.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CaptureFullCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    private bool _busy;

    public SnapshotsViewModel(MainWindowViewModel main, IFileOpener fileOpener, IAppLog log)
    {
        _main = main;
        _fileOpener = fileOpener;
        _log = log;
        Selection = new RowSelectionModel(Rows);
        _main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsConnected))
            {
                CaptureFullCommand.NotifyCanExecuteChanged();
                RestoreCommand.NotifyCanExecuteChanged();
            }
        };
        // Captures from elsewhere (Dashboard tile, Apply pre-capture, etc.) raise this — without it
        // the table only refreshed when the user captured from this page.
        _main.Backups.Changed += OnBackupsChanged;
        _ = EnsureLoadedAsync();
    }

    private void OnBackupsChanged() => Dispatcher.UIThread.Post(() =>
    {
        MarkDataDirty();
        _ = EnsureLoadedAsync();
    });

    private bool CanCaptureFull() => !Busy && _main.IsConnected;
    private bool CanDeleteRow(BackupRow? row) => !Busy && row is not null;
    private bool CanRestore(BackupRow? row) => !Busy && row is not null && _main.IsConnected;

    /// <inheritdoc/>
    public override BackupScope BackupScope => BackupScope.None; // page lists backups, doesn't produce them
    /// <inheritdoc/>
    public override ExcelCapability Excel => ExcelCapability.None;

    /// <inheritdoc/>
    public override Task RefreshAsync()
    {
        var scope = string.Equals(ScopeFilter, "All", StringComparison.OrdinalIgnoreCase) ? null : ScopeFilter;
        var list = _main.Backups.List(scope);
        Rows.Clear();
        foreach (var b in list)
            Rows.Add(new BackupRow(b));
        Summary = $"{Rows.Count} backups";
        return Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanCaptureFull))]
    private async Task CaptureFullAsync()
    {
        Busy = true;
        try
        {
            var path = await _main.Backups.CaptureFullAsync(includeModel: _main.LoadedModel is not null).ConfigureAwait(true);
            _log.Success("Backup", $"Full snapshot saved → {path}");
            _log.Toast(LogLevel.Success, "Full snapshot saved", Path.GetFileName(path));
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.Error("Backup", $"Full snapshot failed: {ex.Message}", ex);
            _log.Toast(LogLevel.Error, "Backup failed", ex.Message);
        }
        finally { Busy = false; }
    }

    /// <summary>
    /// Restore the selected backup into the connected environment. Lists the items, takes a single
    /// itemized + stage-aware destructive confirmation (the same prompt deletes use), applies via the
    /// shared <see cref="RestoreService"/>, then shows the per-item outcome in the same result dialog
    /// imports use. A Full snapshot's model slice is offered to the model Compare/Apply workflow.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRestore))]
    private async Task RestoreAsync(BackupRow? row)
    {
        if (row is null) return;
        if (!_main.IsConnected || _main.ConnectedEnv is null)
        {
            _log.Toast(LogLevel.Warn, "Restore", "Connect to the target environment first.");
            return;
        }
        var env = _main.ConnectedEnv;

        Busy = true;
        try
        {
            var items = await _main.Restores.DescribeItemsAsync(row.Info).ConfigureAwait(true);
            if (items.Count == 0)
            {
                Summary = "Nothing to restore in this backup.";
                _log.Toast(LogLevel.Warn, "Restore", "This backup is empty.");
                return;
            }

            // Restore overwrites live state — confirm against the connected env (Prod banner when Prod),
            // listing every item, exactly like a destructive delete.
            var ok = await DialogHost.ConfirmBulkAsync(
                $"Restore {row.Scope} to {env.Name}", "Restore", "item", items,
                env.Name, env.TypeKey, destructive: true).ConfigureAwait(true);
            if (!ok) return;

            Summary = $"Restoring {row.Scope} into '{env.Name}'…";
            var results = await _main.Restores.RestoreAsync(row.Info, env).ConfigureAwait(true);

            var resultRows = results.Select(r => new ProvisionResultRow(r.Name, r.Outcome, r.Detail)).ToList();
            var errors = results.Count(r => string.Equals(r.Outcome, "error", StringComparison.Ordinal));
            var resultVm = new ProvisionResultViewModel(
                dryRun: false,
                created: results.Count - errors,
                updated: 0,
                errors: errors,
                warnings: 0,
                rows: resultRows,
                importEyebrow: $"RESTORE · {row.Scope.ToUpperInvariant()}",
                keyColumnHeader: "Item",
                itemNoun: "items");
            await DialogHost.ShowProvisionResultAsync(resultVm).ConfigureAwait(true);

            Summary = errors == 0
                ? $"Restored {row.Scope}: {results.Count} item(s) into '{env.Name}'."
                : $"Restored {row.Scope}: {results.Count - errors} ok, {errors} failed.";
            _log.Success("Restore", Summary);
            _log.Toast(errors == 0 ? LogLevel.Success : LogLevel.Warn, "Restore complete", Summary);

            // A Full snapshot's model slice reverts via the diff/apply pipeline, not an upsert — offer it.
            var modelPath = _main.Restores.ModelSlicePath(row.Info);
            if (modelPath is not null)
            {
                var loadModel = await DialogHost.ConfirmAsync(
                    "Restore model slice?",
                    "This Full snapshot includes a model slice. Load it as a project and open Compare to review the reverse change set before applying?",
                    "Load model", "Skip").ConfigureAwait(true);
                if (loadModel) await _main.RestoreFromBackupAsync(modelPath).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            Summary = "Restore failed: " + ex.Message;
            _log.Error("Restore", ex.Message, ex);
            _log.Toast(LogLevel.Error, "Restore failed", ex.Message);
        }
        finally { Busy = false; }
    }

    [RelayCommand]
    private void OpenInExplorer(BackupRow? row)
    {
        if (row is null) return;
        _fileOpener.RevealInExplorer(row.Info.Path);
    }

    [RelayCommand]
    private Task CopyPath(BackupRow? row) => row is null ? Task.CompletedTask : ClipboardHelpers.CopyAsync(row.Info.Path);

    [RelayCommand(CanExecute = nameof(CanDeleteRow))]
    private void Delete(BackupRow? row)
    {
        if (row is null) return;
        Busy = true;
        try
        {
            DeleteFile(row);
            _main.Backups.RaiseChanged();
        }
        catch (Exception ex)
        {
            _log.Error("Backup", $"Delete failed: {ex.Message}", ex);
            _log.Toast(LogLevel.Error, "Delete failed", ex.Message);
        }
        finally { Busy = false; }
    }

    /// <summary>Delete every checked backup after a single confirmation prompt.</summary>
    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        var rows = Selection.SelectedOf<BackupRow>();
        if (rows.Count == 0) { Summary = "Select at least one backup."; return; }
        var ok = await DialogHost.ConfirmBulkAsync(
            "Delete backups", "Delete", "backup",
            rows.Select(r => r.FileName).ToList(),
            envName: null).ConfigureAwait(true);
        if (!ok) return;

        Busy = true;
        int deleted = 0, errors = 0;
        try
        {
            foreach (var row in rows)
            {
                try { DeleteFile(row); deleted++; }
                catch (Exception ex) { errors++; _log.Error("Backup", $"Delete failed: {ex.Message}", ex); }
            }
            Summary = errors == 0 ? $"Deleted {deleted} backup(s)." : $"Deleted {deleted}, {errors} failed.";
            _main.Backups.RaiseChanged();
        }
        finally { Busy = false; }
    }

    private void DeleteFile(BackupRow row)
    {
        if (row.Info.Kind == BackupKind.Folder)
            Directory.Delete(row.Info.Path, recursive: true);
        else
            File.Delete(row.Info.Path);
        _log.Info("Backup", $"Deleted {row.Info.Path}");
    }

    partial void OnScopeFilterChanged(string value) => _ = RefreshAsync();
}

/// <summary>One row in the Snapshots data grid. Wraps a <see cref="BackupFileInfo"/> for display.</summary>
public sealed partial class BackupRow : SelectableRow
{
    public BackupFileInfo Info { get; }
    public BackupRow(BackupFileInfo info) => Info = info;

    public string Scope => Info.Scope;
    public string EnvName => Info.EnvName;
    public string CapturedAt => Info.CapturedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string KindLabel => Info.Kind == BackupKind.Folder ? "folder" : "file";
    public string Size => Info.SizeBytes < 1024
        ? $"{Info.SizeBytes} B"
        : Info.SizeBytes < 1024 * 1024
            ? $"{Info.SizeBytes / 1024.0:F1} KB"
            : $"{Info.SizeBytes / (1024.0 * 1024):F1} MB";
    public string FileName => Path.GetFileName(Info.Path);
}
