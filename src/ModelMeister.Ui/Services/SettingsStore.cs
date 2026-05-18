using System.IO;
using System.Text.Json;
using ModelMeister.Ui.Models;

namespace ModelMeister.Ui.Services;

/// <summary>Abstraction over persisted <see cref="AppSettings"/> so view-models can be tested in isolation.</summary>
public interface ISettingsStore
{
    /// <summary>The live settings instance — view-models mutate it directly and call <see cref="Save"/>.</summary>
    AppSettings Current { get; }

    /// <summary>Persist <see cref="Current"/> to disk atomically.</summary>
    void Save();
}

/// <summary>JSON-backed <see cref="ISettingsStore"/> persisting to <see cref="Paths.SettingsFile"/>.</summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Loads settings from disk (or starts fresh on first run / parse failure).</summary>
    public JsonSettingsStore()
    {
        Paths.EnsureAppDataDir();
        Current = Load();
    }

    /// <inheritdoc/>
    public AppSettings Current { get; }

    /// <inheritdoc/>
    public void Save()
    {
        Paths.EnsureAppDataDir();
        var tmp = Paths.SettingsFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Json));
        if (File.Exists(Paths.SettingsFile)) File.Replace(tmp, Paths.SettingsFile, null);
        else File.Move(tmp, Paths.SettingsFile);
    }

    private static AppSettings Load()
    {
        if (!File.Exists(Paths.SettingsFile)) return new AppSettings();
        try
        {
            var text = File.ReadAllText(Paths.SettingsFile);
            return JsonSerializer.Deserialize<AppSettings>(text, Json) ?? new AppSettings();
        }
        catch
        {
            // Corrupt/legacy settings — fall back to defaults rather than fail to launch.
            return new AppSettings();
        }
    }
}
