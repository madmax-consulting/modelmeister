using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// Placeholder Setup view-model. Wired in Session 1 so the hub IA boots cleanly; full Setup sub-pages
/// (Appearance / Defaults / Storage / Diagnostics / About) land in Session 7.
/// </summary>
public partial class SetupViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _main;
    private readonly ISettingsStore _settings;

    public SetupViewModel(MainWindowViewModel main, ISettingsStore settings)
    {
        _main = main;
        _settings = settings;
    }

    /// <summary>The current dark/light state, mirrored from the main VM.</summary>
    public bool IsDarkTheme => _main.IsDarkTheme;

    [RelayCommand] private void ToggleTheme() => _main.ToggleThemeCommand.Execute(null);
}
