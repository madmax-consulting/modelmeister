using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ModelMeister.Ui.Views;

/// <summary>Landing page. Tiles will be wired to live state in Session 5.</summary>
public partial class DashboardView : UserControl
{
    public DashboardView() => AvaloniaXamlLoader.Load(this);
}
