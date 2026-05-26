using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// View-model for the "grant / revoke a permission on the selected roles" dialog. The user picks one
/// permission from the env's permission list and chooses Add or Remove; <see cref="RolesViewModel"/>
/// then applies it to every checked role via <c>Shell.BulkSetRolePermissionAsync</c>.
/// </summary>
public sealed partial class BulkRolePermissionViewModel : ViewModelBase
{
    public BulkRolePermissionViewModel(int roleCount, IReadOnlyList<string> permissions)
    {
        RoleCount = roleCount;
        foreach (var p in permissions) Permissions.Add(p);
        SelectedPermission = Permissions.FirstOrDefault();
    }

    /// <summary>How many roles the action will touch — shown in the prompt.</summary>
    public int RoleCount { get; }
    public ObservableCollection<string> Permissions { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string? _selectedPermission;

    /// <summary>True = grant the permission; false = revoke it.</summary>
    [ObservableProperty] private bool _add = true;

    public bool? Result { get; private set; }
    public event Action? Closed;

    private bool CanConfirm() => !string.IsNullOrWhiteSpace(SelectedPermission);

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
