using System;
using System.IO;
using System.Linq;

namespace ModelMeister.Ui.Services;

/// <summary>
/// Resolves the per-user application-data paths used by the UI (vault, settings, ...) and
/// provides a small helper for turning a URL into a filesystem-safe segment.
/// </summary>
internal static class Paths
{
    /// <summary>Root directory for all persisted UI state (<c>%APPDATA%/ModelMeister</c>).</summary>
    public static string AppDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ModelMeister");

    /// <summary>DPAPI-encrypted JSON file storing the list of <see cref="Models.EnvironmentEntry"/>.</summary>
    public static string EnvironmentsFile => Path.Combine(AppDataDir, "environments.dat");

    /// <summary>DPAPI-encrypted JSON file storing per-environment secrets, keyed by entry id.</summary>
    public static string SecretsFile => Path.Combine(AppDataDir, "secrets.dat");

    /// <summary>Plain JSON file storing <see cref="Models.AppSettings"/>.</summary>
    public static string SettingsFile => Path.Combine(AppDataDir, "settings.json");

    /// <summary>Ensures <see cref="AppDataDir"/> exists; no-op if it already does.</summary>
    public static void EnsureAppDataDir() => Directory.CreateDirectory(AppDataDir);

    /// <summary>
    /// Replaces every non-alphanumeric character in <paramref name="url"/> with <c>_</c> so the
    /// result can be used as a directory name (receipts and backups are bucketed by environment URL).
    /// </summary>
    public static string SafeUrlSegment(string url)
        => new(url.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
}
