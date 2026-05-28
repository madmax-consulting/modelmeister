using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Inriver.Apply;
using ModelMeister.Inriver.Diff;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// View-model for the Apply page. Drives both Dry-run and Apply against the connected env, with
/// a strict gate (confirmation dialog + pre-apply backup) in front of any real mutation.
/// </summary>
public partial class ApplyViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _main;
    private readonly Shell _shell;
    private readonly IFileOpener _fileOpener;
    private readonly IAppLog _log;
    private CancellationTokenSource? _cts;

    /// <summary>True while an apply/dry-run is in progress; disables buttons and shows the indeterminate bar.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DryRunCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(RetryFailedCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreFromLastBackupCommand))]
    private bool _busy;
    /// <summary>Short status line shown at the bottom of the page.</summary>
    [ObservableProperty] private string _statusMessage = "Run Compare first, then choose Dry-run or Apply.";
    /// <summary>True when there is a change set to apply and a connection to apply it to.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DryRunCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _canApply;
    /// <summary>Total number of changes in the effective set being applied.</summary>
    [ObservableProperty] private int _totalChanges;
    /// <summary>Number of changes processed so far (succeeded + failed).</summary>
    [ObservableProperty] private int _completed;
    /// <summary>Running count of successfully applied changes.</summary>
    [ObservableProperty] private int _succeeded;
    /// <summary>Running count of failed changes.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryFailedCommand))]
    private int _failed;
    /// <summary>Path to the receipt JSON from the most recent run.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenReceiptCommand))]
    private string? _lastReceiptPath;
    /// <summary>Path to the pre-apply backup JSON from the most recent run (real applies only).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RevealBackupCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreFromLastBackupCommand))]
    private string? _lastBackupPath;
    /// <summary>True when the most recent run was a dry-run.</summary>
    [ObservableProperty] private bool _lastDryRun;
    /// <summary>Estimated time remaining; updated as each entry completes.</summary>
    [ObservableProperty] private string _eta = "";
    /// <summary>Throughput display string ("12.3/s" or "8.0/min" depending on rate).</summary>
    [ObservableProperty] private string _ratePerSec = "";

    /// <summary>One of "All", "Failed", "Succeeded".</summary>
    [ObservableProperty] private string _filter = "All";

    /// <summary>Read-only echo of the policy actually in effect (sourced from DiffViewModel).</summary>
    [ObservableProperty] private string _policySummary = "";

    /// <summary>Full execution log (append-only). The view binds <see cref="FilteredEntries"/>.</summary>
    public ObservableCollection<ChangeReceiptEntry> Entries { get; } = [];
    /// <summary>Filtered view onto <see cref="Entries"/> driven by <see cref="Filter"/>.</summary>
    public ObservableCollection<ChangeReceiptEntry> FilteredEntries { get; } = [];

    public ApplyViewModel(MainWindowViewModel main, Shell shell, IFileOpener fileOpener, IAppLog log)
    {
        _main = main;
        _shell = shell;
        _fileOpener = fileOpener;
        _log = log;
        Entries.CollectionChanged += (_, _) => UpdateFiltered();

        // Mirror upstream state: when ChangeSet, LiveSnapshot, or IsConnected change on the hub,
        // re-run Refresh() so CanApply (and therefore the Dry-run/Apply buttons) is always honest
        // — not just on navigate-in. Without this, clearing the ChangeSet after a successful Apply
        // doesn't disable the button until the user re-navigates to the page.
        _main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainWindowViewModel.ChangeSet)
                                or nameof(MainWindowViewModel.LiveSnapshot)
                                or nameof(MainWindowViewModel.IsConnected))
                Refresh();
        };
    }

    /// <summary>Re-read effective changes from <see cref="DiffViewModel"/>. Called by the view on navigate-in.</summary>
    public void Refresh()
    {
        var effective = _main.DiffVm.EffectiveChanges();
        CanApply = effective.Count > 0 && _main.IsConnected;
        TotalChanges = effective.Count;
        PolicySummary = FormatPolicy(_main.PolicyVm);
    }

    private static string FormatPolicy(PolicyViewModel p)
    {
        static string Yn(bool b) => b ? "✓" : "✗";
        return $"Deletes {Yn(p.AllowDeletes)} · Datatype change {Yn(p.AllowDatatypeChange)} · " +
               $"Overwrite names {Yn(p.OverwriteNamesAndDescriptions)} · Overwrite CVL values {Yn(p.OverwriteCvlValues)} · " +
               $"CVL value rename {Yn(p.AllowCvlValueRename)}";
    }

    partial void OnFilterChanged(string value) => UpdateFiltered();

    private void UpdateFiltered()
    {
        FilteredEntries.Clear();
        var rows = Filter switch
        {
            "Failed"    => Entries.Where(e => !e.Succeeded),
            "Succeeded" => Entries.Where(e => e.Succeeded),
            _           => (IEnumerable<ChangeReceiptEntry>)Entries,
        };
        foreach (var e in rows) FilteredEntries.Add(e);
    }

    [RelayCommand] private void SetFilter(string filter) => Filter = filter ?? "All";

    private bool CanRunDryOrApply() => !Busy && CanApply;
    private bool CanRetryFailed() => !Busy && Failed > 0;
    private bool CanCancel() => Busy;
    private bool CanOpenReceipt() => LastReceiptPath is not null;
    private bool CanRevealOrRestoreBackup() => !Busy && LastBackupPath is not null;

    [RelayCommand(CanExecute = nameof(CanRunDryOrApply))]
    private Task DryRunAsync() => RunAsync(dryRun: true, retryOnlyFailed: false);

    [RelayCommand(CanExecute = nameof(CanRunDryOrApply))]
    private Task ApplyAsync() => RunAsync(dryRun: false, retryOnlyFailed: false);

    [RelayCommand(CanExecute = nameof(CanRetryFailed))]
    private Task RetryFailedAsync() => RunAsync(dryRun: false, retryOnlyFailed: true);

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand(CanExecute = nameof(CanOpenReceipt))]
    private void OpenReceipt()
    {
        if (LastReceiptPath is not null) _fileOpener.Open(LastReceiptPath);
    }

    [RelayCommand(CanExecute = nameof(CanRevealOrRestoreBackup))]
    private void RevealBackup()
    {
        if (LastBackupPath is not null) _fileOpener.RevealInExplorer(LastBackupPath);
    }

    [RelayCommand(CanExecute = nameof(CanRevealOrRestoreBackup))]
    private async Task RestoreFromLastBackupAsync()
    {
        if (LastBackupPath is null) return;
        await _main.RestoreFromBackupAsync(LastBackupPath);
    }

    [RelayCommand] private void GoToCompare() => _main.GoTo(NavTarget.Diff);

    private async Task RunAsync(bool dryRun, bool retryOnlyFailed)
    {
        if (_main.LoadedModel is null || _main.LiveSnapshot is null || _main.ChangeSet is null) return;
        if (!_main.IsConnected)
        {
            StatusMessage = "Not connected — connect to an environment first.";
            _log.Warn("Apply", "Apply aborted: not connected.");
            return;
        }

        // For real apply (non-dry-run), force confirmation via dialog.
        if (!dryRun && !await ConfirmRealApplyAsync(retryOnlyFailed)) return;

        var effective = BuildEffectiveSet(retryOnlyFailed);

        Busy = true;
        LastDryRun = dryRun;
        Entries.Clear();
        Completed = Succeeded = Failed = 0;
        TotalChanges = effective.Changes.Count;
        Eta = "";
        RatePerSec = "";
        _cts = new CancellationTokenSource();

        var modelDir = _main.ModelPath is { } p ? Path.GetDirectoryName(p) ?? Environment.CurrentDirectory : Environment.CurrentDirectory;
        var envUrl = _main.LiveSnapshot.EnvironmentUrl;
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");

        string? backupPath = null;
        if (!dryRun)
        {
            backupPath = Path.Combine(_shell.GetBackupsDir(envUrl, modelDir), $"{stamp}.model.json");
            if (!await WriteBackupOrAbortAsync(backupPath, _cts.Token)) return;
            LastBackupPath = backupPath;
        }

        var sw = Stopwatch.StartNew();
        var progress = new Progress<ChangeReceiptEntry>(entry =>
        {
            Entries.Add(entry);
            Completed = Entries.Count;
            if (entry.Succeeded) Succeeded++; else Failed++;
            UpdateEta(sw);
        });

        StatusMessage = dryRun ? "Dry-run executing…" : "Applying changes…";
        _log.Info("Apply", $"{(dryRun ? "Dry-run" : "Apply")} starting — {TotalChanges} change(s).");

        try
        {
            var receipt = await _shell.ApplyAsync(
                effective, _main.LoadedModel, _main.LiveSnapshot,
                dryRun, backupPath, progress, _cts.Token).ConfigureAwait(true);

            var receiptPath = Path.Combine(
                _shell.GetReceiptsDir(envUrl, modelDir),
                $"{stamp}{(dryRun ? ".dryrun.json" : ".json")}");
            await Task.Run(() => receipt.SaveTo(receiptPath), _cts.Token).ConfigureAwait(true);
            LastReceiptPath = receiptPath;

            var verb = dryRun ? "Dry-run" : "Apply";
            StatusMessage = dryRun
                ? $"{verb} complete. {receipt.Succeeded} succeeded, {receipt.Failed} failed. Receipt saved."
                : $"Apply complete: {receipt.Succeeded} ok, {receipt.Failed} failed. Compare cleared — re-run Compare to verify.";
            _log.Success("Apply",
                $"{verb} complete: {receipt.Succeeded} ok, {receipt.Failed} failed in {sw.Elapsed.TotalSeconds:0.0}s.");
            _log.Toast(receipt.Failed == 0 ? LogLevel.Success : LogLevel.Warn,
                dryRun ? "Dry-run complete" : "Apply complete",
                $"{receipt.Succeeded} succeeded · {receipt.Failed} failed",
                onClick: OpenReceipt);

            if (!dryRun) _main.NotifyApplyCompleted(dryRun: false);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Cancelled.";
            _log.Warn("Apply", "Cancelled by user.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed: {ex.Message}";
            _log.Error("Apply", $"Failed: {ex.Message}", ex);
            _log.Toast(LogLevel.Error, "Apply failed", ex.Message);
        }
        finally
        {
            Busy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task<bool> ConfirmRealApplyAsync(bool retryOnlyFailed)
    {
        // The exact changes that will be applied — either the diff's effective set, or just the
        // failed rows on a retry (re-matched to their ModelChange so danger/operation is accurate).
        IReadOnlyList<ModelChange> applying;
        if (retryOnlyFailed)
        {
            var failed = Entries.Where(e => !e.Succeeded).Select(e => e.Description).ToHashSet();
            applying = (_main.ChangeSet?.Changes ?? [])
                .Where(c => failed.Contains(c.Describe())).ToList();
        }
        else
        {
            applying = _main.DiffVm.EffectiveChanges();
        }

        if (applying.Count == 0)
        {
            StatusMessage = "Nothing to apply.";
            return false;
        }

        var review = applying
            .Select(c => new ApplyReviewItem(
                DiffViewModel.OperationOf(c), c.Describe(), DiffViewModel.IsDangerousChange(c)))
            .ToList();

        var confirmed = await DialogHost.ConfirmApplyAsync(
            _main.LiveSnapshot!.EnvironmentUrl,
            applying.Count,
            PolicySummary,
            _main.ConnectedStage,
            review);
        if (!confirmed) _log.Info("Apply", "User cancelled at confirmation.");
        return confirmed;
    }

    private ModelChangeSet BuildEffectiveSet(bool retryOnlyFailed)
    {
        var warnings = _main.ChangeSet!.Warnings;
        if (retryOnlyFailed)
        {
            var failedDescriptions = Entries.Where(e => !e.Succeeded).Select(e => e.Description).ToHashSet();
            var keep = _main.ChangeSet.Changes.Where(c => failedDescriptions.Contains(c.Describe())).ToList();
            return new ModelChangeSet { Changes = keep, Warnings = warnings };
        }

        var effective = _main.DiffVm.EffectiveChanges();
        return new ModelChangeSet { Changes = effective.ToList(), Warnings = warnings };
    }

    private async Task<bool> WriteBackupOrAbortAsync(string backupPath, CancellationToken ct)
    {
        try
        {
            StatusMessage = "Writing backup snapshot…";
            _log.Info("Apply", $"Writing backup snapshot {backupPath}");
            await _shell.SaveSnapshotAsync(_main.LiveSnapshot!, backupPath, ct).ConfigureAwait(true);
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Backup failed (aborting apply): {ex.Message}";
            _log.Error("Apply", $"Backup failed: {ex.Message}", ex);
            _log.Toast(LogLevel.Error, "Apply aborted", $"Backup snapshot failed: {ex.Message}");
            Busy = false;
            return false;
        }
    }

    private void UpdateEta(Stopwatch sw)
    {
        if (Completed == 0 || TotalChanges == 0) return;
        var elapsed = sw.Elapsed.TotalSeconds;
        if (elapsed < 0.001) return;

        var rate = Completed / elapsed;
        RatePerSec = rate >= 1 ? $"{rate:0.0}/s" : $"{rate * 60:0.0}/min";

        var remaining = TotalChanges - Completed;
        if (remaining <= 0) { Eta = "—"; return; }

        var etaSeconds = remaining / Math.Max(rate, 0.0001);
        Eta = etaSeconds < 60 ? $"ETA {etaSeconds:0}s" : $"ETA {etaSeconds / 60:0.0}m";
    }

    /// <summary>
    /// Wire an auto-scroll callback for the receipt grid. The view-model only knows about the
    /// data; the view supplies a delegate that knows how to scroll a particular DataGrid.
    /// </summary>
    public void RegisterAutoScroll(Action<int> scrollToIndex)
    {
        Entries.CollectionChanged += (_, _) =>
        {
            if (Entries.Count == 0) return;
            Dispatcher.UIThread.Post(() => scrollToIndex(FilteredEntries.Count - 1));
        };
    }
}
