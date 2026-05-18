using System.Runtime.Versioning;
using ModelMeister.Ui.ViewModels;

namespace ModelMeister.Ui.Services;

/// <summary>
/// Tiny manual composition root — DI without the framework. Wires the singleton service graph
/// and constructs view-models on demand. Built once in <see cref="App.OnFrameworkInitializationCompleted"/>.
/// </summary>
public sealed class AppServices
{
    /// <summary>Persisted environment definitions and their secrets.</summary>
    public IEnvironmentVault Vault { get; }
    /// <summary>Persisted user preferences (recent paths, default env, drawer state, ...).</summary>
    public ISettingsStore Settings { get; }
    /// <summary>State machine for the single live inriver connection.</summary>
    public IConnectionLifecycle Connection { get; }
    /// <summary>OS-shell facade for opening files / revealing in Explorer.</summary>
    public IFileOpener FileOpener { get; }
    /// <summary>Process-wide log + toast bus.</summary>
    public IAppLog Log { get; }
    /// <summary>Facade over the Model/Loading/Inriver/Scaffolder libraries.</summary>
    public Shell Shell { get; }
    /// <summary>Root view-model bound to the main window.</summary>
    public MainWindowViewModel MainWindow { get; }

    private AppServices(
        IEnvironmentVault vault,
        ISettingsStore settings,
        IConnectionLifecycle connection,
        IFileOpener fileOpener,
        IAppLog log,
        Shell shell,
        MainWindowViewModel mainWindow)
    {
        Vault = vault;
        Settings = settings;
        Connection = connection;
        FileOpener = fileOpener;
        Log = log;
        Shell = shell;
        MainWindow = mainWindow;
    }

    /// <summary>Constructs the full service graph and the root view-model. Call once at startup.</summary>
    [SupportedOSPlatform("windows")]
    public static AppServices Build()
    {
        IEnvironmentVault vault = new DpapiEnvironmentVault();
        ISettingsStore settings = new JsonSettingsStore();
        IConnectionLifecycle connection = new ConnectionLifecycle(vault);
        IFileOpener fileOpener = new OsFileOpener();
        IAppLog log = new AppLog();
        var shell = new Shell(connection);
        var main = new MainWindowViewModel(vault, settings, connection, fileOpener, log, shell);
        return new AppServices(vault, settings, connection, fileOpener, log, shell, main);
    }
}
