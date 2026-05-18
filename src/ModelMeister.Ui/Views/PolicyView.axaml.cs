using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ModelMeister.Ui.Views;

/// <summary>The Policy page — merge-policy toggles between Model and Compare.</summary>
public partial class PolicyView : UserControl
{
    public PolicyView() => AvaloniaXamlLoader.Load(this);
}
