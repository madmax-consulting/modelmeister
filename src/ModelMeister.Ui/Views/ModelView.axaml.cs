using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using ModelMeister.Ui.ViewModels;

namespace ModelMeister.Ui.Views;

/// <summary>Model page. Adds drag-and-drop of a csproj/dll onto the page to auto-load it,
/// and builds the Recents dropdown imperatively (ControlTheme setters on MenuFlyout don't
/// reliably resolve per-item bindings in Avalonia 11).</summary>
public partial class ModelView : UserControl
{
    public ModelView()
    {
        AvaloniaXamlLoader.Load(this);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not ModelViewModel vm) return;
        if (!e.Data.Contains(DataFormats.Files)) return;

        var path = e.Data.GetFiles()?.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        vm.AcceptDroppedPath(path);
        await vm.LoadAsync();
    }

    private void OnIssueDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ModelViewModel vm && vm.SelectedIssue is not null)
            vm.OpenIssueCommand.Execute(null);
    }

    private void OnRecentsClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || DataContext is not ModelViewModel vm) return;
        if (vm.RecentModelPaths.Count == 0) return;

        var flyout = new MenuFlyout();
        foreach (var path in vm.RecentModelPaths)
        {
            var captured = path;
            var item = new MenuItem { Header = captured };
            item.Click += async (_, _) =>
            {
                vm.ModelPath = captured;
                await vm.LoadAsync();
            };
            flyout.Items.Add(item);
        }
        flyout.ShowAt(btn);
    }
}
