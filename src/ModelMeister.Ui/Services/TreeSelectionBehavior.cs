using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using ModelMeister.Ui.ViewModels;

namespace ModelMeister.Ui.Services;

/// <summary>
/// Mirrors a multi-select <see cref="TreeView"/>'s <c>SelectedItems</c> into a bound view-model
/// <c>ObservableCollection&lt;WorkAreaNode&gt;</c>. Avalonia 11's <see cref="TreeView.SelectedItems"/>
/// is an <see cref="IList"/> the control owns and is not reliably two-way bindable, so this attached
/// behavior subscribes to <see cref="TreeView.SelectionChanged"/> and copies the current selection
/// into the VM collection on every change (control → VM is sufficient for the bulk commands).
///
/// Attach with <c>svc:TreeSelectionBehavior.Nodes="{Binding SelectedNodes}"</c> on the
/// <see cref="TreeView"/>. Set <c>SelectionMode="Multiple"</c> on the same control. Models the same
/// house style as <see cref="GridSelectionBehavior"/> (a static <see cref="AttachedProperty{TValue}"/>
/// plus a class handler), keeping the wiring declarative in XAML.
/// </summary>
public static class TreeSelectionBehavior
{
    /// <summary>The VM collection the control's selection is mirrored into (control → VM).</summary>
    public static readonly AttachedProperty<ObservableCollection<WorkAreaNode>?> NodesProperty =
        AvaloniaProperty.RegisterAttached<TreeView, ObservableCollection<WorkAreaNode>?>("Nodes", typeof(TreeSelectionBehavior));

    /// <summary>Sets the mirrored-selection collection (XAML setter for <see cref="NodesProperty"/>).</summary>
    public static void SetNodes(AvaloniaObject element, ObservableCollection<WorkAreaNode>? value) => element.SetValue(NodesProperty, value);

    /// <summary>Gets the mirrored-selection collection (XAML getter for <see cref="NodesProperty"/>).</summary>
    public static ObservableCollection<WorkAreaNode>? GetNodes(AvaloniaObject element) => element.GetValue(NodesProperty);

    // Per-tree handler so a closed page's tree is collectible. We keep one handler instance per control
    // and detach it before re-attaching when the bound collection is swapped.
    private static readonly ConditionalWeakTable<TreeView, SelectionWatch> Watches = new();

    private sealed class SelectionWatch
    {
        public ObservableCollection<WorkAreaNode>? Target;
    }

    static TreeSelectionBehavior()
    {
        NodesProperty.Changed.AddClassHandler<TreeView>((tree, e) =>
        {
            var watch = Watches.GetValue(tree, static t =>
            {
                var w = new SelectionWatch();
                t.SelectionChanged += (_, _) => Mirror(t);
                return w;
            });
            watch.Target = e.NewValue as ObservableCollection<WorkAreaNode>;
            // Mirror once so a pre-existing selection lands in a freshly-bound collection.
            Mirror(tree);
        });
    }

    private static void Mirror(TreeView tree)
    {
        if (!Watches.TryGetValue(tree, out var watch) || watch.Target is not { } target) return;

        var selected = (tree.SelectedItems as IEnumerable)?.OfType<WorkAreaNode>().ToList() ?? [];

        // Mirror the control's selection into the VM collection AND keep each node's IsSelected flag in sync
        // (DESIGN §D): nodes leaving the selection are cleared, nodes entering it are set. This drives the
        // per-row multi-select visual without a second binding.
        var leaving = target.Where(n => !selected.Contains(n)).ToList();
        foreach (var node in leaving) node.IsSelected = false;

        target.Clear();
        foreach (var node in selected)
        {
            node.IsSelected = true;
            target.Add(node);
        }
    }
}
