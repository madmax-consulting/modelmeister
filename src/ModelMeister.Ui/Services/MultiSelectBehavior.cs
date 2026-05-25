using System.Collections;
using Avalonia;
using Avalonia.Controls;

namespace ModelMeister.Ui.Services;

/// <summary>
/// Mirrors a <see cref="DataGrid"/>'s current multi-selection into a bound <see cref="IList"/> on
/// the view-model, so "promote selected" bulk commands can read what's selected. (DataGrid's own
/// <c>SelectedItems</c> is not directly bindable.) Set
/// <c>svc:MultiSelectBehavior.Selection="{Binding SelectedRows}"</c> on a grid with
/// <c>SelectionMode="Extended"</c>.
/// </summary>
public static class MultiSelectBehavior
{
    public static readonly AttachedProperty<IList?> SelectionProperty =
        AvaloniaProperty.RegisterAttached<DataGrid, IList?>("Selection", typeof(MultiSelectBehavior));

    public static void SetSelection(AvaloniaObject element, IList? value) => element.SetValue(SelectionProperty, value);
    public static IList? GetSelection(AvaloniaObject element) => element.GetValue(SelectionProperty);

    static MultiSelectBehavior()
    {
        SelectionProperty.Changed.AddClassHandler<DataGrid>(OnSelectionPropertyChanged);
    }

    private static void OnSelectionPropertyChanged(DataGrid grid, AvaloniaPropertyChangedEventArgs e)
    {
        grid.SelectionChanged -= OnGridSelectionChanged;
        if (e.NewValue is IList)
            grid.SelectionChanged += OnGridSelectionChanged;
    }

    private static void OnGridSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (GetSelection(grid) is not IList target) return;
        target.Clear();
        foreach (var item in grid.SelectedItems) target.Add(item);
    }
}
