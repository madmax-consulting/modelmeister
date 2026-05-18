using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace ModelMeister.Ui.Services;

/// <summary>
/// Common Open/Save file-picker dialogs used by feature pages. Centralised here so each page
/// doesn't reimplement the same five lines around <see cref="IStorageProvider"/>.
/// </summary>
public static class FilePickerHelpers
{
    static Window? MainWindowOrNull()
        => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d ? d.MainWindow : null;

    /// <summary>Show an Open dialog filtered to a single extension; returns the picked local path or null.</summary>
    public static async Task<string?> PickOpenAsync(string title, string extension)
    {
        var w = MainWindowOrNull();
        if (w is null) return null;
        var picks = await w.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(extension.ToUpperInvariant())
                {
                    Patterns = new[] { $"*.{extension}" },
                },
            },
        }).ConfigureAwait(true);
        return picks.Count == 0 ? null : picks[0].TryGetLocalPath();
    }

    /// <summary>Show a Save dialog with a suggested filename and default extension.</summary>
    public static async Task<string?> PickSaveAsync(string title, string suggestedFileName, string defaultExtension)
    {
        var w = MainWindowOrNull();
        if (w is null) return null;
        var pick = await w.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            DefaultExtension = defaultExtension,
        }).ConfigureAwait(true);
        return pick?.TryGetLocalPath();
    }
}
