using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// View-model for the "grant / revoke a role on the selected users" dialog. The user picks one role
/// from the env's role list and chooses Add or Remove; <see cref="UsersViewModel"/> then re-provisions
/// each checked user with the adjusted role set via <c>Shell.ProvisionUserAsync</c>.
/// </summary>
public sealed partial class BulkUserRoleViewModel : ViewModelBase
{
    public BulkUserRoleViewModel(int userCount, IReadOnlyList<string> roles)
    {
        UserCount = userCount;
        foreach (var r in roles) Roles.Add(r);
        SelectedRole = Roles.FirstOrDefault();
    }

    /// <summary>How many users the action will touch — shown in the prompt.</summary>
    public int UserCount { get; }
    public ObservableCollection<string> Roles { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string? _selectedRole;

    /// <summary>True = grant the role; false = revoke it.</summary>
    [ObservableProperty] private bool _add = true;

    public bool? Result { get; private set; }
    public event Action? Closed;

    private bool CanConfirm() => !string.IsNullOrWhiteSpace(SelectedRole);

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
}
