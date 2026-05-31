using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ModelMeister.Ui.Views;

/// <summary>Modal folder-picker window for Copy-to / Move-to / bulk destination selection.</summary>
public partial class FolderPickerDialog : Window
{
    public FolderPickerDialog() => AvaloniaXamlLoader.Load(this);
}
