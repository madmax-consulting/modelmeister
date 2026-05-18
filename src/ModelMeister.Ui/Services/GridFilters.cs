using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Collections;
using Avalonia.Controls;

namespace ModelMeister.Ui.Services;

/// <summary>
/// Wires Excel-style per-column filtering onto a <see cref="DataGrid"/>. Authors place a
/// <c>FilterableHeader</c> inside each column's <c>Header</c> property; the header registers
/// itself here and the grid's <see cref="DataGrid.ItemsSource"/> is wrapped in a
/// <see cref="DataGridCollectionView"/> whose predicate ANDs every column's substring filter
/// against the matching property on each row (reflection, ordinal-ignore-case).
/// </summary>
public static class GridFilters
{
    private sealed class State
    {
        public DataGridCollectionView? View;
        public IEnumerable? OriginalSource;
        public readonly Dictionary<string, string> Filters = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Positive set-membership filters (row passes only if value ∈ allowed). Excel-style.</summary>
        public readonly Dictionary<string, HashSet<string>> SetFilters = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Negative set-membership filters (row fails if value ∈ excluded). Bucket-chart toggle.</summary>
        public readonly Dictionary<string, HashSet<string>> ExcludeFilters = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DataGrid, State> States = new();

    /// <summary>Update the filter for <paramref name="propertyPath"/> on the given grid. Empty / null
    /// removes the filter for that path.</summary>
    public static void SetFilter(DataGrid grid, string propertyPath, string? text)
    {
        var st = EnsureState(grid);
        var changed = false;
        if (string.IsNullOrEmpty(text))
            changed = st.Filters.Remove(propertyPath);
        else
        {
            st.Filters.TryGetValue(propertyPath, out var existing);
            if (!string.Equals(existing, text, StringComparison.Ordinal))
            {
                st.Filters[propertyPath] = text;
                changed = true;
            }
        }
        if (!changed) return;
        EnsureWrapped(grid, st);
        st.View?.Refresh();
    }

    /// <summary>Drop every column filter on the given grid (used by a "clear all" affordance).</summary>
    public static void ClearAll(DataGrid grid)
    {
        if (!States.TryGetValue(grid, out var st)) return;
        if (st.Filters.Count == 0 && st.SetFilters.Count == 0 && st.ExcludeFilters.Count == 0) return;
        st.Filters.Clear();
        st.SetFilters.Clear();
        st.ExcludeFilters.Clear();
        st.View?.Refresh();
    }

    /// <summary>Excel-style multi-select filter: row passes only if its <paramref name="propertyPath"/>
    /// value (string form) is in <paramref name="allowed"/>. Pass <c>null</c> or an empty set to clear.</summary>
    public static void SetAllowedValues(DataGrid grid, string propertyPath, IReadOnlyCollection<string>? allowed)
    {
        var st = EnsureState(grid);
        if (allowed is null || allowed.Count == 0)
        {
            if (!st.SetFilters.Remove(propertyPath)) return;
        }
        else
        {
            st.SetFilters[propertyPath] = new HashSet<string>(allowed, StringComparer.OrdinalIgnoreCase);
        }
        EnsureWrapped(grid, st);
        st.View?.Refresh();
    }

    /// <summary>Read-back of the current set filter (for headers that need to restore checkbox state).</summary>
    public static IReadOnlySet<string>? GetAllowedValues(DataGrid grid, string propertyPath)
        => States.TryGetValue(grid, out var st) && st.SetFilters.TryGetValue(propertyPath, out var set)
            ? set
            : null;

    /// <summary>Negative-set filter: row fails if its property value is in <paramref name="excluded"/>.
    /// Used by the bottom-chart bucket toggle to hide a whole bucket from the grid.</summary>
    public static void SetExcludedValues(DataGrid grid, string propertyPath, IReadOnlyCollection<string>? excluded)
    {
        var st = EnsureState(grid);
        if (excluded is null || excluded.Count == 0)
        {
            if (!st.ExcludeFilters.Remove(propertyPath)) return;
        }
        else
        {
            st.ExcludeFilters[propertyPath] = new HashSet<string>(excluded, StringComparer.OrdinalIgnoreCase);
        }
        EnsureWrapped(grid, st);
        st.View?.Refresh();
    }

    /// <summary>Look up the current filter text for one property path (so the textbox can restore it).</summary>
    public static string? GetFilter(DataGrid grid, string propertyPath)
        => States.TryGetValue(grid, out var st) && st.Filters.TryGetValue(propertyPath, out var t) ? t : null;

    private static State EnsureState(DataGrid grid)
    {
        if (!States.TryGetValue(grid, out var st))
        {
            st = new State();
            States.Add(grid, st);
            grid.PropertyChanged += (_, e) =>
            {
                if (e.Property == ItemsControl.ItemsSourceProperty && st.Filters.Count > 0)
                    EnsureWrapped(grid, st);
            };
        }
        return st;
    }

    private static void EnsureWrapped(DataGrid grid, State st)
    {
        var current = grid.ItemsSource;
        if (current is null) return;
        if (current is DataGridCollectionView v && ReferenceEquals(v, st.View)) return;
        if (current is DataGridCollectionView already)
        {
            st.View = already;
            already.Filter = item => RowPasses(st, item);
            return;
        }
        if (current is not IEnumerable enumerable) return;
        st.OriginalSource = enumerable;
        var view = new DataGridCollectionView(enumerable)
        {
            Filter = item => RowPasses(st, item),
        };
        st.View = view;
        grid.ItemsSource = view;
    }

    private static bool RowPasses(State st, object? item)
    {
        if (item is null) return false;
        foreach (var (path, needle) in st.Filters)
        {
            var actual = ReadPropertyChain(item, path)?.ToString();
            if (string.IsNullOrEmpty(actual)) return false;
            if (actual.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) return false;
        }
        foreach (var (path, allowed) in st.SetFilters)
        {
            var actual = ReadPropertyChain(item, path)?.ToString() ?? "";
            if (!allowed.Contains(actual)) return false;
        }
        foreach (var (path, excluded) in st.ExcludeFilters)
        {
            var actual = ReadPropertyChain(item, path)?.ToString() ?? "";
            if (excluded.Contains(actual)) return false;
        }
        return true;
    }

    private static object? ReadPropertyChain(object root, string path)
    {
        object? current = root;
        foreach (var part in path.Split('.'))
        {
            if (current is null) return null;
            var prop = current.GetType().GetProperty(part);
            if (prop is null) return null;
            current = prop.GetValue(current);
        }
        return current;
    }
}
