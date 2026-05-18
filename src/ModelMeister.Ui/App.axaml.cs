using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using ModelMeister.Ui.Services;
using ModelMeister.Ui.ViewModels;
using ModelMeister.Ui.Views;

namespace ModelMeister.Ui;

/// <summary>
/// Avalonia application entry-point. Owns the DI <see cref="AppServices"/> container and seeds the
/// initial <see cref="Avalonia.Styling.ThemeVariant"/> from persisted settings.
/// </summary>
public partial class App : Application
{
    /// <inheritdoc/>
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc/>
    public override void OnFrameworkInitializationCompleted()
    {
        BindingPlugins.DataValidators.RemoveAt(0);

        var services = AppServices.Build();
        Services = services;

        // Apply the persisted theme preference before any view is shown so the first paint is correct.
        RequestedThemeVariant = services.Settings.Current.PreferDarkTheme
            ? ThemeVariant.Dark
            : ThemeVariant.Light;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainVm = services.MainWindow;
            desktop.MainWindow = new MainWindow { DataContext = mainVm };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>The process-wide DI container. Set once at startup; <c>null</c> until then.</summary>
    public static AppServices? Services { get; private set; }
}
