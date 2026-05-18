using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ModelMeister.Ui.ViewModels;

namespace ModelMeister.Ui.Views;

/// <summary>Apply page. Refreshes the VM on attach and wires auto-scroll for the receipt grid.</summary>
public partial class ApplyView : UserControl
{
    public ApplyView()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not ApplyViewModel vm) return;
            vm.Refresh();
            vm.RegisterAutoScroll(index =>
            {
                var grid = this.FindControl<DataGrid>("LogGrid");
                if (grid is null || index < 0 || index >= vm.FilteredEntries.Count) return;
                grid.ScrollIntoView(vm.FilteredEntries[index], null);
            });
        };
    }
}
