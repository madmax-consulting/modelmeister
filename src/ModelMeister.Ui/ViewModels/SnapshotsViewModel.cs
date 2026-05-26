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
/// regardless of which feature created it. Restore and per-row Compare-with-current ship on
/// individual feature pages.
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
                CaptureFullCommand.NotifyCanExecuteChanged();
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
