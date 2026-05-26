using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ModelMeister.Ui.Controls;

/// <summary>
/// Right-hand toolbar action group rendered identically on every FeaturePage: Backup + Refresh.
/// Bind <c>DataContext</c> to a <c>FeaturePageViewModel</c>; Backup hides itself when the page has no
/// backup scope. Excel export/import lives in the sibling <see cref="ExcelMenu"/> control.
/// </summary>
public partial class PageActions : UserControl
{
    public PageActions() => AvaloniaXamlLoader.Load(this);
}
