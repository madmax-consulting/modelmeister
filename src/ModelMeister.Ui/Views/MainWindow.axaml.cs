using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ModelMeister.Ui.Models;
using ModelMeister.Ui.ViewModels;

namespace ModelMeister.Ui.Views;

/// <summary>
/// Top-level window. Renders the in-app logo to a bitmap at startup so the OS taskbar icon
/// matches the in-app branding (and re-renders if the theme variant changes).
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        Icon = RenderLogoIcon();
    }

    /// <summary>
    /// The sidebar renders three ListBoxes (one per HOME / MANAGE / SYSTEM group) all sharing
    /// the parent VM's <c>SelectedHub</c>. SelectedItem is bound one-way only — pushing through
    /// two-way would clobber the VM whenever a non-owning ListBox cleared its selection to null.
    /// Instead, propagate user-driven selection here.
    /// </summary>
    private void OnHubSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox lb
            && lb.SelectedItem is HubDescriptor hub
            && DataContext is MainWindowViewModel vm
            && !ReferenceEquals(vm.SelectedHub, hub))
        {
            vm.SelectedHub = hub;
        }
    }

    // Render the IcoLogo path resource to a bitmap so the taskbar icon matches the in-app logo.
    // Done at runtime so the icon and the header geometry can never drift; uses TryGetResource
    // because Application.Resources[key] does NOT traverse ThemeDictionaries.
    private static WindowIcon? RenderLogoIcon()
    {
        const int px = 256;
        if (Application.Current is not { } app) return null;
        var theme = app.ActualThemeVariant;
        if (!app.TryGetResource("IcoLogo", theme, out var raw) || raw is not Geometry geom) return null;

        var bg = app.TryGetResource("Accent", theme, out var bgRaw) && bgRaw is IBrush bgBrush
            ? bgBrush
            : (IBrush)new SolidColorBrush(Color.Parse("#E8A24A"));
        var fg = app.TryGetResource("AccentButtonForeground", theme, out var fgRaw) && fgRaw is IBrush fgBrush
            ? fgBrush
            : (IBrush)new SolidColorBrush(Color.Parse("#1A0E00"));

        // Fit the geometry into the bitmap with a generous pad so it doesn't crowd the rounded square.
        var bounds = geom.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return null;
        const double pad = 48;
        var scale = Math.Min((px - 2 * pad) / bounds.Width, (px - 2 * pad) / bounds.Height);
        var dx = (px - bounds.Width * scale) / 2 - bounds.X * scale;
        var dy = (px - bounds.Height * scale) / 2 - bounds.Y * scale;

        var rtb = new RenderTargetBitmap(new PixelSize(px, px), new Vector(96, 96));
        using (var ctx = rtb.CreateDrawingContext())
        {
            // Solid amber rounded-square so the icon reads against any taskbar background.
            ctx.DrawRectangle(bg, null, new RoundedRect(new Rect(0, 0, px, px), 56));

            var tx = Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(dx, dy);
            using (ctx.PushTransform(tx))
            {
                // Stroke width is in device pixels — divide by scale to keep visual weight constant.
                var pen = new Pen(fg, 2.4 / scale)
                {
                    LineJoin = PenLineJoin.Round,
                    LineCap = PenLineCap.Round,
                };
                ctx.DrawGeometry(null, pen, geom);
            }
        }

        using var ms = new MemoryStream();
        rtb.Save(ms);
        ms.Position = 0;
        return new WindowIcon(ms);
    }
}
