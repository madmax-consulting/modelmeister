using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace ModelMeister.Ui.Services;

/// <summary>
/// Tiny wrapper around the platform clipboard. Resolves the active <see cref="TopLevel"/> off the
/// main window so callers don't need to thread a clipboard reference through every view-model.
/// </summary>
internal static class ClipboardHelpers
{
    /// <summary>Place <paramref name="text"/> on the platform clipboard. No-op when there's no main window or text is null.</summary>
    public static Task CopyAsync(string? text)
    {
        if (string.IsNullOrEmpty(text)) return Task.CompletedTask;
        var owner = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d ? d.MainWindow : null;
        var clipboard = owner is null ? null : TopLevel.GetTopLevel(owner)?.Clipboard;
        return clipboard?.SetTextAsync(text) ?? Task.CompletedTask;
    }
}
