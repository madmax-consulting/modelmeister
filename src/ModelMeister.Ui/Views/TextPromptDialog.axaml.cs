using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ModelMeister.Ui.Views;

public partial class TextPromptDialog : Window
{
    public TextPromptDialog()
    {
        AvaloniaXamlLoader.Load(this);
        // Focus + select the input so the user can type / overwrite immediately.
        Opened += (_, _) =>
        {
            if (this.FindControl<TextBox>("Input") is { } box)
            {
                box.Focus();
                box.SelectAll();
            }
        };
    }
}
