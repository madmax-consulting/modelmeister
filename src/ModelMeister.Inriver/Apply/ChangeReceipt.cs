using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelMeister.Inriver.Apply;

/// <summary>Result of a <c>ChangeApplier.ApplyAsync</c> run — one entry per change attempted.</summary>
public sealed class ChangeReceipt
{
    /// <summary>The environment URL the run targeted.</summary>
    public required string EnvironmentUrl { get; init; }
    /// <summary>UTC timestamp when the run began.</summary>
    public required DateTime StartedUtc { get; init; }
    /// <summary>UTC timestamp when the run finished. Mutable so it can be stamped at end-of-run.</summary>
    public DateTime FinishedUtc { get; set; }
    /// <summary>Whether the run was a dry-run (no inriver writes performed).</summary>
    public required bool DryRun { get; init; }
    /// <summary>The per-change entries, in apply order.</summary>
    public List<ChangeReceiptEntry> Entries { get; init; } = [];
    /// <summary>Path to the pre-apply backup JSON, if one was taken.</summary>
    public string? BackupFile { get; init; }

    /// <summary>Number of changes that succeeded.</summary>
    public int Succeeded => Entries.Count(e => e.Succeeded);
    /// <summary>Number of changes that failed.</summary>
    public int Failed => Entries.Count(e => !e.Succeeded);

    /// <summary>Serialise as pretty-printed JSON.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, ReceiptJsonOptions.Default);

    /// <summary>Save the receipt to <paramref name="path"/>, creating the parent directory if needed.</summary>
    public void SaveTo(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, ToJson());
    }
}

/// <summary>A single change attempted by the applier.</summary>
public sealed class ChangeReceiptEntry
{
    /// <summary>The CLR type name of the originating <see cref="Diff.ModelChange"/>.</summary>
    public required string Kind { get; init; }
    /// <summary>Human-readable summary (mirrors <see cref="Diff.ModelChange.Describe"/>).</summary>
    public required string Description { get; init; }
    /// <summary>True iff the change applied without throwing.</summary>
    public required bool Succeeded { get; init; }
    /// <summary>The exception message on failure, otherwise null.</summary>
    public string? Error { get; init; }
    /// <summary>How long the call took, in milliseconds.</summary>
    public required long DurationMs { get; init; }
}

internal static class ReceiptJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
