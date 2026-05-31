using System;
using System.Collections.Generic;
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

    /// <summary>Show the apply-confirmation prompt; returns <c>true</c> when the user confirms.</summary>
    public static Task<bool> ConfirmApplyAsync(
        string envUrl,
        int changeCount,
        string policySummary = "",
        string? typeKey = null,
        IReadOnlyList<ApplyReviewItem>? changes = null,
        IReadOnlyList<ModelMeister.Inriver.Diff.BlastRadiusEntry>? blastRadius = null,
        string? driftWarning = null,
        string? environmentContext = null)
    {
        var vm = new ConfirmApplyViewModel(envUrl, changeCount, policySummary, typeKey, changes, blastRadius, driftWarning, environmentContext);
        return ShowDialogAsync<ConfirmApplyDialog>(vm, dlg => vm.Closed += () => dlg.Close(vm.Result == true));
    }

    /// <summary>Show the unified Excel-import workflow window (one popup for ChooseFile → Verify →
    /// Import → Results, never closing between steps). Runs the plan's preconditions first and toasts
    /// + returns <c>false</c> without opening if they fail. Returns <c>true</c> once an import has run
    /// (so the caller refreshes its page).</summary>
    public static async Task<bool> ShowImportWorkflowAsync(
        ModelMeister.Ui.Services.Import.IImportPlan plan,
        IAppLog log,
        System.Collections.Generic.IReadOnlyList<string>? recents = null,
        ModelMeister.Ui.Services.Import.IImportConfirmGate? confirmGate = null,
        IFileOpener? fileOpener = null)
    {
        var blocker = plan.CheckPreconditions();
        if (blocker is not null)
        {
            log.Toast(LogLevel.Warn, plan.Metadata.Title, blocker);
            return false;
        }
        // The removal gate is only exercised by features that report removals (CVLs); the others pass
        // none and get a stage-unaware default that is never invoked. The file opener is only used by
        // the Results "Reveal backup" button.
        confirmGate ??= new ModelMeister.Ui.Services.Import.ImportConfirmGate(null, null);
        fileOpener ??= new OsFileOpener();
        var vm = new ImportWorkflowViewModel(plan, log, fileOpener, confirmGate, recents);
        return await ShowDialogAsync<ImportWorkflowDialog>(vm, dlg => vm.Closed += () => dlg.Close(vm.Result == true)).ConfigureAwait(true);
    }

    /// <summary>Show the Add/Edit server-setting dialog. Returns the populated VM on Confirm, else <c>null</c>.</summary>
    public static async Task<AddServerSettingViewModel?> AddServerSettingAsync(string? initialKey = null, string? initialValue = null, bool isEdit = false)
    {
        var vm = new AddServerSettingViewModel(initialKey, initialValue, isEdit);
        var ok = await ShowDialogAsync<AddServerSettingDialog>(vm, dlg => vm.Closed += () => dlg.Close(vm.Result == true)).ConfigureAwait(true);
        return ok ? vm : null;
    }

    /// <summary>Show the Create / Edit environment-type dialog (<paramref name="existing"/> = null to create).
    /// Returns the populated VM on Save, else <c>null</c>.</summary>
    public static async Task<EnvironmentTypeEditorViewModel?> EnvironmentTypeEditorAsync(ModelMeister.Ui.Models.EnvironmentType? existing)
    {
        var vm = new EnvironmentTypeEditorViewModel(existing);
        var ok = await ShowDialogAsync<EnvironmentTypeEditorDialog>(vm, dlg => vm.Closed += () => dlg.Close(vm.Result == true)).ConfigureAwait(true);
        return ok ? vm : null;
    }

    /// <summary>Show the Create / Edit organization dialog (<paramref name="existing"/> = null to create).
    /// Returns the populated VM on Save, else <c>null</c>.</summary>
    public static async Task<OrganizationEditorViewModel?> OrganizationEditorAsync(ModelMeister.Ui.Models.Organization? existing)
    {
        var vm = new OrganizationEditorViewModel(existing);
        var ok = await ShowDialogAsync<OrganizationEditorDialog>(vm, dlg => vm.Closed += () => dlg.Close(vm.Result == true)).ConfigureAwait(true);
        return ok ? vm : null;
    }

    /// <summary>Show the folder-picker chooser for Copy-to / Move-to / bulk destination selection.
    /// Presents a read-only mirror of <paramref name="tree"/> (with the <paramref name="exclude"/> subtree
    /// omitted so a folder can't be moved into itself) plus a "(root)" option, and — when
    /// <paramref name="allowScopeSwitch"/> — a Shared/Personal scope selector backed by <paramref name="users"/>.
    /// Returns a <see cref="FolderPickResult"/> on Choose, else <c>null</c> (cancelled).</summary>
    public static async Task<FolderPickResult?> PickFolderAsync(
        string title,
        System.Collections.Generic.IEnumerable<ModelMeister.Ui.ViewModels.WorkAreaNode> tree,
        ModelMeister.Ui.ViewModels.WorkAreaNode? exclude,
        bool allowScopeSwitch,
        System.Collections.Generic.IEnumerable<ModelMeister.Inriver.Users.UserSummary>? users,
        string? currentUser)
    {
        var vm = new FolderPickerViewModel(title, tree, exclude, allowScopeSwitch, users, currentUser);
        var ok = await ShowDialogAsync<FolderPickerDialog>(vm, dlg => vm.Closed += () => dlg.Close(vm.Result == true)).ConfigureAwait(true);
        if (!ok) return null;
        var targetUsername = (allowScopeSwitch && !vm.ToShared) ? vm.TargetUser?.Username : null;
        return new FolderPickResult(vm.SelectedTarget?.Id, targetUsername);
    }

    /// <summary>Show the work-area saved-query builder for <paramref name="folderName"/>, seeded with
    /// <paramref name="existingQueryJson"/> (null for a brand-new query) and validated against
    /// <paramref name="meta"/>. Returns the populated VM on Save (read <c>ResultJson</c>), else <c>null</c>.</summary>
    public static async Task<QueryEditorViewModel?> QueryEditorAsync(
        string folderName, string? existingQueryJson, ModelMeister.Inriver.WorkAreas.Query.QueryMetadata meta)
    {
        var vm = new QueryEditorViewModel(folderName, existingQueryJson, meta);
        var ok = await ShowDialogAsync<QueryEditorDialog>(vm, dlg => vm.Closed += () => dlg.Close(vm.Result == true)).ConfigureAwait(true);
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

    /// <summary>Show the bulk role-permission dialog (pick a permission + Add/Remove). Returns the VM
    /// on Apply, else <c>null</c>.</summary>
    public static async Task<BulkRolePermissionViewModel?> BulkRolePermissionAsync(
        int roleCount, System.Collections.Generic.IReadOnlyList<string> permissions)
    {
        var vm = new BulkRolePermissionViewModel(roleCount, permissions);
        var ok = await ShowDialogAsync<BulkRolePermissionDialog>(vm, dlg => vm.Closed += () => dlg.Close(vm.Result == true)).ConfigureAwait(true);
        return ok ? vm : null;
    }

    /// <summary>Show the Create / Edit User dialog. Returns the populated VM on Save, else <c>null</c>.</summary>
    public static async Task<UserEditorViewModel?> UserEditorAsync(
        string? username, string? email, string? firstName, string? lastName, string? language,
        System.Collections.Generic.IReadOnlyList<string> selectedRoles,
        System.Collections.Generic.IReadOnlyList<string> availableRoles,
        bool isEdit)
    {
        var vm = new UserEditorViewModel(username, email, firstName, lastName, language, selectedRoles, availableRoles, isEdit);
        var ok = await ShowDialogAsync<UserEditorDialog>(vm, dlg => vm.Closed += () => dlg.Close(vm.Result == true)).ConfigureAwait(true);
        return ok ? vm : null;
    }

    /// <summary>Show the bulk user-role dialog (pick a role + Add/Remove). Returns the VM on Apply, else <c>null</c>.</summary>
    public static async Task<BulkUserRoleViewModel?> BulkUserRoleAsync(
        int userCount, System.Collections.Generic.IReadOnlyList<string> roles)
    {
        var vm = new BulkUserRoleViewModel(userCount, roles);
        var ok = await ShowDialogAsync<BulkUserRoleDialog>(vm, dlg => vm.Closed += () => dlg.Close(vm.Result == true)).ConfigureAwait(true);
        return ok ? vm : null;
    }

    /// <summary>Show the Add Restricted-field dialog. Returns the populated VM on Add, else <c>null</c>.</summary>
    public static async Task<RestrictedFieldEditorViewModel?> RestrictedFieldEditorAsync(
        System.Collections.Generic.IReadOnlyList<string> roleNames)
    {
        var vm = new RestrictedFieldEditorViewModel(roleNames);
        var ok = await ShowDialogAsync<RestrictedFieldEditorDialog>(vm, dlg => vm.Closed += () => dlg.Close(vm.Result == true)).ConfigureAwait(true);
        return ok ? vm : null;
    }

    /// <summary>Show the per-row promote confirmation. Returns <c>true</c> when the user clicks Continue.</summary>
    public static Task<bool> ConfirmPromoteAsync(string conceptLabel, string itemLabel, string sourceEnv, string targetEnv, string? targetTypeKey, string? sourceTypeKey = null)
    {
        var vm = new PromoteConfirmViewModel(conceptLabel, itemLabel, sourceEnv, targetEnv, targetTypeKey, sourceTypeKey);
        return ShowDialogAsync<PromoteConfirmDialog>(vm, dlg => vm.Closed += () => dlg.Close(vm.Result == true));
    }

    /// <summary>Show the post-import / post-dry-run result summary. Returns <c>true</c> only when the
    /// user clicked "Continue with import" on a dry-run preview (the signal to run the real import).</summary>
    public static Task<bool> ShowProvisionResultAsync(ProvisionResultViewModel vm)
        => ShowDialogAsync<ProvisionResultDialog>(vm, dlg => vm.Closed += () => dlg.Close(vm.Proceed));

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
        string? typeKey = null,
        bool destructive = true)
    {
        var vm = new ConfirmBulkViewModel(title, verb, noun, itemNames, envName, typeKey, destructive);
        return ShowDialogAsync<ConfirmBulkDialog>(vm, dlg => vm.Closed += () => dlg.Close(vm.Result == true));
    }

    /// <summary>Prompt for a single short string. Returns the trimmed value on Confirm, else <c>null</c>.</summary>
    public static async Task<string?> PromptTextAsync(
        string title, string label, string? initial = null, string? watermark = null, string confirmLabel = "OK")
    {
        var vm = new TextPromptViewModel(title, label, initial, watermark, confirmLabel);
        var ok = await ShowDialogAsync<TextPromptDialog>(vm, dlg => vm.Closed += () => dlg.Close(vm.Result == true)).ConfigureAwait(true);
        return ok ? vm.Text : null;
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

/// <summary>
/// Outcome of <see cref="DialogHost.PickFolderAsync"/>. <see cref="TargetParentId"/> is the chosen parent
/// folder id (<c>null</c> = place at the root). <see cref="TargetPersonalUsername"/> is the destination
/// personal-scope user (<c>null</c> = the Shared scope).
/// </summary>
public sealed record FolderPickResult(Guid? TargetParentId, string? TargetPersonalUsername);
