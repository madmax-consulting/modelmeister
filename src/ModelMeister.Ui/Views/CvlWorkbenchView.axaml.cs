using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using ModelMeister.Ui.ViewModels;

namespace ModelMeister.Ui.Views;

public partial class CvlWorkbenchView : UserControl
{
    public CvlWorkbenchView()
    {
        AvaloniaXamlLoader.Load(this);
        if (this.FindControl<DataGrid>("CvlGrid") is { } grid)
            grid.DoubleTapped += OnRowDoubleTapped;
    }

    // Double-clicking a CVL row opens its editor — same as the row Edit button / context-menu Edit.
    // We resolve the row from the double-tapped element (not SelectedItem) so a double-click on the
    // header or empty space doesn't re-open the previously selected row.
    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not CvlWorkbenchViewModel vm) return;
        if (e.Source is not Visual src) return;
        if (src.FindAncestorOfType<DataGridRow>(includeSelf: true) is not { DataContext: CvlRow row }) return;
        if (vm.EditCvlCommand.CanExecute(row)) vm.EditCvlCommand.Execute(row);
    }
}
