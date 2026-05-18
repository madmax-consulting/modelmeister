using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ModelMeister.Ui.Views;

public partial class SimpleConfirmDialog : Window
{
    public SimpleConfirmDialog() => AvaloniaXamlLoader.Load(this);
}
