using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using ModelMeister.Ui.Models;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// Tracks multi-row checkbox selection over a rows collection of <see cref="SelectableRow"/>. A
/// feature VM constructs one of these around its <c>ObservableCollection</c> and exposes it as a
/// <c>Selection</c> property. The grid's header checkbox binds two-way to <see cref="AllSelected"/>
/// (tri-state — null shows the indeterminate dash when only some rows are checked); the bulk action
/// button binds its enabled state to <see cref="HasSelection"/> and its label to
/// <see cref="SelectedCount"/>; and bulk Delete / Promote commands enumerate <see cref="SelectedRows"/>.
/// </summary>
public sealed partial class RowSelectionModel : ObservableObject
{
    private readonly IEnumerable _rows;
    // Guards the two-way feedback loop between the header checkbox (AllSelected) and the per-row
    // IsSelected flags: SetAll mutates rows → row PropertyChanged → Recompute → AllSelected, and we
    // must not treat that programmatic AllSelected change as a user select-all/none toggle.
    private bool _sync;

    public RowSelectionModel(IEnumerable rows)
    {
        _rows = rows;
        if (rows is INotifyCollectionChanged incc) incc.CollectionChanged += OnCollectionChanged;
        foreach (var r in Items) Hook(r);
        Recompute();
    }

    private IEnumerable<SelectableRow> Items => _rows.OfType<SelectableRow>();

    /// <summary>Tri-state header value: true = all checked, false = none, null = some (indeterminate).</summary>
    [ObservableProperty] private bool? _allSelected = false;

    /// <summary>Number of currently checked rows. Backs the "Delete selected (N)" / "Promote selected (N)" label.</summary>
    [ObservableProperty] private int _selectedCount;

    /// <summary>True when at least one row is checked — gates the bulk action button.</summary>
    public bool HasSelection => SelectedCount > 0;

    /// <summary>Snapshot of the currently checked rows, in collection order.</summary>
    public IReadOnlyList<SelectableRow> SelectedRows => Items.Where(r => r.IsSelected).ToList();

    /// <summary>Checked rows of a concrete row type (convenience for typed bulk commands).</summary>
    public IReadOnlyList<T> SelectedOf<T>() where T : SelectableRow => Items.OfType<T>().Where(r => r.IsSelected).ToList();

    partial void OnAllSelectedChanged(bool? value)
    {
        // Programmatic updates (from Recompute) are gated by _sync. A user click on a non-three-state
        // checkbox only ever yields true/false; treat any null defensively as "select all".
        if (_sync) return;
        SetAll(value ?? true);
    }

    /// <summary>Check or uncheck every row.</summary>
    public void SetAll(bool value)
    {
        _sync = true;
        foreach (var r in Items) r.IsSelected = value;
        _sync = false;
        Recompute();
    }

    /// <summary>Uncheck every row (called after a successful bulk action / reload).</summary>
    public void Clear() => SetAll(false);

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null) foreach (var o in e.OldItems.OfType<SelectableRow>()) Unhook(o);
        if (e.NewItems is not null) foreach (var n in e.NewItems.OfType<SelectableRow>()) Hook(n);
        // Reset (Clear) carries no item lists; re-hook whatever survives. Discarded rows keep no
        // back-reference to this model, so their dangling subscription is collectible — no leak.
        if (e.Action == NotifyCollectionChangedAction.Reset) foreach (var r in Items) Hook(r);
        Recompute();
    }

    private void Hook(SelectableRow r)   { r.PropertyChanged -= OnRowChanged; r.PropertyChanged += OnRowChanged; }
    private void Unhook(SelectableRow r) => r.PropertyChanged -= OnRowChanged;

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectableRow.IsSelected) && !_sync) Recompute();
    }

    private void Recompute()
    {
        var items = Items.ToList();
        var sel = items.Count(r => r.IsSelected);
        SelectedCount = sel;
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
