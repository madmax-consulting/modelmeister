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
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// CVL workbench: capture CVLs from the connected env and export to Excel for editing.
/// </summary>
public partial class CvlWorkbenchViewModel : FeaturePageViewModel
{
    readonly MainWindowViewModel _main;
    readonly Shell _shell;
    readonly IAppLog _log;

    /// <inheritdoc/>
    public override bool SupportsCompare => true;
    /// <inheritdoc/>
    public override BackupScope BackupScope => BackupScope.Cvls;
    /// <inheritdoc/>
    public override ExcelCapability Excel => ExcelCapability.Export;

    /// <inheritdoc/>
    public override Task BackupAsync()
    {
        _log.Toast(LogLevel.Info, "Backup",
            "CVL-scoped backup ships with the Backup hub migration. For now use Full snapshot from the Dashboard.");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public override Task ExportExcelAsync() => ExportFullWorkbookAsync();

    public ObservableCollection<CvlRow> Cvls { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportFullWorkbookCommand))]
    private bool _busy;
    [ObservableProperty] private string _status = "";

    public CvlWorkbenchViewModel(MainWindowViewModel main, Shell shell, IAppLog log)
    {
        _main = main;
        _shell = shell;
        _log = log;
        _main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsConnected))
                ExportFullWorkbookCommand.NotifyCanExecuteChanged();
        };
    }

    private bool CanExportWorkbook() => !Busy && _main.IsConnected;

    /// <inheritdoc/>
    public override async Task RefreshAsync()
    {
        if (!_main.IsConnected) { Status = "Connect first."; return; }
        Busy = true;
        try
        {
            var snap = await _shell.CaptureSnapshotAsync().ConfigureAwait(true);
            Cvls.Clear();
            foreach (var c in snap.Cvls.OrderBy(c => c.Id, StringComparer.OrdinalIgnoreCase))
                Cvls.Add(new CvlRow(c.Id, c.DataType.ToString(), c.Values.Count, c.ParentId ?? "", c.CustomValueList));
            Status = $"{Cvls.Count} CVLs · {Cvls.Sum(r => r.ValueCount)} values";
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; _log.Error("Cvl", ex.Message); }
        finally { Busy = false; }
    }

    [RelayCommand] private Task CopyCvlId(CvlRow? row) => ClipboardHelpers.CopyAsync(row?.Id);

    [RelayCommand(CanExecute = nameof(CanExportWorkbook))]
    public async Task ExportFullWorkbookAsync()
    {
        if (!_main.IsConnected) { Status = "Connect first."; return; }
        var path = await PickSaveAsync("cvls.xlsx").ConfigureAwait(true);
        if (path is null) return;
        Busy = true;
        try
        {
            var snap = await _shell.CaptureSnapshotAsync().ConfigureAwait(true);
            await _shell.SaveCvlValuesAsExcelAsync(snap, path).ConfigureAwait(true);
            Status = $"Wrote {Path.GetFileName(path)}";
            _log.Success("Cvl", $"Exported CVL workbook: {path}");
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; _log.Error("Cvl", ex.Message); }
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
            Title = "Save CVL workbook",
            SuggestedFileName = suggested,
            DefaultExtension = "xlsx",
        }).ConfigureAwait(true);
        return pick?.TryGetLocalPath();
    }
}

public sealed record CvlRow(string Id, string DataType, int ValueCount, string ParentId, bool CustomValueList);
