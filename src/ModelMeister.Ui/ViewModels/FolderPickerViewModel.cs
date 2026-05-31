using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Inriver.Users;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// View-model behind the folder-picker chooser dialog (Copy to… / Move to… / bulk variants). Presents a
/// read-only mirror of the work-area folder tree so the admin can pick a destination parent (or the
/// special "(root)" option for a top-level placement). The subtree rooted at <c>exclude</c> is omitted
/// so a folder can never be moved or copied into itself or one of its own descendants.
///
/// When <see cref="AllowScopeSwitch"/> is set, the dialog also offers a scope selector (Shared, or a
/// specific user's personal scope) so the same dialog covers cross-scope copy. The caller reads
/// <see cref="SelectedTarget"/>, <see cref="ToShared"/> and <see cref="TargetUser"/> on confirm and maps
/// them to a <c>FolderPickResult</c>.
/// </summary>
public partial class FolderPickerViewModel : ViewModelBase
{
    /// <summary>Builds the picker. <paramref name="exclude"/> (and its subtree) is omitted from the tree.</summary>
    public FolderPickerViewModel(
        string title,
        IEnumerable<WorkAreaNode> tree,
        WorkAreaNode? exclude,
        bool allowScopeSwitch,
        IEnumerable<UserSummary>? users,
        string? currentUser)
    {
        Title = title;
        AllowScopeSwitch = allowScopeSwitch;
        _toShared = true;

        var excludeId = exclude?.Id;
        foreach (var rowNode in tree)
        {
            var row = BuildRow(rowNode, excludeId, depth: 0);
            if (row is not null) Roots.Add(row);
        }

        if (allowScopeSwitch && users is not null)
            foreach (var u in users.OrderBy(u => u.Username, StringComparer.OrdinalIgnoreCase))
                Users.Add(u);

        // Default the scope to the source's scope so a same-scope copy/move is the no-friction path.
        if (allowScopeSwitch && currentUser is not null && Users.Count > 0)
        {
            var match = Users.FirstOrDefault(u => string.Equals(u.Username, currentUser, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                _toShared = false;
                _targetUser = match;
            }
        }
    }

    /// <summary>Dialog title (e.g. "Copy folder to…").</summary>
    public string Title { get; }

    /// <summary>Whether the destination-scope selector (Shared / Personal:user) is shown.</summary>
    public bool AllowScopeSwitch { get; }

    /// <summary>Root rows of the read-only destination tree (excludes the source subtree).</summary>
    public ObservableCollection<FolderPickerRow> Roots { get; } = [];

    /// <summary>Candidate users for the personal-scope selector (empty when scope switching is off).</summary>
    public ObservableCollection<UserSummary> Users { get; } = [];

    /// <summary>The chosen destination parent row; <c>null</c> means place at the root.</summary>
    [ObservableProperty]
    private FolderPickerRow? _selectedRow;

    /// <summary>True when the destination scope is Shared; false routes to <see cref="TargetUser"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUserPicker))]
    [NotifyPropertyChangedFor(nameof(ToPersonal))]
    private bool _toShared;

    /// <summary>Two-way mirror of <c>!ToShared</c> so the Personal radio button can drive the scope.</summary>
    public bool ToPersonal
    {
        get => !ToShared;
        set
        {
            if (value == !ToShared) return;
            ToShared = !value;
        }
    }

    /// <summary>The chosen personal-scope user (used when <see cref="ToShared"/> is false).</summary>
    [ObservableProperty]
    private UserSummary? _targetUser;

    /// <summary>The selected destination node, or <c>null</c> for the root placement.</summary>
    public WorkAreaNode? SelectedTarget => SelectedRow?.Node;

    /// <summary>Whether the user picker is visible (personal scope chosen and switching allowed).</summary>
    public bool ShowUserPicker => AllowScopeSwitch && !ToShared;

    /// <summary>The dialog result, set just before <see cref="Closed"/> fires.</summary>
    public bool? Result { get; private set; }

    /// <summary>Raised when the dialog should close (Confirm or Cancel).</summary>
    public event Action? Closed;

    [RelayCommand]
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

    /// <summary>Selects the root placement (the "(root)" affordance clears the parent selection).</summary>
    [RelayCommand]
    private void SelectRoot() => SelectedRow = null;

    /// <summary>Recursively wraps a live node into a read-only picker row, dropping the excluded subtree.</summary>
    private static FolderPickerRow? BuildRow(WorkAreaNode node, Guid? excludeId, int depth)
    {
        if (excludeId is { } ex && node.Id == ex) return null;
        var row = new FolderPickerRow(node, depth);
        foreach (var child in node.Children)
        {
            var childRow = BuildRow(child, excludeId, depth + 1);
            if (childRow is not null) row.Children.Add(childRow);
        }
        return row;
    }
}

/// <summary>
/// One read-only row in the folder-picker tree. Wraps a live <see cref="WorkAreaNode"/> for display only
/// (the picker never mutates the source tree) and exposes the handful of members the dialog binds to.
/// </summary>
public sealed class FolderPickerRow
{
    /// <summary>Wraps <paramref name="node"/> at the given <paramref name="depth"/> for indentation.</summary>
    public FolderPickerRow(WorkAreaNode node, int depth)
    {
        Node = node;
        Depth = depth;
        Children = [];
    }

    /// <summary>The live node this row mirrors.</summary>
    public WorkAreaNode Node { get; }

    /// <summary>Nesting depth (drives row indentation in the picker).</summary>
    public int Depth { get; }

    /// <summary>Display name of the wrapped folder.</summary>
    public string Name => Node.Name;

    /// <summary>Full path of the wrapped folder.</summary>
    public string Path => Node.Path;

    /// <summary>Whether the wrapped folder is a saved-query folder.</summary>
    public bool IsQuery => Node.IsQuery;

    /// <summary>Child rows (the excluded subtree is already pruned out during build).</summary>
    public ObservableCollection<FolderPickerRow> Children { get; }
}
