using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ModelMeister.Ui.Views;

public partial class UsersView : UserControl
{
    public UsersView() => AvaloniaXamlLoader.Load(this);
}
