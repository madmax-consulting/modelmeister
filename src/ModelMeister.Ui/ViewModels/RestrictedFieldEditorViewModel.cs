using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Inriver.Users;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// View-model behind the Add Restricted-field dialog. Collects a role + restriction type and the
/// target (one or more of entity-type / field-type / category id). Restricted-field permissions have
/// no update operation, so this is add-only; the caller reads the populated properties on Save and
/// routes them through <c>Shell.AddRestrictedFieldAsync</c>.
/// </summary>
public partial class RestrictedFieldEditorViewModel : ViewModelBase
{
    public RestrictedFieldEditorViewModel(IReadOnlyList<string> roleNames)
    {
        foreach (var r in roleNames) Roles.Add(r);
        SelectedRole = Roles.FirstOrDefault();
    }

    public ObservableCollection<string> Roles { get; } = [];

    /// <summary>The only restriction types inriver accepts (canonical casing — lowercase 'o' in "Readonly").</summary>
    public ObservableCollection<string> RestrictionTypes { get; } = ["Readonly", "Hidden"];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string? _selectedRole;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _restrictionType = "Readonly";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _entityTypeId = "";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _fieldTypeId = "";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _categoryId = "";
    [ObservableProperty] private string _validation = "";

    public bool? Result { get; private set; }
    public event Action? Closed;

    private static bool IsValidRestrictionType(string? t) =>
        RestrictedFieldProvisioning.NormalizeRestrictionType(t) is not null;

    // inriver requires a role, a valid restriction type, an entity type, and at least one of
    // field-type / category. Gate the button on the full rule so an invalid restriction can't be added.
    private bool CanConfirm() =>
        !string.IsNullOrWhiteSpace(SelectedRole)
        && IsValidRestrictionType(RestrictionType)
        && !string.IsNullOrWhiteSpace(EntityTypeId)
        && (!string.IsNullOrWhiteSpace(FieldTypeId) || !string.IsNullOrWhiteSpace(CategoryId));

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        if (string.IsNullOrWhiteSpace(SelectedRole)) { Validation = "Role is required."; return; }
        if (!IsValidRestrictionType(RestrictionType)) { Validation = "Restriction type must be 'Readonly' or 'Hidden'."; return; }
        if (string.IsNullOrWhiteSpace(EntityTypeId)) { Validation = "Entity type is required."; return; }
        if (string.IsNullOrWhiteSpace(FieldTypeId) && string.IsNullOrWhiteSpace(CategoryId))
        {
            Validation = "Set at least one of Field type or Category.";
            return;
        }
        // Persist canonical casing so the natural key matches inriver's stored value.
        RestrictionType = RestrictedFieldProvisioning.NormalizeRestrictionType(RestrictionType)!;
        Result = true;
        Closed?.Invoke();
    }

    [RelayCommand]
    private void Abort()
    {
        Result = false;
        Closed?.Invoke();
    }
}
