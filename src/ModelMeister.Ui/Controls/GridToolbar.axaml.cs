using Avalonia;
using Avalonia.Controls;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.Controls;

/// <summary>
/// A small toolbar that sits above a <see cref="DataGrid"/> and drives it through
/// <see cref="GridFilters"/>: a global search box (matches every scalar column), a show/hide
/// column chooser, a compact-density toggle, and a clear-filters button. Host it next to the grid
/// and point <see cref="TargetGrid"/> at it, e.g. <c>TargetGrid="{Binding #MainGrid}"</c>.
/// </summary>
public partial class GridToolbar : UserControl
{
    public static readonly StyledProperty<DataGrid?> TargetGridProperty =
        AvaloniaProperty.Register<GridToolbar, DataGrid?>(nameof(TargetGrid));

    public DataGrid? TargetGrid
    {
        get => GetValue(TargetGridProperty);
        set => SetValue(TargetGridProperty, value);
    }

    public GridToolbar()
    {
        InitializeComponent();

        SearchBox.TextChanged += (_, _) =>
        {
            if (TargetGrid is { } g) GridFilters.SetGlobalFilter(g, SearchBox.Text);
        };

        ClearButton.Click += (_, _) =>
        {
            if (TargetGrid is { } g) GridFilters.ClearAll(g);
            SearchBox.Text = null;
        };

        DensityToggle.IsCheckedChanged += (_, _) =>
        {
            if (TargetGrid is not { } g) return;
            if (DensityToggle.IsChecked == true)
            {
                if (!g.Classes.Contains("compact")) g.Classes.Add("compact");
            }
            else
            {
                g.Classes.Remove("compact");
            }
        };

        if (ColumnsButton.Flyout is Flyout flyout)
            flyout.Opened += (_, _) => BuildColumnList();
    }

    private void BuildColumnList()
    {
        ColumnsPanel.Children.Clear();
        if (TargetGrid is not { } g) return;

        foreach (var column in g.Columns)
        {
            var label = ColumnLabel(column);
            if (string.IsNullOrWhiteSpace(label)) continue; // skip the action/button columns (empty header)

            var captured = column;
            var cb = new CheckBox { Content = label, IsChecked = column.IsVisible };
            cb.IsCheckedChanged += (_, _) => captured.IsVisible = cb.IsChecked == true;
            ColumnsPanel.Children.Add(cb);
        }
    }

    private static string ColumnLabel(DataGridColumn column) => column.Header switch
    {
        FilterableHeader fh => (fh.Title as string) is { Length: > 0 } t ? t : fh.Path,
        ChecklistFilterHeader ch => ch.Title is { Length: > 0 } t ? t : ch.Path,
        TextBlock tb => tb.Text ?? "",
        string s => s,
        { } other => other.ToString() ?? "",
        _ => "",
    };
}
