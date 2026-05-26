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

    /// <summary>Show the Add/Edit server-setting dialog. Returns the populated VM on Confirm, else <c>null</c>.</summary>
    public static async Task<AddServerSettingViewModel?> AddServerSettingAsync(string? initialKey = null, string? initialValue = null, bool isEdit = false)
    {
        var vm = new AddServerSettingViewModel(initialKey, initialValue, isEdit);
        var ok = await ShowDialogAsync<AddServerSettingDialog>(vm, dlg => vm.Closed += () => dlg.Close(vm.Result == true)).ConfigureAwait(true);
        return ok ? vm : null;
    }

    /// <summary>Show the Create / Edit Role dialog. Returns the populated VM on Save, else <c>null</c>.</summary>
    public static async Task<RoleEditorViewModel?> RoleEditorAsync(
        string? name, string? description,
        System.Collections.Generic.IReadOnlyList<string> selectedPermissions,
        System.Collections.Generic.IReadOnlyList<string> availablePermissions,
        bool isEdit)
    {
        var vm = new RoleEditorViewModel(name, description, selectedPermissions, availablePermissions, isEdit);
        var ok = await ShowDialogAsync<RoleEditorDialog>(vm, dlg => vm.Closed += () => dlg.Close(vm.Result == true)).ConfigureAwait(true);
        return ok ? vm : null;
    }

    /// <summary>Show the Create / Edit CVL dialog (definition + value editor). Returns the VM on Save, else <c>null</c>.</summary>
    public static async Task<CvlEditorViewModel?> CvlEditorAsync(
        bool isEdit, string id,
        ModelMeister.Model.Primitives.CvlDataType dataType, string? parentId, bool customValueList,
        System.Collections.Generic.IReadOnlyList<ModelMeister.Inriver.Snapshot.LiveCvlValue> values,
        System.Collections.Generic.IReadOnlyList<string> availableCvlIds)
    {
        var vm = new CvlEditorViewModel(isEdit, id, dataType, parentId, customValueList, values, availableCvlIds);
        var ok = await ShowDialogAsync<CvlEditorDialog>(vm, dlg => vm.Closed += () => dlg.Close(vm.Result == true)).ConfigureAwait(true);
        return ok ? vm : null;
    }

    /// <summary>Show the per-row promote confirmation. Returns <c>true</c> when the user clicks Continue.</summary>
    public static Task<bool> ConfirmPromoteAsync(string conceptLabel, string itemLabel, string sourceEnv, string targetEnv, string targetStage)
    {
        var vm = new PromoteConfirmViewModel(conceptLabel, itemLabel, sourceEnv, targetEnv, targetStage);
        return ShowDialogAsync<PromoteConfirmDialog>(vm, dlg => vm.Closed += () => dlg.Close(vm.Result == true));
    }

    /// <summary>Show the post-import / post-dry-run result summary. Always returns once the user closes it.</summary>
    public static Task<bool> ShowProvisionResultAsync(ProvisionResultViewModel vm)
        => ShowDialogAsync<ProvisionResultDialog>(vm, dlg => vm.Closed += () => dlg.Close(true));

    /// <summary>
    /// Show the itemized, stage-aware bulk-confirm dialog. Lists every target by name (capped) plus a
    /// Prod banner when <paramref name="stage"/> is Prod and the action is destructive. Returns
    /// <c>true</c> when the user confirms. Use this for every delete (single + bulk) so the user always
    /// sees what they are about to change.
    /// </summary>
    public static Task<bool> ConfirmBulkAsync(
        string title, string verb, string noun,
        System.Collections.Generic.IReadOnlyList<string> itemNames,
        string? envName,
        ModelMeister.Ui.Models.EnvironmentStage stage = ModelMeister.Ui.Models.EnvironmentStage.Unspecified,
        bool destructive = true)
    {
        var vm = new ConfirmBulkViewModel(title, verb, noun, itemNames, envName, stage, destructive);
        return ShowDialogAsync<ConfirmBulkDialog>(vm, dlg => vm.Closed += () => dlg.Close(vm.Result == true));
    }

    /// <summary>Simple yes/no confirmation. Returns <c>true</c> on the affirmative button.</summary>
    public static async Task<bool> ConfirmAsync(string title, string message, string confirmLabel = "Continue", string cancelLabel = "Abort")
    {
        var vm = new SimpleConfirmViewModel(title, message, confirmLabel, cancelLabel);
        return await ShowDialogAsync<SimpleConfirmDialog>(vm, dlg => vm.Closed += () => dlg.Close(vm.Result == true)).ConfigureAwait(true);
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
