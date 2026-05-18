using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using ModelMeister.Ui.ViewModels;

namespace ModelMeister.Ui.Views;

/// <summary>Environments page. Adds a double-click handler that connects to the selected row,
/// and toggles a <c>connected</c> CSS class on rows whose env is currently connected so they tint green.</summary>
public partial class EnvironmentsView : UserControl
{
    public EnvironmentsView()
    {
        AvaloniaXamlLoader.Load(this);
        AddHandler(DoubleTappedEvent, OnRowDoubleTapped, handledEventsToo: false);
    }

    private async void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not EnvironmentsViewModel vm) return;
        if (e.Source is not Control source) return;

        // Only react to double-clicks on grid rows; ignore clicks on toolbar buttons etc.
        if (source.FindLogicalAncestorOfType<DataGridRow>() is null) return;
        if (vm.SelectedRow is null) return;

        await vm.ConnectOrDisconnectAsync();
    }

    private void OnLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.DataContext is not EnvironmentRow row) return;
        ApplyConnectedClass(e.Row, row);
        row.PropertyChanged -= OnRowPropChanged;
        row.PropertyChanged += OnRowPropChanged;

        void OnRowPropChanged(object? s, PropertyChangedEventArgs ev)
        {
            if (ev.PropertyName == nameof(EnvironmentRow.IsConnected))
                ApplyConnectedClass(e.Row, row);
        }
    }

    private static void ApplyConnectedClass(DataGridRow dgRow, EnvironmentRow row)
    {
        if (row.IsConnected) { if (!dgRow.Classes.Contains("connected")) dgRow.Classes.Add("connected"); }
        else dgRow.Classes.Remove("connected");
    }
}

internal static class LogicalAncestorExt
{
    /// <summary>Walks <see cref="Control.Parent"/> upwards until a <typeparamref name="T"/> is found, or returns null.</summary>
    public static T? FindLogicalAncestorOfType<T>(this Control control) where T : Control
    {
        var current = control.Parent;
        while (current is not null)
        {
            if (current is T match) return match;
            current = (current as Control)?.Parent;
        }
        return null;
    }
}
