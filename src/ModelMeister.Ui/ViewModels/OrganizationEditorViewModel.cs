using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Ui.Models;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// View-model behind the Create / Edit organization dialog. Edits a working copy; the caller commits it
/// to the registry on confirm. The built-in "Default" organization is editable except its stable
/// <see cref="Organization.Key"/>.
/// </summary>
public partial class OrganizationEditorViewModel : ViewModelBase
{
    public OrganizationEditorViewModel(Organization? existing)
    {
        IsEdit = existing is not null;
        var src = existing ?? new Organization();
        Key = src.Key;
        IsBuiltIn = src.IsBuiltIn;
        SortOrder = src.SortOrder;
        _name = src.Name;
        _description = src.Description ?? "";
    }

    /// <summary>True when editing an existing organization (built-in or custom).</summary>
    public bool IsEdit { get; }
    /// <summary>True for the shipped "Default" organization — name/description are editable but the key is fixed.</summary>
    public bool IsBuiltIn { get; }
    /// <summary>Stable key, preserved across edits (empty for a brand-new organization — the registry assigns one).</summary>
    public string Key { get; }
    private int SortOrder { get; }

    public string Title => IsEdit
        ? (IsBuiltIn ? "Edit built-in organization" : "Edit organization")
        : "New organization";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _name;

    [ObservableProperty] private string _description;
    [ObservableProperty] private string _validation = "";

    /// <summary>Project the form into an <see cref="Organization"/> for <see cref="IOrganizationRegistry.Upsert"/>.</summary>
    public Organization ToOrganization() => new()
    {
        Key = Key,
        Name = Name.Trim(),
        Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
        IsBuiltIn = IsBuiltIn,
        SortOrder = SortOrder,
    };

    public bool? Result { get; private set; }
    public event Action? Closed;

    private bool CanConfirm() => !string.IsNullOrWhiteSpace(Name);

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        if (!CanConfirm()) { Validation = "Name is required."; return; }
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
