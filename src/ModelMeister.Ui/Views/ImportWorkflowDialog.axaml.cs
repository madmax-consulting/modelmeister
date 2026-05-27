using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ModelMeister.Ui.ViewModels;

namespace ModelMeister.Ui.Views;

/// <summary>The single, resizable Excel-import workflow window (ChooseFile → Verify → Import →
/// Results). Builds the Recents dropdown imperatively (MenuFlyout setters don't reliably resolve
/// per-item bindings in Avalonia 11) and wires the import grid's auto-scroll to the view-model.</summary>
public partial class ImportWorkflowDialog : Window
{
    public ImportWorkflowDialog()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is not ImportWorkflowViewModel vm) return;
        var grid = this.FindControl<DataGrid>("RunGrid");
        if (grid is null) return;
        vm.RegisterAutoScroll(index =>
        {
            if (index < 0 || index >= vm.FilteredRows.Count) return;
            grid.ScrollIntoView(vm.FilteredRows[index], null);
        });
    }

    private void OnRecentsClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || DataContext is not ImportWorkflowViewModel vm) return;
        if (vm.Recents.Count == 0) return;

        var flyout = new MenuFlyout();
        foreach (var path in vm.Recents)
        {
            var captured = path;
            var item = new MenuItem { Header = captured };
            item.Click += (_, _) => vm.PickRecentCommand.Execute(captured);
            flyout.Items.Add(item);
        }
        flyout.ShowAt(btn);
    }
}
