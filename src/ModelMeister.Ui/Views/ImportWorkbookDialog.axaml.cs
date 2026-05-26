using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ModelMeister.Ui.ViewModels;

namespace ModelMeister.Ui.Views;

/// <summary>Import-from-workbook dialog. Builds the Recents dropdown imperatively (ControlTheme
/// setters on MenuFlyout don't reliably resolve per-item bindings in Avalonia 11) — mirrors the
/// Model page's recents pattern.</summary>
public partial class ImportWorkbookDialog : Window
{
    public ImportWorkbookDialog() => AvaloniaXamlLoader.Load(this);

    private void OnRecentsClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || DataContext is not ImportWorkbookViewModel vm) return;
        if (vm.Recents.Count == 0) return;

        var flyout = new MenuFlyout();
        foreach (var path in vm.Recents)
        {
            var captured = path;
            var item = new MenuItem { Header = captured };
            item.Click += (_, _) => vm.WorkbookPath = captured;
            flyout.Items.Add(item);
        }
        flyout.ShowAt(btn);
    }
}
