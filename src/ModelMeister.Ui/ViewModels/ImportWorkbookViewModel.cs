using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// View-model behind the universal Import-from-workbook dialog. Captures a workbook path and a
/// dry-run flag, then resolves to <c>true</c> when the user confirms. The caller is responsible
/// for actually doing the import — this VM just collects the inputs in a consistent place.
/// </summary>
public partial class ImportWorkbookViewModel : ViewModelBase
{
    /// <summary>Display title shown in the dialog (e.g. "Import users from workbook").</summary>
    public string Title { get; }

    /// <summary>One-line subtitle / explanation shown under the title.</summary>
    public string Subtitle { get; }

    /// <summary>File-picker label (e.g. "users.xlsx").</summary>
    public string SuggestedFileName { get; }

    /// <summary>When false, the dry-run checkbox is hidden (the operation has no preview mode).</summary>
    public bool SupportsDryRun { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string? _workbookPath;

    [ObservableProperty] private bool _dryRun = true;

    public bool? Result { get; private set; }

    public event Action? Closed;

    public ImportWorkbookViewModel(string title, string subtitle, string suggestedFileName = "workbook.xlsx", bool supportsDryRun = true)
    {
        Title = title;
        Subtitle = subtitle;
        SuggestedFileName = suggestedFileName;
        SupportsDryRun = supportsDryRun;
        if (!supportsDryRun) _dryRun = false;
    }

    private bool CanConfirm() =>
        !string.IsNullOrEmpty(WorkbookPath) && File.Exists(WorkbookPath);

    [RelayCommand]
    private async Task PickAsync()
    {
        var w = MainWindowOrNull();
        if (w is null) return;
        var picks = await w.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Title,
            FileTypeFilter = new[] { new FilePickerFileType("Excel") { Patterns = new[] { "*.xlsx" } } },
        }).ConfigureAwait(true);
        if (picks.Count == 0) return;
        WorkbookPath = picks[0].TryGetLocalPath();
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        Result = true;
        Closed?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = false;
        Closed?.Invoke();
    }

    static Window? MainWindowOrNull()
        => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d ? d.MainWindow : null;
}
