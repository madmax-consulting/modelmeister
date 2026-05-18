using Avalonia;
using System;

namespace ModelMeister.Ui;

/// <summary>
/// Process entry point. Avalonia's <c>BuildAvaloniaApp</c> hook is intentionally <c>public</c>
/// so the previewer / design-time tooling can pick it up.
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
        => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    /// <summary>Configures the Avalonia app builder. Called by both <see cref="Main"/> and design-time tools.</summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
