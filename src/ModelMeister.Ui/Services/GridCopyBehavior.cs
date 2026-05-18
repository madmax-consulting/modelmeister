using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

namespace ModelMeister.Ui.Services;

/// <summary>
/// Universal cell/row copy support for any <see cref="DataGrid"/>. Activated by setting the
/// <c>GridCopyBehavior.Enable</c> attached property to <c>true</c> (done globally in App.axaml).
/// Ctrl+C copies the focused cell (or all selected rows if multi-row selected); right-click
/// surfaces Copy cell / Copy row in the context menu (added to the existing menu if any, with a
/// marker so we only add once).
/// </summary>
public static class GridCopyBehavior
{
    public static readonly AttachedProperty<bool> EnableProperty =
        AvaloniaProperty.RegisterAttached<DataGrid, bool>("Enable", typeof(GridCopyBehavior));

    public static void SetEnable(AvaloniaObject element, bool value) => element.SetValue(EnableProperty, value);
    public static bool GetEnable(AvaloniaObject element) => element.GetValue(EnableProperty);

    private const string MarkerTag = "__gridcopy";

    static GridCopyBehavior()
    {
        EnableProperty.Changed.AddClassHandler<DataGrid>(OnEnableChanged);
    }

    private static void OnEnableChanged(DataGrid grid, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            grid.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
            grid.AttachedToLogicalTree += OnAttached;
            if (grid.GetLogicalParent() is not null) EnsureMenuItems(grid);
        }
        else
        {
            grid.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            grid.AttachedToLogicalTree -= OnAttached;
        }
    }

    private static void OnAttached(object? sender, LogicalTreeAttachmentEventArgs e)
    {
        if (sender is DataGrid grid) EnsureMenuItems(grid);
    }

    private static void EnsureMenuItems(DataGrid grid)
    {
        var menu = grid.ContextMenu;
        if (menu is null)
        {
            menu = new ContextMenu();
            grid.ContextMenu = menu;
        }
        else if (menu.Items.OfType<Control>().Any(c => (c.Tag as string) == MarkerTag))
        {
            return;
        }
        else if (menu.Items.Count > 0)
        {
            menu.Items.Add(new Separator { Tag = MarkerTag });
        }

        var copyCell = new MenuItem { Header = "Copy cell", Tag = MarkerTag };
        copyCell.Click += (_, _) => CopyCurrentCell(grid);
        var copyRow = new MenuItem { Header = "Copy row", Tag = MarkerTag };
        copyRow.Click += (_, _) => CopySelectedRows(grid);
        menu.Items.Add(copyCell);
        menu.Items.Add(copyRow);
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (e.Key != Key.C || (e.KeyModifiers & KeyModifiers.Control) == 0) return;
        // Let TextBox cells handle their own copy.
        if (e.Source is TextBox) return;
        CopySelectedRows(grid);
        e.Handled = true;
    }

    private static void CopyCurrentCell(DataGrid grid)
    {
        var item = grid.SelectedItem;
        if (item is null) return;

        string? text = null;
        if (grid.CurrentColumn is DataGridBoundColumn { Binding: Binding b } && !string.IsNullOrEmpty(b.Path))
        {
            text = ReadPath(item, b.Path)?.ToString();
        }
        text ??= RowToText(item);
        _ = ClipboardHelpers.CopyAsync(text);
    }

    private static void CopySelectedRows(DataGrid grid)
    {
        var rows = grid.SelectedItems?.Cast<object>().ToList() ?? new();
        if (rows.Count == 0 && grid.SelectedItem is not null) rows.Add(grid.SelectedItem);
        if (rows.Count == 0) return;

        var sb = new StringBuilder();
        foreach (var r in rows)
            sb.AppendLine(RowToText(r));
        _ = ClipboardHelpers.CopyAsync(sb.ToString().TrimEnd());
    }

    private static string RowToText(object item)
    {
        var values = item.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && IsScalar(p.PropertyType))
            .Select(p =>
            {
                try { return p.GetValue(item)?.ToString() ?? ""; }
                catch { return ""; }
            });
        return string.Join("\t", values);
    }

    private static bool IsScalar(Type t)
    {
        if (t == typeof(string)) return true;
        if (t.IsPrimitive) return true;
        if (t.IsEnum) return true;
        if (Nullable.GetUnderlyingType(t) is { } nt) return IsScalar(nt);
        if (t == typeof(DateTime) || t == typeof(DateTimeOffset)) return true;
        if (t == typeof(decimal) || t == typeof(Guid) || t == typeof(TimeSpan)) return true;
        return false;
    }

    private static object? ReadPath(object item, string path)
    {
        object? cur = item;
        foreach (var part in path.Split('.'))
        {
            if (cur is null) return null;
            var p = cur.GetType().GetProperty(part);
            if (p is null) return null;
            cur = p.GetValue(cur);
        }
        return cur;
    }
}
