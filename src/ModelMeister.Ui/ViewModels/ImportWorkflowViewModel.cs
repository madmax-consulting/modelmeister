using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Ui.Services;
using ModelMeister.Ui.Services.Import;

namespace ModelMeister.Ui.ViewModels;

/// <summary>The stages of the unified Excel-import workflow, in order.</summary>
public enum ImportStep
{
    /// <summary>Pick the workbook file + set the abort-on-error option.</summary>
    ChooseFile,
    /// <summary>Loaded + diffed against the env; shows the per-row preview.</summary>
    Verify,
    /// <summary>Transient gate shown only when the import would remove items.</summary>
    ConfirmRemovals,
    /// <summary>Rows are being applied with live progress.</summary>
    Import,
    /// <summary>Final summary.</summary>
    Results,
}

/// <summary>One chip on the import workflow's step rail.</summary>
public sealed partial class ImportStepItem : ObservableObject
{
    public required ImportStep Step { get; init; }
    public required string Title { get; init; }
    [ObservableProperty] private bool _isDone;
    [ObservableProperty] private bool _isCurrent;
    [ObservableProperty] private bool _hasSeparator;
}

/// <summary>
/// Shared view-model behind the single Excel-import popup. Owns the step flow
/// (ChooseFile → Verify → [ConfirmRemovals] → Import → Results), live per-row progress, mid-run
/// cancellation, the abort-on-first-error option, the automatic pre-run backup, and the running
/// status line. Feature-specific load/verify/backup/apply is delegated to an <see cref="IImportPlan"/>.
/// Mirrors <see cref="ApplyViewModel"/>'s execution shape (Busy + CTS + counts + rate/ETA + backup-or-abort).
/// </summary>
public partial class ImportWorkflowViewModel : ViewModelBase
{
    private readonly IImportPlan _plan;
    private readonly IAppLog _log;
    private readonly IFileOpener _fileOpener;
    private readonly IImportConfirmGate _confirmGate;
    private CancellationTokenSource? _cts;
    private Action<int>? _scrollToIndex;

    // Destructive-removal gate data, filled by Verify.
    private string? _destructiveTitle, _destructiveVerb, _destructiveNoun;
    private IReadOnlyList<string>? _destructiveItems;
    private bool _ranToResults;

    public ImportWorkflowViewModel(
        IImportPlan plan, IAppLog log, IFileOpener fileOpener, IImportConfirmGate confirmGate,
        IReadOnlyList<string>? recents = null)
    {
        _plan = plan;
        _log = log;
        _fileOpener = fileOpener;
        _confirmGate = confirmGate;
        if (recents is not null)
            foreach (var p in recents) Recents.Add(p);
        RebuildSteps(hasRemovals: false);
    }

    // ----- static labels from the plan (bound by the view) -----
    public string Eyebrow => _plan.Metadata.Eyebrow;
    public string Title => _plan.Metadata.Title;
    public string Subtitle => _plan.Metadata.Subtitle;
    public string ItemNoun => _plan.Metadata.ItemNoun;
    public string KeyColumnHeader => _plan.Metadata.KeyColumnHeader;

    // ----- step + status -----
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartImportCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private ImportStep _currentStep = ImportStep.ChooseFile;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartImportCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    private bool _busy;

    [ObservableProperty] private string _statusMessage = "Pick a workbook to import.";

    /// <summary>True on the Import + Results steps, which share the live progress grid.</summary>
    public bool ShowRunPanel => CurrentStep is ImportStep.Import or ImportStep.Results;

    partial void OnCurrentStepChanged(ImportStep value) => OnPropertyChanged(nameof(ShowRunPanel));

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartImportCommand))]
    [NotifyPropertyChangedFor(nameof(InvalidBlocksImport))]
    private bool _abortOnFirstError;

    /// <summary>Rail chips, rebuilt when removals are detected.</summary>
    public ObservableCollection<ImportStepItem> Steps { get; } = [];

    // ----- choose file -----
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    private string? _workbookPath;

    public ObservableCollection<string> Recents { get; } = [];
    public bool HasRecents => Recents.Count > 0;

    // ----- verify summary -----
    [ObservableProperty] private int _verifyCreate;
    [ObservableProperty] private int _verifyUpdate;
    [ObservableProperty] private int _verifySkip;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInvalid))]
    [NotifyPropertyChangedFor(nameof(InvalidBlocksImport))]
    private int _verifyInvalid;

    public bool HasInvalid => VerifyInvalid > 0;
    /// <summary>True when invalid rows exist AND abort-on-first-error is on — we block the run and ask
    /// the user to fix the rows or turn the option off (help avoid mistakes).</summary>
    public bool InvalidBlocksImport => VerifyInvalid > 0 && AbortOnFirstError;

    /// <summary>All categorised rows from Verify (mutated live during Import).</summary>
    public ObservableCollection<ImportRowViewModel> Rows { get; } = [];
    /// <summary>Filtered view onto <see cref="Rows"/> driven by <see cref="Filter"/>.</summary>
    public ObservableCollection<ImportRowViewModel> FilteredRows { get; } = [];

    // ----- import progress -----
    [ObservableProperty] private int _total;
    [ObservableProperty] private int _completed;
    [ObservableProperty] private int _created;
    [ObservableProperty] private int _updated;
    [ObservableProperty] private int _skipped;
    [ObservableProperty] private int _failed;
    [ObservableProperty] private string _ratePerSec = "";
    [ObservableProperty] private string _eta = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RevealBackupCommand))]
    [NotifyPropertyChangedFor(nameof(HasBackup))]
    private string? _backupPath;
    public bool HasBackup => !string.IsNullOrEmpty(BackupPath);

    [ObservableProperty] private string _filter = "All";

    /// <summary>Rows that will actually be applied (will-create + will-update).</summary>
    public int Applicable => VerifyCreate + VerifyUpdate;
    private bool _verified;

    // ----- dialog plumbing -----
    public bool? Result { get; private set; }
    public event Action? Closed;

    partial void OnFilterChanged(string value) => UpdateFiltered();

    private void UpdateFiltered()
    {
        FilteredRows.Clear();
        IEnumerable<ImportRowViewModel> rows = Filter switch
        {
            "Failed"   => Rows.Where(r => r.State == RowRunState.Failed),
            "Created"  => Rows.Where(r => r.State is RowRunState.Created),
            "Updated"  => Rows.Where(r => r.State is RowRunState.Updated),
            "Skipped"  => Rows.Where(r => r.State is RowRunState.Skipped or RowRunState.Cancelled),
            _          => Rows,
        };
        foreach (var r in rows) FilteredRows.Add(r);
    }

    [RelayCommand] private void SetFilter(string? filter) => Filter = filter ?? "All";

    // ===================== Choose file =====================

    [RelayCommand]
    private async Task PickFileAsync()
    {
        var w = MainWindowOrNull();
        if (w is null) return;
        var picks = await w.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = _plan.Metadata.Title,
            FileTypeFilter = new[] { new FilePickerFileType("Excel") { Patterns = new[] { "*.xlsx" } } },
        }).ConfigureAwait(true);
        if (picks.Count == 0) return;
        WorkbookPath = picks[0].TryGetLocalPath();
    }

    [RelayCommand] private void PickRecent(string? path) { if (!string.IsNullOrWhiteSpace(path)) WorkbookPath = path; }

    private bool CanVerify() => !Busy && CurrentStep == ImportStep.ChooseFile
                                && !string.IsNullOrEmpty(WorkbookPath) && File.Exists(WorkbookPath);

    [RelayCommand(CanExecute = nameof(CanVerify))]
    private async Task VerifyAsync()
    {
        Busy = true;
        StatusMessage = "Verifying workbook against the environment…";
        try
        {
            _cts = new CancellationTokenSource();
            var path = WorkbookPath!;
            var result = await Task.Run(() => _plan.LoadAndVerifyAsync(path, _cts.Token), _cts.Token).ConfigureAwait(true);

            Rows.Clear();
            foreach (var r in result.Rows) Rows.Add(r);
            VerifyCreate = result.WillCreate;
            VerifyUpdate = result.WillUpdate;
            VerifySkip = result.WillSkip;
            VerifyInvalid = result.Invalid;
            OnPropertyChanged(nameof(Applicable));

            _destructiveTitle = result.DestructiveConfirmTitle;
            _destructiveVerb = result.DestructiveVerb;
            _destructiveNoun = result.DestructiveNoun;
            _destructiveItems = result.DestructiveItems;
            var hasRemovals = result.DestructiveItems is { Count: > 0 };

            _verified = true;
            CurrentStep = ImportStep.Verify;
            RebuildSteps(hasRemovals);
            UpdateFiltered();
            StatusMessage = $"Verified · {result.WillCreate} to create, {result.WillUpdate} to update, "
                          + $"{result.WillSkip} unchanged, {result.Invalid} invalid.";
            StartImportCommand.NotifyCanExecuteChanged();
        }
        catch (OperationCanceledException) { StatusMessage = "Verify cancelled."; }
        catch (Exception ex)
        {
            StatusMessage = $"Verify failed: {ex.Message}";
            _log.Error("Import", $"Verify failed: {ex.Message}", ex);
        }
        finally { Busy = false; _cts?.Dispose(); _cts = null; }
    }

    // ===================== Navigation =====================

    private bool CanGoBack() => !Busy && CurrentStep == ImportStep.Verify;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        CurrentStep = ImportStep.ChooseFile;
        RebuildSteps(hasRemovals: false);
        StatusMessage = "Pick a workbook to import.";
    }

    // ===================== Import =====================

    private bool CanStartImport() =>
        !Busy && CurrentStep == ImportStep.Verify && _verified && Applicable > 0 && !InvalidBlocksImport;

    [RelayCommand(CanExecute = nameof(CanStartImport))]
    private async Task StartImportAsync()
    {
        // Destructive-removal confirmation gate (e.g. CVL value removals).
        if (_destructiveItems is { Count: > 0 })
        {
            CurrentStep = ImportStep.ConfirmRemovals;
            RebuildSteps(hasRemovals: true);
            var ok = await _confirmGate.ConfirmDestructiveAsync(
                _destructiveTitle ?? "Apply import", _destructiveVerb ?? "Remove",
                _destructiveNoun ?? "item", _destructiveItems).ConfigureAwait(true);
            if (!ok)
            {
                CurrentStep = ImportStep.Verify;
                RebuildSteps(hasRemovals: true);
                StatusMessage = "Import cancelled — returned to Verify.";
                return;
            }
        }

        _cts = new CancellationTokenSource();
        Busy = true;
        Created = Updated = Skipped = Failed = Completed = 0;
        Eta = ""; RatePerSec = "";
        Filter = "All";
        CurrentStep = ImportStep.Import;
        RebuildSteps(_destructiveItems is { Count: > 0 });
        UpdateFiltered();

        // Automatic backup before any write — abort the import if it fails.
        StatusMessage = "Writing a backup before importing…";
        try
        {
            BackupPath = await _plan.BackupAsync(_cts.Token).ConfigureAwait(true);
            if (BackupPath is not null) _log.Info("Import", $"Backup saved → {BackupPath}");
        }
        catch (OperationCanceledException) { StatusMessage = "Cancelled before importing."; FinishRun(); return; }
        catch (Exception ex)
        {
            StatusMessage = $"Backup failed — import aborted: {ex.Message}";
            _log.Error("Import", StatusMessage, ex);
            _log.Toast(LogLevel.Error, "Import aborted", $"Backup failed: {ex.Message}");
            FinishRun();
            return;
        }

        var applicable = Rows.Where(r => r.PlanKind is RowPlanKind.WillCreate or RowPlanKind.WillUpdate).ToList();
        Total = applicable.Count;
        var sw = Stopwatch.StartNew();
        var aborted = false;
        StatusMessage = $"Importing {Total} {ItemNoun}…";
        _log.Info("Import", $"Import starting — {Total} row(s).");

        try
        {
            foreach (var row in applicable)
            {
                _cts.Token.ThrowIfCancellationRequested();
                row.State = RowRunState.Running;
                StatusMessage = $"Importing '{row.Key}' ({Completed + 1} / {Total})…";
                _scrollToIndex?.Invoke(FilteredRows.IndexOf(row));

                var outcome = await _plan.ApplyRowAsync(row, _cts.Token).ConfigureAwait(true);
                row.State = outcome.State;
                row.ResultDetail = outcome.Detail;
                row.Error = outcome.Error;
                Completed++;
                switch (outcome.State)
                {
                    case RowRunState.Created: Created++; break;
                    case RowRunState.Updated: Updated++; break;
                    case RowRunState.Skipped: Skipped++; break;
                    case RowRunState.Failed:
                        Failed++;
                        _log.Warn("Import", $"{row.Key}: {outcome.Error}");
                        break;
                }
                UpdateEta(sw);

                if (outcome.State == RowRunState.Failed && AbortOnFirstError)
                {
                    aborted = true;
                    StatusMessage = $"Aborted on first error at '{row.Key}'.";
                    break;
                }
            }

            if (!aborted)
                StatusMessage = $"Import complete · created {Created}, updated {Updated}, skipped {Skipped}"
                              + (Failed > 0 ? $", {Failed} failed" : "");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = $"Cancelled · {Completed} of {Total} processed.";
            _log.Warn("Import", StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import failed: {ex.Message}";
            _log.Error("Import", ex.Message, ex);
        }
        finally
        {
            _log.Toast(Failed == 0 ? LogLevel.Success : LogLevel.Warn,
                Failed == 0 ? "Import complete" : "Import finished with errors",
                $"created {Created} · updated {Updated} · skipped {Skipped} · failed {Failed}");
            FinishRun();
        }
    }

    /// <summary>Common teardown for the run: stop busy, dispose the CTS, land on Results.</summary>
    private void FinishRun()
    {
        Busy = false;
        _cts?.Dispose();
        _cts = null;
        _ranToResults = true;
        CurrentStep = ImportStep.Results;
        RebuildSteps(_destructiveItems is { Count: > 0 });
        UpdateFiltered();
    }

    private bool CanCancel() => Busy && CurrentStep == ImportStep.Import;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cts?.Cancel();
        StatusMessage = "Cancelling…";
    }

    // ===================== Results / close =====================

    private bool CanRevealBackup() => HasBackup;
    [RelayCommand(CanExecute = nameof(CanRevealBackup))]
    private void RevealBackup() { if (BackupPath is not null) _fileOpener.RevealInExplorer(BackupPath); }

    private bool CanClose() => !Busy;
    [RelayCommand(CanExecute = nameof(CanClose))]
    private void Close()
    {
        Result = _ranToResults;
        Closed?.Invoke();
    }

    // ===================== helpers =====================

    /// <summary>Wire an auto-scroll callback for the import grid (view supplies the delegate).</summary>
    public void RegisterAutoScroll(Action<int> scrollToIndex) => _scrollToIndex = scrollToIndex;

    /// <summary>Rebuild the rail. <paramref name="hasRemovals"/> inserts the ConfirmRemovals chip.</summary>
    private void RebuildSteps(bool hasRemovals)
    {
        var seq = new List<(ImportStep step, string title)>
        {
            (ImportStep.ChooseFile, "CHOOSE FILE"),
            (ImportStep.Verify, "VERIFY"),
        };
        if (hasRemovals) seq.Add((ImportStep.ConfirmRemovals, "CONFIRM"));
        seq.Add((ImportStep.Import, "IMPORT"));
        seq.Add((ImportStep.Results, "RESULTS"));

        var currentIdx = seq.FindIndex(s => s.step == CurrentStep);
        if (currentIdx < 0) currentIdx = 0;

        Steps.Clear();
        for (var i = 0; i < seq.Count; i++)
        {
            Steps.Add(new ImportStepItem
            {
                Step = seq[i].step,
                Title = seq[i].title,
                IsDone = i < currentIdx,
                IsCurrent = i == currentIdx,
                HasSeparator = i < seq.Count - 1,
            });
        }
    }

    // Copied from ApplyViewModel so the rate/ETA display is identical across the two run pages.
    private void UpdateEta(Stopwatch sw)
    {
        if (Completed == 0 || Total == 0) return;
        var elapsed = sw.Elapsed.TotalSeconds;
        if (elapsed < 0.001) return;

        var rate = Completed / elapsed;
        RatePerSec = rate >= 1 ? $"{rate:0.0}/s" : $"{rate * 60:0.0}/min";

        var remaining = Total - Completed;
        if (remaining <= 0) { Eta = "—"; return; }

        var etaSeconds = remaining / Math.Max(rate, 0.0001);
        Eta = etaSeconds < 60 ? $"ETA {etaSeconds:0}s" : $"ETA {etaSeconds / 60:0.0}m";
    }

    private static Window? MainWindowOrNull()
        => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d ? d.MainWindow : null;
}
