using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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

    /// <summary>Common inriver restriction types offered as suggestions; the field stays free-text.</summary>
    public ObservableCollection<string> RestrictionTypes { get; } = ["ReadOnly", "Hidden"];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string? _selectedRole;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _restrictionType = "ReadOnly";

    [ObservableProperty] private string _entityTypeId = "";
    [ObservableProperty] private string _fieldTypeId = "";
    [ObservableProperty] private string _categoryId = "";
    [ObservableProperty] private string _validation = "";

    public bool? Result { get; private set; }
    public event Action? Closed;

    private bool CanConfirm() => !string.IsNullOrWhiteSpace(SelectedRole) && !string.IsNullOrWhiteSpace(RestrictionType);

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        if (string.IsNullOrWhiteSpace(SelectedRole)) { Validation = "Role is required."; return; }
        if (string.IsNullOrWhiteSpace(RestrictionType)) { Validation = "Restriction type is required."; return; }
        if (string.IsNullOrWhiteSpace(EntityTypeId) && string.IsNullOrWhiteSpace(FieldTypeId) && string.IsNullOrWhiteSpace(CategoryId))
        {
            Validation = "Set at least one of Entity type, Field type, or Category.";
            return;
        }
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
