using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.Controls;

/// <summary>
/// Column-header filter with two affordances: an inline substring filter (same as
/// <see cref="FilterableHeader"/>) and an Excel-style checkbox dropdown. The substring filter writes
/// to <see cref="GridFilters.SetFilter"/>; the checkbox flyout writes to
/// <see cref="GridFilters.SetAllowedValues"/>. Distinct values for the checklist are recomputed
/// every time the flyout opens so the list stays current as the underlying rows collection changes.
/// </summary>
public partial class ChecklistFilterHeader : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<ChecklistFilterHeader, string>(nameof(Title), defaultValue: "");

    public static readonly StyledProperty<string> PathProperty =
        AvaloniaProperty.Register<ChecklistFilterHeader, string>(nameof(Path), defaultValue: "");

    public static readonly StyledProperty<string?> FilterTextProperty =
        AvaloniaProperty.Register<ChecklistFilterHeader, string?>(nameof(FilterText));

    public static readonly StyledProperty<string> SearchProperty =
        AvaloniaProperty.Register<ChecklistFilterHeader, string>(nameof(Search), defaultValue: "");

    public static readonly StyledProperty<bool> HasActiveFilterProperty =
        AvaloniaProperty.Register<ChecklistFilterHeader, bool>(nameof(HasActiveFilter));

    public static readonly StyledProperty<bool> IsEmptyProperty =
        AvaloniaProperty.Register<ChecklistFilterHeader, bool>(nameof(IsEmpty));

    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Path { get => GetValue(PathProperty); set => SetValue(PathProperty, value); }
    public string? FilterText { get => GetValue(FilterTextProperty); set => SetValue(FilterTextProperty, value); }
    public string Search { get => GetValue(SearchProperty); set => SetValue(SearchProperty, value); }
    public bool HasActiveFilter { get => GetValue(HasActiveFilterProperty); private set => SetValue(HasActiveFilterProperty, value); }
    public bool IsEmpty { get => GetValue(IsEmptyProperty); private set => SetValue(IsEmptyProperty, value); }

    public ObservableCollection<FilterOption> Options { get; } = new();
    public ObservableCollection<FilterOption> FilteredOptions { get; } = new();

    private DataGrid? _grid;
    private bool _suppressSync;

    public ChecklistFilterHeader()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _grid = this.FindAncestorOfType<DataGrid>();
        if (_grid is null) return;

        // Restore any previously-set substring filter so re-attach after virtualization keeps it.
        var existing = GridFilters.GetFilter(_grid, Path);
        if (!string.IsNullOrEmpty(existing) && FilterText != existing)
            FilterText = existing;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FilterTextProperty)
        {
            if (_grid is null) _grid = this.FindAncestorOfType<DataGrid>();
            if (_grid is not null && !string.IsNullOrEmpty(Path))
                GridFilters.SetFilter(_grid, Path, change.GetNewValue<string?>());
        }
        else if (change.Property == SearchProperty)
        {
            RebuildFiltered();
        }
    }

    /// <summary>Repopulate the checkbox list right before the flyout opens so it always reflects
    /// the current contents of the grid (the row collection mutates after Compare runs).</summary>
    private void OnFilterButtonClick(object? sender, RoutedEventArgs e)
    {
        RebuildOptions();
    }

    private void RebuildOptions()
    {
        if (_grid is null) _grid = this.FindAncestorOfType<DataGrid>();
        if (_grid is null) return;

        // Read from the wrapped DataGridCollectionView's underlying source if present; otherwise
        // the raw ItemsSource. We want every distinct value, not just rows that survived current filters.
        IEnumerable? source = _grid.ItemsSource;
        if (source is Avalonia.Collections.DataGridCollectionView view)
            source = view.SourceCollection as IEnumerable ?? view;

        var values = (source ?? Array.Empty<object>())
            .Cast<object?>()
            .Where(x => x is not null)
            .Select(x => ReadPath(x!, Path)?.ToString() ?? "")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var allowed = GridFilters.GetAllowedValues(_grid, Path);

        foreach (var o in Options) o.PropertyChanged -= OnOptionChanged;
        Options.Clear();
        foreach (var v in values)
        {
            var opt = new FilterOption(v) { IsChecked = allowed is null || allowed.Contains(v) };
            opt.PropertyChanged += OnOptionChanged;
            Options.Add(opt);
        }
        IsEmpty = Options.Count == 0;
        RebuildFiltered();
        UpdateHasActiveFilter();
    }

    private void RebuildFiltered()
    {
        FilteredOptions.Clear();
        var needle = (Search ?? "").Trim();
        foreach (var o in Options)
        {
            if (needle.Length == 0 || o.Value.Contains(needle, StringComparison.OrdinalIgnoreCase))
                FilteredOptions.Add(o);
        }
    }

    private void OnOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressSync) return;
        if (e.PropertyName != nameof(FilterOption.IsChecked)) return;
        PushFilter();
    }

    private void PushFilter()
    {
        if (_grid is null) return;
        var allChecked = Options.All(o => o.IsChecked);
        if (allChecked || Options.Count == 0)
        {
            GridFilters.SetAllowedValues(_grid, Path, null);
            HasActiveFilter = false;
        }
        else
        {
            var checkedValues = Options.Where(o => o.IsChecked).Select(o => o.Value).ToList();
            GridFilters.SetAllowedValues(_grid, Path, checkedValues);
            HasActiveFilter = true;
        }
    }

    private void UpdateHasActiveFilter()
    {
        HasActiveFilter = Options.Count > 0 && Options.Any(o => !o.IsChecked);
    }

    private void OnSelectAll(object? sender, RoutedEventArgs e)
    {
        _suppressSync = true;
        foreach (var o in Options) o.IsChecked = true;
        _suppressSync = false;
        PushFilter();
    }

    private void OnClearAll(object? sender, RoutedEventArgs e)
    {
        _suppressSync = true;
        foreach (var o in Options) o.IsChecked = false;
        _suppressSync = false;
        PushFilter();
    }

    private static object? ReadPath(object root, string path)
    {
        if (string.IsNullOrEmpty(path)) return root;
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

/// <summary>One option row in the checklist popup. Two-way bound IsChecked drives the set filter.</summary>
public sealed class FilterOption : INotifyPropertyChanged
{
    private bool _isChecked;
    public FilterOption(string value) { Value = value; _isChecked = true; }
    public string Value { get; }
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
