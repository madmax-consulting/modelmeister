using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ModelMeister.Ui.Models;

namespace ModelMeister.Ui.Services;

/// <summary>
/// Adds shift-click range selection to a <see cref="DataGrid"/>'s left-hand checkbox column. Enable
/// with <c>svc:GridSelectionBehavior.Enable="True"</c> on the grid. The selection checkboxes must be
/// tagged <see cref="CheckBoxTag"/> (the shared checkbox-column template does this) so we only react
/// to them and never to a data checkbox elsewhere in the row.
///
/// A plain click sets the range anchor and lets the checkbox toggle normally; a Shift+click sets
/// every row between the anchor and the clicked row to the clicked row's new state. The range walks
/// the grid's current view order, so it honours the active sort and column filters.
/// </summary>
public static class GridSelectionBehavior
{
    /// <summary>Tag applied to the row-selection checkbox so the behavior can recognise it.</summary>
    public const string CheckBoxTag = "__rowselect";

    public static readonly AttachedProperty<bool> EnableProperty =
        AvaloniaProperty.RegisterAttached<DataGrid, bool>("Enable", typeof(GridSelectionBehavior));

    public static void SetEnable(AvaloniaObject element, bool value) => element.SetValue(EnableProperty, value);
    public static bool GetEnable(AvaloniaObject element) => element.GetValue(EnableProperty);

    // Per-grid shift-range anchor, held as a row reference (not an index) so it stays correct when the
    // user re-sorts or filters between clicks. ConditionalWeakTable so a closed page's grid is collectible.
    private static readonly ConditionalWeakTable<DataGrid, AnchorBox> Anchors = new();
    private sealed class AnchorBox { public SelectableRow? Row; }

    static GridSelectionBehavior()
    {
        EnableProperty.Changed.AddClassHandler<DataGrid>((grid, e) =>
        {
            if (e.NewValue is true)
                grid.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
            else
                grid.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
        });
    }

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not DataGrid grid) return;

        var cb = (e.Source as Visual)?.FindAncestorOfType<CheckBox>(includeSelf: true);
        if (cb is null || cb.Tag as string != CheckBoxTag || cb.DataContext is not SelectableRow row) return;

        var items = OrderedRows(grid);
        var index = items.IndexOf(row);
        if (index < 0) return;

        var anchor = Anchors.GetValue(grid, static _ => new AnchorBox());
        var anchorIndex = anchor.Row is null ? -1 : items.IndexOf(anchor.Row);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (shift && anchorIndex >= 0)
        {
            var target = !row.IsSelected; // the state this click would have toggled the box to
            var lo = Math.Min(anchorIndex, index);
            var hi = Math.Max(anchorIndex, index);
            for (var i = lo; i <= hi; i++) items[i].IsSelected = target;
            e.Handled = true; // we set the range ourselves; suppress the checkbox's own single toggle
        }
        else
        {
            anchor.Row = row; // plain click → new anchor; let the checkbox toggle naturally
        }
    }

    /// <summary>
    /// Rows in current display order. When <see cref="GridFilters"/> has wrapped the grid, its
    /// <c>ItemsSource</c> is the sort/filter-aware <c>DataGridCollectionView</c>; otherwise it is the
    /// raw <c>ObservableCollection</c>. Either enumerates in the order the user sees.
    /// </summary>
    private static List<SelectableRow> OrderedRows(DataGrid grid)
        => (grid.ItemsSource as IEnumerable)?.OfType<SelectableRow>().ToList() ?? new List<SelectableRow>();
}
