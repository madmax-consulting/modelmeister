using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using ModelMeister.Ui.ViewModels;

namespace ModelMeister.Ui.Views;

/// <summary>Backup library page — lists every backup produced anywhere in the app.</summary>
public partial class SnapshotsView : UserControl
{
    public SnapshotsView() => AvaloniaXamlLoader.Load(this);

    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not SnapshotsViewModel vm) return;
        if (sender is not DataGrid grid) return;
        if (grid.SelectedItem is not BackupRow row) return;
        vm.OpenInExplorerCommand.Execute(row);
    }
}
