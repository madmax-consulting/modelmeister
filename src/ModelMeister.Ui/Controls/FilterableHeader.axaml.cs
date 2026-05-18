using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.Controls;

/// <summary>
/// Drop-in column header for a <see cref="DataGrid"/> column. Renders the column title bold and a
/// tight inline filter <see cref="TextBox"/> underneath. Changes to <see cref="FilterText"/> are
/// pushed into <see cref="GridFilters"/> on the parent grid, keyed by <see cref="Path"/>.
/// </summary>
public partial class FilterableHeader : UserControl
{
    /// <summary>Display text shown above the filter input.</summary>
    public static readonly StyledProperty<object?> TitleProperty =
        AvaloniaProperty.Register<FilterableHeader, object?>(nameof(Title));

    /// <summary>Property-name on the row instance the filter compares against (substring,
    /// ordinal-ignore-case). Supports dotted paths.</summary>
    public static readonly StyledProperty<string> PathProperty =
        AvaloniaProperty.Register<FilterableHeader, string>(nameof(Path), string.Empty);

    /// <summary>Two-way bound to the inline filter <see cref="TextBox"/>.</summary>
    public static readonly StyledProperty<string?> FilterTextProperty =
        AvaloniaProperty.Register<FilterableHeader, string?>(nameof(FilterText));

    /// <summary>Read-only convenience for the clear-button's visibility.</summary>
    public static readonly DirectProperty<FilterableHeader, bool> HasFilterProperty =
        AvaloniaProperty.RegisterDirect<FilterableHeader, bool>(nameof(HasFilter), o => o._hasFilter);

    private bool _hasFilter;
    private DataGrid? _grid;

    public object? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Path { get => GetValue(PathProperty); set => SetValue(PathProperty, value); }
    public string? FilterText { get => GetValue(FilterTextProperty); set => SetValue(FilterTextProperty, value); }
    public bool HasFilter => _hasFilter;

    public FilterableHeader()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _grid = this.FindAncestorOfType<DataGrid>();
        if (_grid is null || string.IsNullOrEmpty(Path)) return;

        // Restore any previously-set filter text (so re-attach after virtualization keeps it).
        var existing = GridFilters.GetFilter(_grid, Path);
        if (!string.IsNullOrEmpty(existing) && FilterText != existing)
            FilterText = existing;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FilterTextProperty)
        {
            var newText = change.GetNewValue<string?>();
            var was = _hasFilter;
            _hasFilter = !string.IsNullOrEmpty(newText);
            if (was != _hasFilter) RaisePropertyChanged(HasFilterProperty, was, _hasFilter);

            if (_grid is null) _grid = this.FindAncestorOfType<DataGrid>();
            if (_grid is not null && !string.IsNullOrEmpty(Path))
                GridFilters.SetFilter(_grid, Path, newText);
        }
    }

    private void OnClearClick(object? sender, RoutedEventArgs e) => FilterText = null;
}
