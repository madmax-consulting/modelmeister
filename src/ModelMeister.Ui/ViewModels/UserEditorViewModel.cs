using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// View-model behind the Create / Edit User dialog. Collects the user's identity fields + role
/// memberships (checkbox list of every role in the env). On edit, the username is locked (the upsert
/// keys by username). The caller reads the populated properties on Save and routes them through
/// <c>Shell.ProvisionUserAsync</c> (which requires the env's REST endpoint + key).
/// </summary>
public partial class UserEditorViewModel : ViewModelBase
{
    public UserEditorViewModel(
        string? username, string? email, string? firstName, string? lastName, string? company,
        string? language, IReadOnlyList<string> selectedRoles, IReadOnlyList<string> availableRoles,
        bool isEdit)
    {
        _username = username ?? "";
        _email = email ?? "";
        _firstName = firstName ?? "";
        _lastName = lastName ?? "";
        _company = company ?? "";
        _language = string.IsNullOrWhiteSpace(language) ? "en" : language;
        IsEdit = isEdit;

        var sel = new HashSet<string>(selectedRoles, StringComparer.OrdinalIgnoreCase);
        // Union available roles with any already-assigned ones (defensive) and sort for a stable list.
        var all = availableRoles.Concat(selectedRoles).Distinct(StringComparer.OrdinalIgnoreCase)
                                .OrderBy(r => r, StringComparer.OrdinalIgnoreCase);
        foreach (var r in all) Roles.Add(new RoleToggle(r, sel.Contains(r)));
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _username;

    [ObservableProperty] private string _email;
    [ObservableProperty] private string _firstName;
    [ObservableProperty] private string _lastName;
    [ObservableProperty] private string _company;
    [ObservableProperty] private string _language;
    /// <summary>Generate a REST API key for the user on create (ignored on edit).</summary>
    [ObservableProperty] private bool _generateApiKey;
    [ObservableProperty] private string _validation = "";

    /// <summary>True when editing an existing user; locks the Username field.</summary>
    public bool IsEdit { get; }

    public string Title => IsEdit ? "Edit user" : "New user";

    public ObservableCollection<RoleToggle> Roles { get; } = [];

    /// <summary>Names of the currently-checked roles.</summary>
    public IReadOnlyList<string> SelectedRoles => Roles.Where(r => r.IsSelected).Select(r => r.Name).ToList();

    public bool? Result { get; private set; }
    public event Action? Closed;

    private bool CanConfirm() => !string.IsNullOrWhiteSpace(Username);

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        if (string.IsNullOrWhiteSpace(Username)) { Validation = "Username is required."; return; }
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

/// <summary>One toggleable role row in the user editor's role list.</summary>
public partial class RoleToggle : ObservableObject
{
    public RoleToggle(string name, bool isSelected)
    {
        Name = name;
        _isSelected = isSelected;
    }

    public string Name { get; }
    [ObservableProperty] private bool _isSelected;
}
