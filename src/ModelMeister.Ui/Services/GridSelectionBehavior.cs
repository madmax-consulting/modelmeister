using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ModelMeister.Ui.Models;
using ModelMeister.Ui.ViewModels;

namespace ModelMeister.Ui.Services;

/// <summary>
/// Binds a <see cref="DataGrid"/>'s left-hand checkbox column to a <see cref="RowSelectionModel"/> and
/// adds shift-click range selection. Attach with
/// <c>svc:GridSelectionBehavior.Selection="{Binding Selection}"</c> on the grid. The selection
/// checkboxes must be tagged <see cref="CheckBoxTag"/> (the shared checkbox-column template does this)
/// so we only react to them and never to a data checkbox elsewhere in the row.
///
/// On attach the behavior feeds the model the grid's <em>visible</em> rows (filter-aware) via
/// <see cref="RowSelectionModel.BindView"/> and re-triggers <see cref="RowSelectionModel.Recompute"/>
/// whenever the active filter changes (the grid swaps its <c>ItemsSource</c>, or the wrapping
/// <c>DataGridCollectionView</c> raises a refresh). This is what makes "select all" honour the filter.
///
/// A plain click sets the range anchor and lets the checkbox toggle normally; a Shift+click sets every
/// row between the anchor and the clicked row to the clicked row's new state. The range walks the
/// grid's current view order, so it honours the active sort and column filters.
/// </summary>
public static class GridSelectionBehavior
{
    /// <summary>Tag applied to the row-selection checkbox so the behavior can recognise it.</summary>
    public const string CheckBoxTag = "__rowselect";

    public static readonly AttachedProperty<RowSelectionModel?> SelectionProperty =
        AvaloniaProperty.RegisterAttached<DataGrid, RowSelectionModel?>("Selection", typeof(GridSelectionBehavior));

    public static void SetSelection(AvaloniaObject element, RowSelectionModel? value) => element.SetValue(SelectionProperty, value);
    public static RowSelectionModel? GetSelection(AvaloniaObject element) => element.GetValue(SelectionProperty);

    // Per-grid shift-range anchor, held as a row reference (not an index) so it stays correct when the
    // user re-sorts or filters between clicks. ConditionalWeakTable so a closed page's grid is collectible.
    private static readonly ConditionalWeakTable<DataGrid, AnchorBox> Anchors = new();
    private sealed class AnchorBox { public SelectableRow? Row; }

    // Per-grid view watch: keeps the model's "visible rows" recompute in sync with filter changes.
    private static readonly ConditionalWeakTable<DataGrid, ViewWatch> Watches = new();
    private sealed class ViewWatch
    {
        public RowSelectionModel Model = null!;
        public INotifyCollectionChanged? View;
        public NotifyCollectionChangedEventHandler ViewHandler = null!;
        public EventHandler<AvaloniaPropertyChangedEventArgs> GridHandler = null!;
    }

    static GridSelectionBehavior()
    {
        SelectionProperty.Changed.AddClassHandler<DataGrid>((grid, e) =>
        {
            grid.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
            DetachViewWatch(grid);
            if (e.NewValue is RowSelectionModel model)
            {
                grid.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
                model.BindView(() => OrderedRows(grid), () => GridFilters.HasAnyFilter(grid));
                AttachViewWatch(grid, model);
            }
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

    // --- view watch: recompute the model when the grid's filter / view changes ---

    private static void AttachViewWatch(DataGrid grid, RowSelectionModel model)
    {
        var w = new ViewWatch { Model = model };
        w.ViewHandler = (_, _) => model.Recompute();
        w.GridHandler = (_, e) =>
        {
            if (e.Property == ItemsControl.ItemsSourceProperty)
            {
                RebindView(grid, w);
                model.Recompute();
            }
        };
        Watches.AddOrUpdate(grid, w);
        grid.PropertyChanged += w.GridHandler;
        RebindView(grid, w);
    }

    private static void RebindView(DataGrid grid, ViewWatch w)
    {
        if (w.View is not null) w.View.CollectionChanged -= w.ViewHandler;
        w.View = grid.ItemsSource as INotifyCollectionChanged;
        if (w.View is not null) w.View.CollectionChanged += w.ViewHandler;
    }

    private static void DetachViewWatch(DataGrid grid)
    {
        if (!Watches.TryGetValue(grid, out var w)) return;
        grid.PropertyChanged -= w.GridHandler;
        if (w.View is not null) w.View.CollectionChanged -= w.ViewHandler;
        Watches.Remove(grid);
    }
}
