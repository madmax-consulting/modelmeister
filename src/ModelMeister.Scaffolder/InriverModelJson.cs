using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelMeister.Scaffolder;

/// <summary>
/// JSON DTOs mirroring the inriver model-export JSON shape.
/// Property names match the export (PascalCase) — System.Text.Json uses property names directly.
/// </summary>
public sealed class InriverModelJson
{
    public string? Version { get; set; }
    public string? DbVersion { get; set; }
    public string? CustomerName { get; set; }
    public Dictionary<string, string>? ServerSettings { get; set; }
    public List<JsonLanguage> Languages { get; set; } = [];
    public List<JsonCategory> Categories { get; set; } = [];
    public List<JsonLinkType> LinkTypes { get; set; } = [];
    public List<JsonEntityType> EntityTypes { get; set; } = [];
    public List<JsonFieldSet> FieldSets { get; set; } = [];
    public List<JsonFieldType> FieldTypes { get; set; } = [];
    [JsonPropertyName("CVLs")]
    public List<JsonCvl> Cvls { get; set; } = [];
    public List<JsonCvlValue> CvlValues { get; set; } = [];
    public JsonCompleteness? Completeness { get; set; }
    public JsonSecurity? Security { get; set; }

    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static InriverModelJson Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<InriverModelJson>(stream, Options)
            ?? throw new InvalidOperationException("Failed to deserialize inriver model JSON.");
    }
}

/// <summary>
/// Locale-keyed string. Mirrors the inriver export shape (<c>{ "StringMap": { "en": ..., "sv": ... } }</c>).
/// </summary>
public sealed class JsonLocaleString
{
    public Dictionary<string, string>? StringMap { get; set; }

    /// <summary>First non-null value in insertion order, or <see cref="string.Empty"/>.</summary>
    public string DefaultValue() => StringMap?.Values.FirstOrDefault() ?? string.Empty;

    /// <summary>True when the map is null/empty or every value is null/empty.</summary>
    public bool IsEmpty() =>
        StringMap is null || StringMap.Count == 0 || StringMap.Values.All(string.IsNullOrEmpty);
}

/// <summary>JSON DTO for an inriver language. <see cref="Name"/> is the ISO code.</summary>
public sealed class JsonLanguage
{
    public string Name { get; set; } = string.Empty;       // ISO code
}

/// <summary>JSON DTO for an inriver category.</summary>
public sealed class JsonCategory
{
    public string Id { get; set; } = string.Empty;
    public JsonLocaleString? Name { get; set; }
    public int Index { get; set; }
}

/// <summary>JSON DTO for an inriver entity type, including its nested field types and fieldsets.</summary>
public sealed class JsonEntityType
{
    public string Id { get; set; } = string.Empty;
    public JsonLocaleString? Name { get; set; }
    public bool IsLinkEntityType { get; set; }
    public List<JsonFieldType>? FieldTypes { get; set; }
    public List<JsonLinkType>? LinkTypes { get; set; }
    public List<JsonFieldSet>? FieldSets { get; set; }
    public string? GetDisplayNameFieldTypeId { get; set; }
    public string? GetDisplayDescriptionFieldTypeId { get; set; }
}

/// <summary>JSON DTO for an inriver field type.</summary>
public sealed class JsonFieldType
{
    public string Id { get; set; } = string.Empty;
    public JsonLocaleString? Name { get; set; }
    public JsonLocaleString? Description { get; set; }
    public string EntityTypeId { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool Mandatory { get; set; }
    public bool Unique { get; set; }
    public int Index { get; set; }
    public string? CategoryId { get; set; }
    public string? DefaultValue { get; set; }
    public bool Hidden { get; set; }
    public bool ReadOnly { get; set; }
    public bool IsDisplayName { get; set; }
    public bool IsDisplayDescription { get; set; }
    public Dictionary<string, string>? Settings { get; set; }
    public bool ExcludeFromDefaultView { get; set; }
    [JsonPropertyName("CVLId")]
    public string? CvlId { get; set; }
    public bool Multivalue { get; set; }
    public bool TrackChanges { get; set; }
    public bool ExpressionSupport { get; set; }
}

/// <summary>JSON DTO for an inriver link type.</summary>
public sealed class JsonLinkType
{
    public string Id { get; set; } = string.Empty;
    public string SourceEntityTypeId { get; set; } = string.Empty;
    public JsonLocaleString? SourceName { get; set; }
    public string TargetEntityTypeId { get; set; } = string.Empty;
    public JsonLocaleString? TargetName { get; set; }
    public string? LinkEntityTypeId { get; set; }
    public int Index { get; set; }
}

/// <summary>JSON DTO for an inriver fieldset (a named subset of an entity-type's fields).</summary>
public sealed class JsonFieldSet
{
    public string Id { get; set; } = string.Empty;
    public JsonLocaleString? Name { get; set; }
    public JsonLocaleString? Description { get; set; }
    public string EntityTypeId { get; set; } = string.Empty;
    public List<string>? FieldTypes { get; set; }
}

/// <summary>JSON DTO for a CVL (controlled value list) definition. Values are in <see cref="InriverModelJson.CvlValues"/>.</summary>
public sealed class JsonCvl
{
    public string Id { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public string? ParentId { get; set; }
    public bool CustomValueList { get; set; }
    public bool? Activated { get; set; }
}

/// <summary>JSON DTO for a single CVL value (a row inside a <see cref="JsonCvl"/>).</summary>
public sealed class JsonCvlValue
{
    public int Id { get; set; }
    [JsonPropertyName("CVLId")]
    public string CvlId { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public JsonElement Value { get; set; }
    public int Index { get; set; }
    public string? ParentKey { get; set; }
    public bool Deactivated { get; set; }
}

/// <summary>JSON DTO for the completeness section of an inriver model export.</summary>
public sealed class JsonCompleteness
{
    public List<JsonCompletenessDefinition>? CompletenessDefinitions { get; set; }
    public List<JsonCompletenessGroup>? CompletenessGroups { get; set; }
    public List<JsonCompletenessBusinessRule>? CompletenessBusinessRules { get; set; }
}

public sealed class JsonCompletenessDefinition
{
    public int Id { get; set; }
    public JsonLocaleString? Name { get; set; }
    public string EntityTypeId { get; set; } = string.Empty;
    public List<int>? GroupIds { get; set; }
}

public sealed class JsonCompletenessGroup
{
    public int Id { get; set; }
    public JsonLocaleString? Name { get; set; }
    public int Weight { get; set; }
    public int SortOrder { get; set; }
    public int CompletenessDefinitionId { get; set; }
    public List<int>? RuleIds { get; set; }
}

public sealed class JsonCompletenessBusinessRule
{
    public int Id { get; set; }
    public JsonLocaleString? Name { get; set; }
    public int Weight { get; set; }
    public int SortOrder { get; set; }
    public string Type { get; set; } = string.Empty;
    public List<JsonCompletenessRuleSetting>? RuleSettings { get; set; }
    public List<int>? GroupIds { get; set; }
}

public sealed class JsonCompletenessRuleSetting
{
    public int Id { get; set; }
    public int BusinessRuleId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
}

/// <summary>JSON DTO for the security section of an inriver model export (roles + per-field restrictions).</summary>
public sealed class JsonSecurity
{
    public List<JsonRole>? Roles { get; set; }
    public List<JsonRestrictedFieldPermission>? RestrictedFieldPermissions { get; set; }
}

/// <summary>JSON DTO for an inriver role and its granted permissions.</summary>
public sealed class JsonRole
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<JsonPermission>? Permissions { get; set; }
}

public sealed class JsonPermission
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public sealed class JsonRestrictedFieldPermission
{
    public int Id { get; set; }
    public int RoleId { get; set; }
    public string? RestrictionType { get; set; }
    public string? EntityTypeId { get; set; }
    public string? FieldTypeId { get; set; }
    public string? CategoryId { get; set; }
}
