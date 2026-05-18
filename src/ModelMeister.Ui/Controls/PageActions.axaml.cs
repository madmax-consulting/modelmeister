using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ModelMeister.Ui.Controls;

/// <summary>
/// Right-hand toolbar action group rendered identically on every FeaturePage: Compare, Backup,
/// Export Excel, Import Excel, Refresh. Bind <c>DataContext</c> to a <c>FeaturePageViewModel</c>;
/// buttons hide themselves when the page's capability flags say no.
/// </summary>
public partial class PageActions : UserControl
{
    public PageActions() => AvaloniaXamlLoader.Load(this);
}
