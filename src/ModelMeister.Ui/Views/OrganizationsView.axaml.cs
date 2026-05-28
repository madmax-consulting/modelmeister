using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using ModelMeister.Ui.Models;
using ModelMeister.Ui.ViewModels;

namespace ModelMeister.Ui.Views;

public partial class OrganizationsView : UserControl
{
    public OrganizationsView()
    {
        AvaloniaXamlLoader.Load(this);
        if (this.FindControl<DataGrid>("OrgsGrid") is { } grid)
            grid.DoubleTapped += OnRowDoubleTapped;
    }

    // Double-clicking an organization row opens its editor — same as the row Edit button / context-menu Edit.
    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not OrganizationsViewModel vm) return;
        if (e.Source is not Visual src) return;
        if (src.FindAncestorOfType<DataGridRow>(includeSelf: true) is not { DataContext: Organization row }) return;
        if (vm.EditCommand.CanExecute(row)) vm.EditCommand.Execute(row);
    }
}
