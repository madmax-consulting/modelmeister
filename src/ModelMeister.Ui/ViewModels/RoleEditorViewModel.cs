using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// View-model behind the Create / Edit Role dialog. Collects a name + description and the set of
/// bound permissions (checkbox list of every permission available in the env). On edit, the name is
/// locked (the upsert keys by name; renaming would mean delete + recreate). The caller reads
/// <see cref="Name"/>, <see cref="Description"/> and <see cref="SelectedPermissions"/> on confirm and
/// routes them through <c>Shell.ProvisionRoleAsync</c>.
/// </summary>
public partial class RoleEditorViewModel : ViewModelBase
{
    public RoleEditorViewModel(string? name, string? description, IReadOnlyList<string> selected, IReadOnlyList<string> available, bool isEdit)
    {
        _name = name ?? "";
        _description = description ?? "";
        IsEdit = isEdit;

        var sel = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
        // Union available perms with any already-bound ones (defensive: a role may reference a perm
        // not returned by the catalog). Sort so the list is stable across opens.
        var all = available.Concat(selected).Distinct(StringComparer.OrdinalIgnoreCase)
                           .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
        foreach (var p in all) Permissions.Add(new PermissionToggle(p, sel.Contains(p)));
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _name;

    [ObservableProperty] private string _description;
    [ObservableProperty] private string _validation = "";

    /// <summary>True when editing an existing role; locks the Name field.</summary>
    public bool IsEdit { get; }

    public string Title => IsEdit ? "Edit role" : "New role";

    public ObservableCollection<PermissionToggle> Permissions { get; } = [];

    /// <summary>Names of the currently-checked permissions.</summary>
    public IReadOnlyList<string> SelectedPermissions => Permissions.Where(p => p.IsSelected).Select(p => p.Name).ToList();

    public bool? Result { get; private set; }
    public event Action? Closed;

    private bool CanConfirm() => !string.IsNullOrWhiteSpace(Name);

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        if (string.IsNullOrWhiteSpace(Name)) { Validation = "Name is required."; return; }
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

/// <summary>One toggleable permission row in the role editor's permission list.</summary>
public partial class PermissionToggle : ObservableObject
{
    public PermissionToggle(string name, bool isSelected)
    {
        Name = name;
        _isSelected = isSelected;
    }

    public string Name { get; }
    [ObservableProperty] private bool _isSelected;
}
