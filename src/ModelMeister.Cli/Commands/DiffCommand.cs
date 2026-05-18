using Spectre.Console;
using ModelMeister.Cli.Interactive;
using ModelMeister.Inriver;
using ModelMeister.Inriver.Diff;
using ModelMeister.Inriver.Reporting;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Loading;

namespace ModelMeister.Cli.Commands;

/// <summary>Diffs a code-defined model against a live inriver environment.</summary>
public static class DiffCommand
{
    /// <summary>The accepted values for the <c>--format</c> option.</summary>
    public enum Format { Tree, Text, Json }

    /// <summary>
    /// Loads the model, snapshots the environment, runs the differ, renders the result,
    /// optionally writes a file copy, and returns an exit code that may signal pending changes.
    /// </summary>
    public static async Task<int> RunAsync(
        string modelPath, string url, InriverAuth auth,
        string format, string? outPath, bool failOnChanges, MergePolicy policy, CancellationToken ct)
    {
        var code = new ModelAssemblyLoader().LoadFromPath(modelPath);

        using var client = new InriverClient(url);
        var rc = await auth.ConnectAsync(client).ConfigureAwait(false);
        if (rc != ExitCodes.Success) return rc;

        var snapshot = new InriverSnapshot(client).Capture();
        var changes = ModelDiffer.Diff(code, snapshot, policy);

        var fmt = ParseFormat(format);
        RenderToConsole(changes, fmt);

        if (!string.IsNullOrEmpty(outPath))
            WriteToFile(changes, fmt, outPath);

        // CI gates merges on this signal — return ChangesPending instead of Success when
        // the working tree disagrees with the live env.
        return failOnChanges && !changes.IsEmpty ? ExitCodes.ChangesPending : ExitCodes.Success;
    }

    private static Format ParseFormat(string raw) => raw.ToLowerInvariant() switch
    {
        "json" => Format.Json,
        "text" => Format.Text,
        _ => Format.Tree,
    };

    private static void RenderToConsole(ModelChangeSet changes, Format fmt)
    {
        switch (fmt)
        {
            case Format.Tree:
                DiffRenderer.Render(changes);
                break;
            case Format.Json:
                Console.WriteLine(ChangeReport.ToJson(changes));
                break;
            default:
                AnsiConsole.WriteLine(ChangeReport.ToText(changes));
                break;
        }
    }

    private static void WriteToFile(ModelChangeSet changes, Format fmt, string outPath)
    {
        var body = fmt is Format.Json ? ChangeReport.ToJson(changes) : ChangeReport.ToText(changes);
        var dir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(outPath, body);
    }
}
