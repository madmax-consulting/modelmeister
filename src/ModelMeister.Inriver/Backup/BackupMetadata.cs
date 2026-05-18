using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelMeister.Inriver.Backup;

/// <summary>Information common to every backup file, regardless of scope.</summary>
public sealed record BackupMetadata
{
    /// <summary>Vault entry name of the source environment.</summary>
    public string EnvName { get; init; } = "";

    /// <summary>The env's URL at capture time. Recorded for audit, not used by restore.</summary>
    public string? EnvUrl { get; init; }

    /// <summary>Stage tag at capture time (Dev / Test / Prod / Unspecified).</summary>
    public string? Stage { get; init; }

    /// <summary>UTC capture timestamp.</summary>
    public DateTime CapturedAtUtc { get; init; } = DateTime.UtcNow;

    /// <summary>Optional user-supplied label (e.g., "before-rolling-back-cvls").</summary>
    public string? Label { get; init; }

    /// <summary>ModelMeister version that produced the backup.</summary>
    public string? Tool { get; init; }
}

/// <summary>Shared <see cref="JsonSerializerOptions"/> for all backup file types.</summary>
public static class BackupJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
