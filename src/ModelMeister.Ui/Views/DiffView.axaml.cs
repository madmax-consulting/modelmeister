using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using ModelMeister.Ui.ViewModels;

namespace ModelMeister.Ui.Views;

/// <summary>
/// Compare page. Forwards the per-row "include" checkbox toggle to the view-model, and wires
/// Expand-all/Collapse-all buttons that have to walk the visual tree (the bindings can't reach
/// the implicit <see cref="TreeViewItem"/>s).
/// </summary>
public partial class DiffView : UserControl
{
    public DiffView() => AvaloniaXamlLoader.Load(this);

    private void OnIncludeToggle(object? sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: ChangeRow row }) return;
        if (DataContext is not DiffViewModel vm) return;
        vm.ToggleExcluded(row);
    }

    private void OnExpandAllClick(object? sender, RoutedEventArgs e) => SetAllExpanded(true);
    private void OnCollapseAllClick(object? sender, RoutedEventArgs e) => SetAllExpanded(false);

    private void SetAllExpanded(bool expanded)
    {
        var tree = this.FindControl<TreeView>("ChangeTree");
        if (tree is null) return;

        foreach (var item in tree.GetVisualDescendants().OfType<TreeViewItem>())
            item.IsExpanded = expanded;
    }
}
