using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
/// View-model behind the universal Import-from-workbook dialog. Captures a workbook path (with a
/// Recents dropdown of previously-imported files), then resolves to <c>true</c> when the user
/// confirms. The import itself always verifies + dry-runs first and asks for explicit approval before
/// writing — so this dialog only collects the file; it no longer carries a dry-run toggle.
/// </summary>
public partial class ImportWorkbookViewModel : ViewModelBase
{
    /// <summary>Display title shown in the dialog (e.g. "Import users from workbook").</summary>
    public string Title { get; }

    /// <summary>One-line subtitle / explanation shown under the title.</summary>
    public string Subtitle { get; }

    /// <summary>File-picker label (e.g. "users.xlsx").</summary>
    public string SuggestedFileName { get; }

    /// <summary>Recently-imported workbook paths (newest first) for the Recents dropdown.</summary>
    public ObservableCollection<string> Recents { get; } = [];

    /// <summary>True when there is at least one recent workbook to offer.</summary>
    public bool HasRecents => Recents.Count > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string? _workbookPath;

    public bool? Result { get; private set; }

    public event Action? Closed;

    public ImportWorkbookViewModel(string title, string subtitle, string suggestedFileName = "workbook.xlsx", IReadOnlyList<string>? recents = null)
    {
        Title = title;
        Subtitle = subtitle;
        SuggestedFileName = suggestedFileName;
        if (recents is not null)
            foreach (var p in recents) Recents.Add(p);
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
