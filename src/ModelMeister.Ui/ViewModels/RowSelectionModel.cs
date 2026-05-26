using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Ui.Models;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// Tracks multi-row checkbox selection over a rows collection of <see cref="SelectableRow"/>. A
/// feature VM constructs one of these around its <c>ObservableCollection</c> and exposes it as a
/// <c>Selection</c> property. The grid's header checkbox binds two-way to <see cref="AllSelected"/>
/// (tri-state — null shows the indeterminate dash when only some rows are checked); the bulk action
/// button binds its enabled state to <see cref="HasSelection"/> and its label to
/// <see cref="SelectedCount"/>; and bulk Delete / Promote commands enumerate <see cref="SelectedRows"/>.
///
/// <para><b>View-aware (safety):</b> when a grid is filtered (<see cref="Services.GridFilters"/> wraps
/// its <c>ItemsSource</c> in a <c>DataGridCollectionView</c>), <see cref="Services.GridSelectionBehavior"/>
/// feeds this model the <em>visible</em> rows via <see cref="BindView"/>. From then on every count,
/// the tri-state header, and <see cref="SelectedRows"/> compute over what the user can actually see —
/// so "select all" never checks a hidden row and a bulk action can never touch one. Selecting checks
/// the visible rows only; clearing clears <em>every</em> row (including filtered-out ones) so a hidden
/// still-checked row can't silently return to the selection when the filter is lifted.</para>
/// </summary>
public sealed partial class RowSelectionModel : ObservableObject
{
    private readonly IEnumerable _rows;
    // Guards the two-way feedback loop between the header checkbox (AllSelected) and the per-row
    // IsSelected flags: SetAll mutates rows → row PropertyChanged → Recompute → AllSelected, and we
    // must not treat that programmatic AllSelected change as a user select-all/none toggle.
    private bool _sync;
    // The grid's current-view + filter-state, supplied by GridSelectionBehavior.BindView. Null for
    // un-filtered sub-grids (e.g. the CVL value editor), where the raw collection is the visible set.
    private Func<IReadOnlyList<SelectableRow>>? _visible;
    private Func<bool>? _filtered;
    // Recompute coalescing: a clear-then-add-N reload raises N collection-change events; instead of
    // recomputing per item (O(n²)), we set a dirty flag and recompute once on the next dispatcher turn.
    private bool _recomputeQueued;

    public RowSelectionModel(IEnumerable rows)
    {
        _rows = rows;
        if (rows is INotifyCollectionChanged incc) incc.CollectionChanged += OnCollectionChanged;
        foreach (var r in AllRows) Hook(r);
        RecomputeCore();
    }

    /// <summary>Every row in the backing collection, regardless of any active filter.</summary>
    private IEnumerable<SelectableRow> AllRows => _rows.OfType<SelectableRow>();

    /// <summary>The rows currently visible in the grid (filter-aware). Falls back to the raw
    /// collection when no view is bound — e.g. an un-filtered dialog sub-grid.</summary>
    private IReadOnlyList<SelectableRow> VisibleRows => _visible?.Invoke() ?? AllRows.ToList();

    /// <summary>Tri-state header value: true = all visible checked, false = none, null = some (indeterminate).</summary>
    [ObservableProperty] private bool? _allSelected = false;

    /// <summary>Number of currently checked visible rows. Backs the "Delete selected (N)" / "Promote selected (N)" label.</summary>
    [ObservableProperty] private int _selectedCount;

    /// <summary>True when a column / global filter is hiding some rows — surfaced as "(filtered)" in the action bar.</summary>
    [ObservableProperty] private bool _filteredView;

    /// <summary>True when at least one visible row is checked — gates the bulk action button.</summary>
    public bool HasSelection => SelectedCount > 0;

    /// <summary>Snapshot of the currently checked <em>visible</em> rows, in display order.</summary>
    public IReadOnlyList<SelectableRow> SelectedRows => VisibleRows.Where(r => r.IsSelected).ToList();

    /// <summary>Checked visible rows of a concrete row type (convenience for typed bulk commands).</summary>
    public IReadOnlyList<T> SelectedOf<T>() where T : SelectableRow => VisibleRows.OfType<T>().Where(r => r.IsSelected).ToList();

    /// <summary>
    /// Bind the grid's current-view + filter-state providers so all selection math becomes filter-aware.
    /// Called once by <see cref="Services.GridSelectionBehavior"/> when its <c>Selection</c> attached
    /// property is set; the behavior re-triggers <see cref="Recompute"/> whenever the filter changes.
    /// </summary>
    public void BindView(Func<IReadOnlyList<SelectableRow>> visible, Func<bool> filtered)
    {
        _visible = visible;
        _filtered = filtered;
        Recompute();
    }

    partial void OnAllSelectedChanged(bool? value)
    {
        // Programmatic updates (from Recompute) are gated by _sync. A user click on a non-three-state
        // checkbox only ever yields true/false; treat any null defensively as "select all".
        if (_sync) return;
        SetAll(value ?? true);
    }

    /// <summary>Check the visible rows (<paramref name="value"/> true) or clear <em>every</em> row,
    /// including filtered-out ones (false) — so a hidden checked row can't silently come back selected.</summary>
    public void SetAll(bool value)
    {
        _sync = true;
        if (value) foreach (var r in VisibleRows) r.IsSelected = true;
        else       foreach (var r in AllRows)     r.IsSelected = false;
        _sync = false;
        Recompute();
    }

    /// <summary>Uncheck every row (called after a successful bulk action / reload, and by the bar's Clear).</summary>
    [RelayCommand] public void Clear() => SetAll(false);

    /// <summary>Check every currently-visible row (the bar's "select all visible" / Ctrl+A affordance).</summary>
    [RelayCommand] private void SelectAllVisible() => SetAll(true);

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null) foreach (var o in e.OldItems.OfType<SelectableRow>()) Unhook(o);
        if (e.NewItems is not null) foreach (var n in e.NewItems.OfType<SelectableRow>()) Hook(n);
        // Reset (Clear) carries no item lists; re-hook whatever survives. Discarded rows keep no
        // back-reference to this model, so their dangling subscription is collectible — no leak.
        if (e.Action == NotifyCollectionChangedAction.Reset) foreach (var r in AllRows) Hook(r);
        Recompute();
    }

    private void Hook(SelectableRow r)   { r.PropertyChanged -= OnRowChanged; r.PropertyChanged += OnRowChanged; }
    private void Unhook(SelectableRow r) => r.PropertyChanged -= OnRowChanged;

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectableRow.IsSelected) && !_sync) Recompute();
    }

    /// <summary>Queue a coalesced recompute. Many rapid changes (a bulk reload, a filter refresh)
    /// collapse into a single <see cref="RecomputeCore"/> on the next dispatcher turn.</summary>
    public void Recompute()
    {
        if (_recomputeQueued) return;
        _recomputeQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _recomputeQueued = false;
            RecomputeCore();
        }, DispatcherPriority.Background);
    }

    private void RecomputeCore()
    {
        var items = VisibleRows;
        var sel = items.Count(r => r.IsSelected);
        SelectedCount = sel;
        FilteredView = _filtered?.Invoke() ?? false;
        _sync = true;
        AllSelected = items.Count == 0 ? false
                    : sel == 0          ? false
                    : sel == items.Count ? true
                    : null;
        _sync = false;
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedRows));
    }
}
