using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Scaffolder;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// View-model for the Tools page. Bundles the scaffolding utilities (from JSON, environment,
/// or Excel), JSON merge, and snapshot/Excel exports.
/// </summary>
public partial class ToolsViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _main;
    private readonly Shell _shell;
    private readonly IFileOpener _fileOpener;
    private readonly IAppLog _log;
    private CancellationTokenSource? _cts;

    /// <summary>True while any tool is in flight; disables actions and shows the indeterminate bar.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScaffoldCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnvScaffoldCommand))]
    [NotifyCanExecuteChangedFor(nameof(MergeCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExcelScaffoldCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExcelExportCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _busy;

    private bool NotBusy() => !Busy;
    private bool BusyNow() => Busy;
    /// <summary>Status line at the bottom of the page; reused by every action.</summary>
    [ObservableProperty] private string _statusMessage = "";

    // ----- Scaffold from JSON -----
    /// <summary>Path to the source inriver JSON export.</summary>
    [ObservableProperty] private string? _scaffoldJsonPath;
    /// <summary>Destination directory for the scaffolded project.</summary>
    [ObservableProperty] private string? _scaffoldOutputDir;
    /// <summary>Root namespace baked into the scaffolded csproj.</summary>
    [ObservableProperty] private string _scaffoldNamespace = "Acme.PimModel";
    /// <summary>Whether to factor out shared field-sets into abstract base classes.</summary>
    [ObservableProperty] private bool _scaffoldDetectBaseClasses = true;
    /// <summary>Skip CVL values when emitting (faster, smaller projects).</summary>
    [ObservableProperty] private bool _scaffoldNoCvlValues;
    /// <summary>Number of files written by the last JSON scaffold.</summary>
    [ObservableProperty] private int _scaffoldFilesCount;
    /// <summary>Per-warning rows from the last JSON scaffold (drives the warnings list).</summary>
    public ObservableCollection<string> ScaffoldWarnings { get; } = [];
    /// <summary>True once the JSON scaffold has produced output (drives the post-action buttons).</summary>
    [ObservableProperty] private bool _scaffoldDone;
    /// <summary>Path to the scaffolded csproj for "Open output".</summary>
    [ObservableProperty] private string? _scaffoldLastCsproj;

    // ----- Scaffold from environment -----
    /// <summary>Destination directory for a scaffold sourced from the connected env.</summary>
    [ObservableProperty] private string? _envScaffoldOutputDir;
    /// <summary>Root namespace for the env-sourced scaffold.</summary>
    [ObservableProperty] private string _envScaffoldNamespace = "Acme.PimModel";
    /// <summary>Whether the env scaffold should factor out shared field-sets.</summary>
    [ObservableProperty] private bool _envScaffoldDetectBaseClasses = true;
    /// <summary>Skip CVL values when emitting (faster, smaller projects).</summary>
    [ObservableProperty] private bool _envScaffoldNoCvlValues;
    /// <summary>Number of files written by the last env scaffold.</summary>
    [ObservableProperty] private int _envScaffoldFilesCount;
    /// <summary>Per-warning rows from the last env scaffold.</summary>
    public ObservableCollection<string> EnvScaffoldWarnings { get; } = [];
    /// <summary>True once the env scaffold has produced output.</summary>
    [ObservableProperty] private bool _envScaffoldDone;
    /// <summary>Path to the env-scaffolded csproj for "Open output".</summary>
    [ObservableProperty] private string? _envScaffoldLastCsproj;

    // ----- Merge -----
    /// <summary>Base JSON file (the "left" side of the merge).</summary>
    [ObservableProperty] private string? _mergeBasePath;
    /// <summary>Overlay JSON file (the "right" side; takes precedence under <see cref="MergeConflictPolicy.OverlayWins"/>).</summary>
    [ObservableProperty] private string? _mergeOverlayPath;
    /// <summary>Optional output path; when empty the merge runs but isn't written to disk.</summary>
    [ObservableProperty] private string? _mergeOutPath;
    /// <summary>Conflict policy in effect for the merge.</summary>
    [ObservableProperty] private MergeConflictPolicy _mergePolicy = MergeConflictPolicy.OverlayWins;
    /// <summary>Result + conflicts summary for the merge action.</summary>
    [ObservableProperty] private string _mergeResultText = "";

    // ----- Export -----
    /// <summary>Destination JSON file for an env-snapshot export.</summary>
    [ObservableProperty] private string? _exportOutputPath;
    /// <summary>Result summary for the export action.</summary>
    [ObservableProperty] private string _exportResultText = "";

    // ----- Excel export (any source → workbook). Per-page PageActions still own slice exports;
    // this tab is for "the whole model, from env/JSON/code project, into a filterable workbook". -----
    /// <summary>True when the env radio is selected.</summary>
    [ObservableProperty] private bool _excelExportIsEnv;
    /// <summary>True when the JSON-file radio is selected (default).</summary>
    [ObservableProperty] private bool _excelExportIsJson = true;
    /// <summary>True when the model-project radio is selected.</summary>
    [ObservableProperty] private bool _excelExportIsModel;
    /// <summary>Source JSON file path (used when ExcelExportIsJson).</summary>
    [ObservableProperty] private string? _excelExportJsonPath;
    /// <summary>Source model project / dll / dir path (used when ExcelExportIsModel).</summary>
    [ObservableProperty] private string? _excelExportProjectPath;
    /// <summary>Destination xlsx path.</summary>
    [ObservableProperty] private string? _excelExportOutputPath;
    /// <summary>Open the xlsx in the OS default handler after a successful export.</summary>
    [ObservableProperty] private bool _excelExportOpenAfter = true;

    partial void OnExcelExportIsEnvChanged(bool value)   { if (value) { ExcelExportIsJson = false; ExcelExportIsModel = false; } }
    partial void OnExcelExportIsJsonChanged(bool value)  { if (value) { ExcelExportIsEnv = false; ExcelExportIsModel = false; } }
    partial void OnExcelExportIsModelChanged(bool value) { if (value) { ExcelExportIsEnv = false; ExcelExportIsJson = false; } }

    // ----- Excel scaffold (workbook → typed C# project). Excel export of feature data lives
    // on each feature page via PageActions; a "capture full env" is in the Snapshots hub. -----
    /// <summary>Source Excel workbook for an Excel-driven scaffold.</summary>
    [ObservableProperty] private string? _excelScaffoldPath;
    /// <summary>Output directory for the Excel scaffold.</summary>
    [ObservableProperty] private string? _excelScaffoldOutputDir;
    /// <summary>Namespace baked into the Excel scaffold.</summary>
    [ObservableProperty] private string _excelScaffoldNamespace = "Acme.PimModel";
    /// <summary>Detect-base-classes flag for the Excel scaffold.</summary>
    [ObservableProperty] private bool _excelScaffoldDetectBaseClasses = true;
    /// <summary>Skip CVL values when emitting (faster, smaller projects).</summary>
    [ObservableProperty] private bool _excelScaffoldNoCvlValues;
    /// <summary>True once the Excel scaffold produced output.</summary>
    [ObservableProperty] private bool _excelScaffoldDone;
    /// <summary>Number of files written by the last Excel scaffold.</summary>
    [ObservableProperty] private int _excelScaffoldFilesCount;
    /// <summary>Per-warning rows from the last Excel scaffold.</summary>
    public ObservableCollection<string> ExcelScaffoldWarnings { get; } = [];

    /// <summary>Allowed values for the merge-policy dropdown.</summary>
    public MergeConflictPolicy[] MergePolicies { get; } =
    {
        MergeConflictPolicy.OverlayWins,
        MergeConflictPolicy.BaseWins,
        MergeConflictPolicy.Fail,
    };

    public ToolsViewModel(MainWindowViewModel main, Shell shell, IFileOpener fileOpener, IAppLog log)
    {
        _main = main;
        _shell = shell;
        _fileOpener = fileOpener;
        _log = log;
    }

    [RelayCommand] private async Task BrowseScaffoldJsonAsync()    => ScaffoldJsonPath = await PickFileAsync("inriver JSON export", "*.json");
    [RelayCommand] private async Task BrowseScaffoldOutAsync()     => ScaffoldOutputDir = await PickFolderAsync("Scaffold output directory");
    [RelayCommand] private async Task BrowseEnvScaffoldOutAsync()  => EnvScaffoldOutputDir = await PickFolderAsync("Scaffold output directory");
    [RelayCommand] private async Task BrowseMergeBaseAsync()       => MergeBasePath = await PickFileAsync("base JSON", "*.json");
    [RelayCommand] private async Task BrowseMergeOverlayAsync()    => MergeOverlayPath = await PickFileAsync("overlay JSON", "*.json");
    [RelayCommand] private async Task BrowseMergeOutAsync()        => MergeOutPath = await PickSaveAsync("merged JSON", "merged.json", "json");
    [RelayCommand] private async Task BrowseExportOutAsync()       => ExportOutputPath = await PickSaveAsync("snapshot JSON", "snapshot.json", "json");
    [RelayCommand] private async Task BrowseExcelScaffoldXlsxAsync() => ExcelScaffoldPath = await PickFileAsync("Excel workbook", "*.xlsx");
    [RelayCommand] private async Task BrowseExcelScaffoldOutAsync()  => ExcelScaffoldOutputDir = await PickFolderAsync("Excel scaffold output directory");
    [RelayCommand] private async Task BrowseExcelExportJsonAsync()    => ExcelExportJsonPath = await PickFileAsync("inriver JSON export", "*.json");
    [RelayCommand] private async Task BrowseExcelExportProjectAsync() => ExcelExportProjectPath = await PickFileAsync("Model csproj", "*.csproj");
    [RelayCommand] private async Task BrowseExcelExportOutAsync()     => ExcelExportOutputPath = await PickSaveAsync("workbook", "model.xlsx", "xlsx");

    [RelayCommand(CanExecute = nameof(BusyNow))] private void Cancel() => _cts?.Cancel();

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task ScaffoldAsync()
    {
        if (string.IsNullOrEmpty(ScaffoldJsonPath) || string.IsNullOrEmpty(ScaffoldOutputDir))
        {
            StatusMessage = "Pick a JSON export and an output directory.";
            return;
        }

        ScaffoldDone = false;
        ScaffoldLastCsproj = null;
        Busy = true;
        _cts = new CancellationTokenSource();
        try
        {
            _log.Info("Scaffold", $"Scaffold from JSON → {ScaffoldOutputDir}");
            var result = await _shell.ScaffoldAsync(
                ScaffoldJsonPath, ScaffoldOutputDir, ScaffoldNamespace,
                ScaffoldDetectBaseClasses, !ScaffoldNoCvlValues, _cts.Token).ConfigureAwait(true);

            ApplyScaffoldResult(result, ScaffoldWarnings, n => ScaffoldFilesCount = n);
            ScaffoldLastCsproj = Directory.EnumerateFiles(ScaffoldOutputDir!, "*.csproj", SearchOption.AllDirectories).FirstOrDefault();
            ScaffoldDone = true;
            StatusMessage = "Scaffold complete.";
            _log.Success("Scaffold", $"Scaffold complete: {result.Files.Count} files.");
            _log.Toast(LogLevel.Success, "Scaffold complete", $"{result.Files.Count} files generated.");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Scaffold failed: {ex.Message}";
            _log.Error("Scaffold", ex.Message, ex);
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task EnvScaffoldAsync()
    {
        if (string.IsNullOrEmpty(EnvScaffoldOutputDir))
        {
            StatusMessage = "Pick an output directory.";
            return;
        }
        if (!_main.IsConnected)
        {
            StatusMessage = "Connect to an environment first.";
            return;
        }

        EnvScaffoldDone = false;
        EnvScaffoldLastCsproj = null;
        Busy = true;
        _cts = new CancellationTokenSource();
        try
        {
            _log.Info("Scaffold", $"Scaffold from env → {EnvScaffoldOutputDir}");
            var result = await _shell.ScaffoldFromEnvAsync(
                EnvScaffoldOutputDir!, EnvScaffoldNamespace,
                EnvScaffoldDetectBaseClasses, !EnvScaffoldNoCvlValues, _cts.Token).ConfigureAwait(true);

            ApplyScaffoldResult(result, EnvScaffoldWarnings, n => EnvScaffoldFilesCount = n);
            EnvScaffoldLastCsproj = Directory.EnumerateFiles(EnvScaffoldOutputDir!, "*.csproj", SearchOption.AllDirectories).FirstOrDefault();
            EnvScaffoldDone = true;
            StatusMessage = "Scaffold from environment complete.";
            _log.Success("Scaffold", $"Scaffold from env complete: {result.Files.Count} files.");
            _log.Toast(LogLevel.Success, "Scaffold complete", $"{result.Files.Count} files generated.");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Scaffold from environment failed: {ex.Message}";
            _log.Error("Scaffold", ex.Message, ex);
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand]
    private void OpenScaffoldOutput() => RevealOrOpen(ScaffoldLastCsproj, ScaffoldOutputDir);

    [RelayCommand]
    private void OpenEnvScaffoldOutput() => RevealOrOpen(EnvScaffoldLastCsproj, EnvScaffoldOutputDir);

    private void RevealOrOpen(string? preferredFile, string? fallbackDir)
    {
        if (!string.IsNullOrEmpty(preferredFile)) _fileOpener.RevealInExplorer(preferredFile);
        else if (!string.IsNullOrEmpty(fallbackDir)) _fileOpener.Open(fallbackDir);
    }

    /// <summary>Project a <see cref="ScaffoldResult"/> onto the per-tab observable fields the view binds to.</summary>
    private static void ApplyScaffoldResult(ScaffoldResult result, ObservableCollection<string> warnings, Action<int> setFilesCount)
    {
        setFilesCount(result.Files.Count);
        warnings.Clear();
        foreach (var w in result.WarningsFromExpressions) warnings.Add(w);
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task MergeAsync()
    {
        if (string.IsNullOrEmpty(MergeBasePath) || string.IsNullOrEmpty(MergeOverlayPath))
        {
            StatusMessage = "Pick both base and overlay JSON files.";
            return;
        }

        Busy = true;
        _cts = new CancellationTokenSource();
        try
        {
            var (merged, conflicts) = await _shell.MergeJsonAsync(
                MergeBasePath, MergeOverlayPath, MergePolicy, _cts.Token).ConfigureAwait(true);

            if (!string.IsNullOrEmpty(MergeOutPath))
            {
                var json = JsonSerializer.Serialize(merged, InriverModelJson.Options);
                await File.WriteAllTextAsync(MergeOutPath, json).ConfigureAwait(true);
            }

            MergeResultText = conflicts.Count == 0
                ? "Merge complete. No conflicts."
                : $"Merge complete. {conflicts.Count} conflicts:\n  " + string.Join("\n  ", conflicts.Take(50));
            StatusMessage = "Merge complete.";
            _log.Success("Merge", "Merge complete.");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Merge failed: {ex.Message}";
            _log.Error("Merge", ex.Message, ex);
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task ExportAsync()
    {
        if (string.IsNullOrEmpty(ExportOutputPath))
        {
            StatusMessage = "Pick an output JSON path.";
            return;
        }
        if (!_main.IsConnected)
        {
            StatusMessage = "Connect to an environment first.";
            return;
        }

        Busy = true;
        _cts = new CancellationTokenSource();
        try
        {
            var live = await _shell.CaptureSnapshotAsync(_cts.Token).ConfigureAwait(true);
            await _shell.SaveSnapshotAsync(live, ExportOutputPath!, _cts.Token).ConfigureAwait(true);

            ExportResultText =
                $"Exported {live.EntityTypes.Count} entity types, {live.Cvls.Count} CVLs, " +
                $"{live.Categories.Count} categories.\nSaved to {ExportOutputPath}.";
            StatusMessage = "Export complete.";
            _log.Success("Export", $"Snapshot exported to {ExportOutputPath}");
            _log.Toast(LogLevel.Success, "Snapshot exported", ExportOutputPath,
                onClick: () => _fileOpener.RevealInExplorer(ExportOutputPath!));
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
            _log.Error("Export", ex.Message, ex);
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>Scaffold a typed model project from an Excel workbook.</summary>
    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task ExcelScaffoldAsync()
    {
        if (string.IsNullOrEmpty(ExcelScaffoldPath) || string.IsNullOrEmpty(ExcelScaffoldOutputDir))
        {
            StatusMessage = "Pick a workbook and an output directory.";
            return;
        }

        ExcelScaffoldDone = false;
        Busy = true;
        _cts = new CancellationTokenSource();
        try
        {
            var result = await _shell.ScaffoldFromExcelAsync(
                ExcelScaffoldPath!, ExcelScaffoldOutputDir!,
                ExcelScaffoldNamespace, ExcelScaffoldDetectBaseClasses,
                !ExcelScaffoldNoCvlValues, _cts.Token).ConfigureAwait(true);
            ApplyScaffoldResult(result, ExcelScaffoldWarnings, n => ExcelScaffoldFilesCount = n);
            ExcelScaffoldDone = true;
            StatusMessage = "Excel scaffold complete.";
            _log.Success("Excel", $"Scaffold from Excel: {result.Files.Count} files.");
            _log.Toast(LogLevel.Success, "Scaffold complete", $"{result.Files.Count} files");
        }
        catch (OperationCanceledException) { StatusMessage = "Cancelled."; }
        catch (Exception ex) { StatusMessage = "Excel scaffold failed: " + ex.Message; _log.Error("Excel", ex.Message, ex); }
        finally { Busy = false; }
    }

    /// <summary>Export the whole model to a styled, filterable Excel workbook from one of three sources.</summary>
    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task ExcelExportAsync()
    {
        if (string.IsNullOrEmpty(ExcelExportOutputPath))
        {
            StatusMessage = "Pick an output .xlsx path.";
            return;
        }

        Busy = true;
        _cts = new CancellationTokenSource();
        try
        {
            if (ExcelExportIsEnv)
            {
                if (!_main.IsConnected)
                {
                    StatusMessage = "Connect to an environment first.";
                    return;
                }
                var live = await _shell.CaptureSnapshotAsync(_cts.Token).ConfigureAwait(true);
                await _shell.SaveSnapshotAsExcelAsync(live, ExcelExportOutputPath!, _cts.Token).ConfigureAwait(true);
            }
            else if (ExcelExportIsJson)
            {
                if (string.IsNullOrEmpty(ExcelExportJsonPath))
                {
                    StatusMessage = "Pick a source JSON file.";
                    return;
                }
                await _shell.SaveJsonAsExcelAsync(ExcelExportJsonPath!, ExcelExportOutputPath!, _cts.Token).ConfigureAwait(true);
            }
            else if (ExcelExportIsModel)
            {
                if (string.IsNullOrEmpty(ExcelExportProjectPath))
                {
                    StatusMessage = "Pick a model project (csproj/dll/dir).";
                    return;
                }
                await _shell.SaveLoadedModelAsExcelAsync(ExcelExportProjectPath!, ExcelExportOutputPath!, _cts.Token).ConfigureAwait(true);
            }
            else
            {
                StatusMessage = "Pick a source.";
                return;
            }

            StatusMessage = $"Workbook written to {ExcelExportOutputPath}.";
            _log.Success("Excel", $"Exported model to {ExcelExportOutputPath}");
            _log.Toast(LogLevel.Success, "Workbook exported", ExcelExportOutputPath!,
                onClick: () => _fileOpener.RevealInExplorer(ExcelExportOutputPath!));

            if (ExcelExportOpenAfter) _fileOpener.Open(ExcelExportOutputPath!);
        }
        catch (OperationCanceledException) { StatusMessage = "Cancelled."; }
        catch (Exception ex) { StatusMessage = "Excel export failed: " + ex.Message; _log.Error("Excel", ex.Message, ex); }
        finally { Busy = false; }
    }

    private static Window? MainWindowOrNull()
        => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d
            ? d.MainWindow
            : null;

    private static async Task<string?> PickFileAsync(string title, string pattern)
    {
        var window = MainWindowOrNull();
        if (window is null) return null;

        var picks = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType(title) { Patterns = new[] { pattern } } },
        }).ConfigureAwait(true);

        return picks.Count == 0 ? null : picks[0].TryGetLocalPath();
    }

    private static async Task<string?> PickFolderAsync(string title)
    {
        var window = MainWindowOrNull();
        if (window is null) return null;

        var picks = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        }).ConfigureAwait(true);

        return picks.Count == 0 ? null : picks[0].TryGetLocalPath();
    }

    private static async Task<string?> PickSaveAsync(string title, string suggestedName, string ext)
    {
        var window = MainWindowOrNull();
        if (window is null) return null;

        var pick = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = ext,
        }).ConfigureAwait(true);

        return pick?.TryGetLocalPath();
    }
}
