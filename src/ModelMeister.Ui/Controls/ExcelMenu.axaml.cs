using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ModelMeister.Ui.Controls;

/// <summary>
/// A single labeled "Excel ▾" menu button for a FeaturePage: Download list / Download template /
/// Import…. Bind <c>DataContext</c> to a <c>FeaturePageViewModel</c>; each item hides itself when the
/// page's capability flags (<c>HasExcelExport</c> / <c>HasExcelTemplate</c> / <c>HasExcelImport</c>)
/// say no. Replaces the old per-page download flyout plus the copy-looking import icon in PageActions.
/// </summary>
public partial class ExcelMenu : UserControl
{
    public ExcelMenu() => AvaloniaXamlLoader.Load(this);
}
