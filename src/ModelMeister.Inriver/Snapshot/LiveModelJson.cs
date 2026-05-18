using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelMeister.Inriver.Snapshot;

/// <summary>JSON persistence for <see cref="LiveModel"/> — used for snapshot-before-apply backups.</summary>
public static class LiveModelJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Serialise a model as pretty-printed JSON.</summary>
    public static string Serialize(LiveModel model) => JsonSerializer.Serialize(model, Options);

    /// <summary>Deserialise a model from JSON. Returns null on null/empty input.</summary>
    public static LiveModel? Deserialize(string json) => JsonSerializer.Deserialize<LiveModel>(json, Options);

    /// <summary>Save the model to <paramref name="path"/>, creating the parent directory if needed.</summary>
    public static void Save(LiveModel model, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, Serialize(model));
    }
}
