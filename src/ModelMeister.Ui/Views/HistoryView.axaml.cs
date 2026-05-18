using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using ModelMeister.Ui.ViewModels;

namespace ModelMeister.Ui.Views;

public partial class HistoryView : UserControl
{
    public HistoryView()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is HistoryViewModel vm) vm.Refresh();
        };
    }

    private void OnReceiptDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is HistoryViewModel vm && vm.SelectedReceipt is not null)
            vm.OpenReceiptCommand.Execute(null);
    }

    private void OnBackupDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is HistoryViewModel vm && vm.SelectedBackup is not null)
            vm.OpenBackupCommand.Execute(null);
    }
}
