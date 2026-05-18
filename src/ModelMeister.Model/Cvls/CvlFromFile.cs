using System.Text.Json;
using ModelMeister.Model.Primitives;

namespace ModelMeister.Model.Cvls;

/// <summary>
/// CVL whose values are loaded from a JSON file beside the assembly. Expected shape:
/// <code>
/// [
///   { "key": "red",   "value": { "en-US": "Red",   "sv-SE": "Röd" },   "index": 0 },
///   { "key": "blue",  "value": { "en-US": "Blue",  "sv-SE": "Blå" },  "index": 1 }
/// ]
/// </code>
/// </summary>
public abstract class CvlFromFile : Cvl
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Path to the JSON source — absolute, or relative to the assembly's base directory.</summary>
    public abstract string FilePath { get; }

    public override IEnumerable<CvlValue> GetValues()
    {
        string path;
        string json;
        try
        {
            path = ResolvePath(FilePath);
            json = File.ReadAllText(path);
        }
        catch (FileNotFoundException ex)
        {
            throw new CvlSourceMissingException(GetType().Name, FilePath, ex);
        }

        var raw = JsonSerializer.Deserialize<List<RawEntry>>(json, JsonOpts) ?? [];

        return raw.Select((e, i) => new CvlValue(
            Key: e.Key ?? throw new InvalidDataException($"CVL value entry missing 'key' in {path}"),
            Value: ToLocaleString(e.Value),
            Parent: e.Parent,
            Index: e.Index ?? i,
            Deactivated: e.Deactivated ?? false));
    }

    /// <summary>Resolves a path: rooted-and-existing wins, else tries the assembly base dir, else the raw path.</summary>
    private static string ResolvePath(string filePath)
    {
        if (Path.IsPathRooted(filePath) && File.Exists(filePath)) return filePath;

        var candidate = Path.Combine(AppContext.BaseDirectory, filePath);
        if (File.Exists(candidate)) return candidate;

        if (File.Exists(filePath)) return filePath;

        throw new FileNotFoundException($"CVL data file not found: {filePath}");
    }

    private static LocaleString ToLocaleString(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
            return new LocaleString(value.GetString() ?? string.Empty);

        if (value.ValueKind != JsonValueKind.Object)
            return new LocaleString();

        var ls = new LocaleString();
        foreach (var prop in value.EnumerateObject())
        {
            var text = prop.Value.GetString() ?? string.Empty;
            if (string.IsNullOrEmpty(ls.DefaultValue)) ls.DefaultValue = text;
            ls.With(prop.Name, text);
        }
        return ls;
    }

    /// <summary>Raw shape of a single entry in the JSON file — mirrors <see cref="CvlValue"/> with optionals.</summary>
    private sealed record RawEntry(
        string? Key,
        JsonElement Value,
        string? Parent,
        int? Index,
        bool? Deactivated);
}

/// <summary>
/// Thrown when a <see cref="CvlFromFile"/>-derived CVL cannot read its source file. Carries
/// validation code MM076 so the CLI can surface it as a model-validation issue instead of a raw
/// stack trace.
/// </summary>
public sealed class CvlSourceMissingException(string cvlTypeName, string missingPath, Exception inner)
    : Exception($"{Code} CVL '{cvlTypeName}' data file not found: {missingPath}", inner)
{
    public const string Code = "MM076";

    public string CvlTypeName { get; } = cvlTypeName;
    public string MissingPath { get; } = missingPath;
}
