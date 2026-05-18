using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace ModelMeister.Ui.Services;

/// <summary>Abstraction over OS shell operations so view-models stay test-friendly.</summary>
public interface IFileOpener
{
    /// <summary>Open <paramref name="path"/> in the user's default handler. Silently no-ops on missing files.</summary>
    void Open(string path);

    /// <summary>Open <paramref name="filePath"/> at <paramref name="line"/> in VS Code if available; otherwise fall back to <see cref="Open"/>.</summary>
    void OpenAt(string filePath, int line);

    /// <summary>Reveal <paramref name="path"/> in the platform file manager (Explorer/Finder).</summary>
    void RevealInExplorer(string path);
}

/// <summary>Default <see cref="IFileOpener"/> backed by <see cref="Process.Start(ProcessStartInfo)"/>.</summary>
public sealed class OsFileOpener : IFileOpener
{
    /// <inheritdoc/>
    public void Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    /// <inheritdoc/>
    public void OpenAt(string filePath, int line)
    {
        if (!File.Exists(filePath)) return;

        // Best-effort: try VS Code first ("code -g file:line"), fall back to OS default.
        try
        {
            Process.Start(new ProcessStartInfo("code", $"-g \"{filePath}:{line}\"") { UseShellExecute = true });
            return;
        }
        catch
        {
            // VS Code not installed or not on PATH — fall through to the default handler.
        }

        Open(filePath);
    }

    /// <inheritdoc/>
    public void RevealInExplorer(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            return;
        }

        var target = File.Exists(path) ? Path.GetDirectoryName(path) ?? path : path;
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }
}
