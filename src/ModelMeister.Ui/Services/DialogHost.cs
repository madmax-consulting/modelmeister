using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using ModelMeister.Ui.ViewModels;
using ModelMeister.Ui.Views;

namespace ModelMeister.Ui.Services;

/// <summary>
/// Tiny modal-dialog helper: instantiates a <see cref="Window"/>, wires it to a view-model's
/// <c>Closed</c> event, and resolves to <c>true</c>/<c>false</c> based on the VM's <c>Result</c>.
/// </summary>
internal static class DialogHost
{
    /// <summary>Show the environment editor; returns <c>true</c> when the user clicked Save.</summary>
    public static Task<bool> ShowAsync(EnvEditorViewModel vm)
        => ShowDialogAsync<EnvEditorDialog>(vm, dlg => vm.Closed += () => dlg.Close(vm.Result == true));

    /// <summary>Show the apply-confirmation prompt; returns <c>true</c> when the user typed APPLY and confirmed.</summary>
    public static Task<bool> ConfirmApplyAsync(
        string envUrl,
        int changeCount,
        string policySummary = "",
        string stage = "Unspecified")
    {
        var vm = new ConfirmApplyViewModel(envUrl, changeCount, policySummary, stage);
        return ShowDialogAsync<ConfirmApplyDialog>(vm, dlg => vm.Closed += () => dlg.Close(vm.Result == true));
    }

    /// <summary>Show the import-from-workbook prompt; returns the populated VM (with path and dry-run flag) when the user confirms, else <c>null</c>.</summary>
    public static async Task<ImportWorkbookViewModel?> ImportWorkbookAsync(string title, string subtitle, string suggestedFileName = "workbook.xlsx", bool supportsDryRun = true)
    {
        var vm = new ImportWorkbookViewModel(title, subtitle, suggestedFileName, supportsDryRun);
        var ok = await ShowDialogAsync<ImportWorkbookDialog>(vm, dlg => vm.Closed += () => dlg.Close(vm.Result == true)).ConfigureAwait(true);
        return ok ? vm : null;
    }

    private static async Task<bool> ShowDialogAsync<TDialog>(object dataContext, Action<Window> wire)
        where TDialog : Window, new()
    {
        var owner = MainWindowOrNull();
        if (owner is null) return false;

        var dlg = new TDialog { DataContext = dataContext };
        wire(dlg);
        var result = await dlg.ShowDialog<bool?>(owner).ConfigureAwait(true);
        return result == true;
    }

    private static Window? MainWindowOrNull()
        => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d
            ? d.MainWindow
            : null;
}
