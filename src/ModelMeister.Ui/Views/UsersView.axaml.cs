using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using ModelMeister.Ui.ViewModels;

namespace ModelMeister.Ui.Views;

public partial class UsersView : UserControl
{
    public UsersView()
    {
        AvaloniaXamlLoader.Load(this);
        if (this.FindControl<DataGrid>("UsersGrid") is { } grid)
            grid.DoubleTapped += OnRowDoubleTapped;
    }

    // Double-clicking a user row opens its editor — same as the row Edit button / context-menu Edit.
    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not UsersViewModel vm) return;
        if (e.Source is not Visual src) return;
        if (src.FindAncestorOfType<DataGridRow>(includeSelf: true) is not { DataContext: UserListRow row }) return;
        if (vm.EditUserCommand.CanExecute(row)) vm.EditUserCommand.Execute(row);
    }
}
