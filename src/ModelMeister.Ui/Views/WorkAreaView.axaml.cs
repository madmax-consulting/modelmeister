using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using ModelMeister.Ui.ViewModels;

namespace ModelMeister.Ui.Views;

/// <summary>
/// Work-area management view. The XAML provides the toolbar, tree, full context menu, breadcrumb and
/// detail pane; this code-behind adds the imperative pieces that compiled bindings can't express:
/// drag-and-drop re-parent/copy of folder rows, selecting the right-clicked row before its context
/// menu acts, and focusing the search box on <c>Ctrl+F</c>.
/// </summary>
public partial class WorkAreaView : UserControl
{
    /// <summary>Custom clipboard/drag format carrying a dragged <see cref="WorkAreaNode"/>.</summary>
    private const string NodeFormat = "workarea-node";

    private Point _pressPoint;
    private WorkAreaNode? _pressedNode;
    private bool _dragging;

    public WorkAreaView()
    {
        AvaloniaXamlLoader.Load(this);

        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private WorkAreaViewModel? Vm => DataContext as WorkAreaViewModel;

    /// <summary>Resolve the <see cref="WorkAreaNode"/> for the row under a pointer/visual, if any.</summary>
    private static WorkAreaNode? NodeFor(object? source)
    {
        if (source is not Visual v) return null;
        var item = v.FindAncestorOfType<TreeViewItem>();
        return item?.DataContext as WorkAreaNode;
    }

    // ----- right-click row selection -----

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var node = NodeFor(e.Source);
        var point = e.GetCurrentPoint(this);

        // On right-click, make the row the primary selection so context-menu items (which act on
        // Selected / the bound node) target what the user clicked.
        if (point.Properties.IsRightButtonPressed && node is not null)
        {
            if (Vm is not null) Vm.Selected = node;
        }

        // Record a potential drag start (left button on a real row).
        if (point.Properties.IsLeftButtonPressed && node is not null)
        {
            _pressedNode = node;
            _pressPoint = point.Position;
            _dragging = false;
        }
        else
        {
            _pressedNode = null;
        }
    }

    private async void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressedNode is null || _dragging) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { _pressedNode = null; return; }

        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _pressPoint.X) < 4 && Math.Abs(pos.Y - _pressPoint.Y) < 4) return;

        _dragging = true;
        var data = new DataObject();
        data.Set(NodeFormat, _pressedNode);
        try
        {
            await DragDrop.DoDragDrop(e, data, DragDropEffects.Move | DragDropEffects.Copy);
        }
        catch (Exception) { /* drag aborted — ignore */ }
        finally
        {
            _dragging = false;
            _pressedNode = null;
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging) _pressedNode = null;
    }

    // ----- drag-and-drop -----

    private static WorkAreaNode? Dragged(DragEventArgs e) =>
        e.Data.Get(NodeFormat) as WorkAreaNode;

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var dragged = Dragged(e);
        var target = NodeFor(e.Source);

        if (dragged is null || ReferenceEquals(dragged, target) || IsSelfOrDescendant(target, dragged))
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.DragEffects = e.KeyModifiers.HasFlag(KeyModifiers.Control)
            ? DragDropEffects.Copy
            : DragDropEffects.Move;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        var dragged = Dragged(e);
        if (dragged is null || Vm is null) return;

        var target = NodeFor(e.Source);
        if (ReferenceEquals(dragged, target) || IsSelfOrDescendant(target, dragged)) return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            await Vm.CopyOntoAsync(dragged, target);
        else
            await Vm.MoveOntoAsync(dragged, target);
    }

    /// <summary>True when <paramref name="candidate"/> is <paramref name="ancestor"/> itself or sits beneath it.</summary>
    private static bool IsSelfOrDescendant(WorkAreaNode? candidate, WorkAreaNode ancestor)
    {
        for (var n = candidate; n is not null; n = n.Parent)
            if (ReferenceEquals(n, ancestor)) return true;
        return false;
    }

    // ----- keyboard -----

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+F focuses the search box (the other shortcuts are declared as KeyBindings in XAML).
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            this.FindControl<TextBox>("SearchBox")?.Focus();
            e.Handled = true;
        }
    }
}
