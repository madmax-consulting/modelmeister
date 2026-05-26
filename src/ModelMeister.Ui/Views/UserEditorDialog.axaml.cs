using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ModelMeister.Ui.Views;

public partial class UserEditorDialog : Window
{
    public UserEditorDialog() => AvaloniaXamlLoader.Load(this);
}
