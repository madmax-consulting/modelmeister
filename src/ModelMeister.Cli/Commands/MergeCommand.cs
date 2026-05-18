using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;
using ModelMeister.Scaffolder;

namespace ModelMeister.Cli.Commands;

/// <summary>Merges two inriver JSON exports into a single document.</summary>
public static class MergeCommand
{
    private const int MaxConflictsShown = 50;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Combine two inriver JSON exports. <paramref name="basePath"/> is the lower-priority side;
    /// <paramref name="overlayPath"/> overrides on conflict. Writes merged JSON to <paramref name="outPath"/>.
    /// </summary>
    public static int Run(string basePath, string overlayPath, string outPath, string conflictPolicy)
    {
        if (TryEnsureExists(basePath, "Base") is { } baseFail) return baseFail;
        if (TryEnsureExists(overlayPath, "Overlay") is { } overlayFail) return overlayFail;

        var baseModel = InriverModelJson.Load(basePath);
        var overlay = InriverModelJson.Load(overlayPath);

        var policy = ParsePolicy(conflictPolicy);
        var merger = new ModelMerger(policy);
        var (merged, conflicts) = merger.Merge(baseModel, overlay);

        if (conflicts.Count > 0)
        {
            AnsiConsole.MarkupLine($"[yellow]{conflicts.Count} conflict(s):[/]");
            foreach (var c in conflicts.Take(MaxConflictsShown))
                AnsiConsole.MarkupLine($"  [yellow]·[/] {c.EscapeMarkup()}");

            var overflow = conflicts.Count - MaxConflictsShown;
            if (overflow > 0)
                AnsiConsole.MarkupLine($"  [grey]... and {overflow} more[/]");

            if (merger.Policy == MergeConflictPolicy.Fail)
                return ExitCodes.OperationFailed;
        }

        WriteJson(merged, outPath);
        AnsiConsole.MarkupLine($"[green]Merged model written to {outPath.EscapeMarkup()}[/]");
        return ExitCodes.Success;
    }

    private static int? TryEnsureExists(string path, string label)
    {
        if (File.Exists(path)) return null;
        AnsiConsole.MarkupLine($"[red]{label} file not found: {path.EscapeMarkup()}[/]");
        return ExitCodes.UsageError;
    }

    private static MergeConflictPolicy ParsePolicy(string raw) => raw switch
    {
        "overlay-wins" => MergeConflictPolicy.OverlayWins,
        "base-wins" => MergeConflictPolicy.BaseWins,
        "fail" => MergeConflictPolicy.Fail,
        _ => MergeConflictPolicy.OverlayWins,
    };

    private static void WriteJson(object payload, string outPath)
    {
        var dir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(outPath, JsonSerializer.Serialize(payload, JsonOptions));
    }
}
