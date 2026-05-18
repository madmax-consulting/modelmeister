using ModelMeister.Model.Primitives;

namespace ModelMeister.Inriver.Snapshot;

/// <summary>
/// Inriver-side representation of the full model. Filled by <see cref="InriverSnapshot"/>
/// and consumed by <c>ModelDiffer</c>. Parallel to <c>LoadedModel</c> in shape.
/// </summary>
public sealed class LiveModel
{
    /// <summary>The environment URL the snapshot was captured from.</summary>
    public required string EnvironmentUrl { get; init; }
    /// <summary>UTC timestamp at the start of capture.</summary>
    public required DateTime CapturedUtc { get; init; }

    public IReadOnlyList<LiveEntityType> EntityTypes { get; init; } = [];
    public IReadOnlyList<LiveCvl> Cvls { get; init; } = [];
    public IReadOnlyList<LiveCategory> Categories { get; init; } = [];
    public IReadOnlyList<LiveFieldset> Fieldsets { get; init; } = [];
    public IReadOnlyList<LiveLinkType> LinkTypes { get; init; } = [];
    public IReadOnlyList<LiveRole> Roles { get; init; } = [];
    public IReadOnlyList<LivePermission> Permissions { get; init; } = [];
    public IReadOnlyList<LiveCompletenessDefinition> CompletenessDefinitions { get; init; } = [];
    public IReadOnlyList<LiveRestrictedFieldPermission> RestrictedFieldPermissions { get; init; } = [];
    public IReadOnlyList<string> Languages { get; init; } = [];
}

public sealed class LiveEntityType
{
    public required string Id { get; init; }
    public required LocaleString Name { get; init; }
    public bool IsLinkEntityType { get; init; }
    public string? DisplayNameFieldId { get; init; }
    public string? DisplayDescriptionFieldId { get; init; }
    public IReadOnlyList<LiveFieldType> Fields { get; init; } = [];
}

public sealed class LiveFieldType
{
    public required string Id { get; init; }
    public required string EntityTypeId { get; init; }
    public required LocaleString Name { get; init; }
    public LocaleString Description { get; init; } = new();
    public required Datatype DataType { get; init; }
    public bool Mandatory { get; init; }
    public bool Unique { get; init; }
    public bool ReadOnly { get; init; }
    public bool Hidden { get; init; }
    public bool MultiValue { get; init; }
    public bool TrackChanges { get; init; }
    public bool IsDisplayName { get; init; }
    public bool IsDisplayDescription { get; init; }
    public bool ExcludeFromDefaultView { get; init; }
    public bool ExpressionSupport { get; init; }
    public int Index { get; init; }
    public string? CategoryId { get; init; }
    public string? CvlId { get; init; }
    public string? DefaultValue { get; init; }
    public Dictionary<string, string> Settings { get; init; } = new(StringComparer.Ordinal);
    /// <summary>
    /// Captured set of inriver <c>Unit</c> attachments (id + display name). Preserved on update so we
    /// don't blank a field's unit list when the code model is silent on units.
    /// </summary>
    public IReadOnlyList<LiveFieldUnit> Units { get; init; } = [];
}

public sealed record LiveFieldUnit(int Id, LocaleString Name);

public sealed class LiveCvl
{
    public required string Id { get; init; }
    public required string DataTypeRaw { get; init; }
    public required CvlDataType DataType { get; init; }
    public string? ParentId { get; init; }
    public bool CustomValueList { get; init; }
    public IReadOnlyList<LiveCvlValue> Values { get; init; } = [];
}

public sealed class LiveCvlValue
{
    public required int Id { get; init; }
    public required string CvlId { get; init; }
    public required string Key { get; init; }
    public LocaleString Value { get; init; } = new();
    public string? ParentKey { get; init; }
    public int Index { get; init; }
    public bool Deactivated { get; init; }
}

public sealed class LiveCategory
{
    public required string Id { get; init; }
    public required LocaleString Name { get; init; }
    public int Index { get; init; }
}

public sealed class LiveFieldset
{
    public required string Id { get; init; }
    public required string EntityTypeId { get; init; }
    public required LocaleString Name { get; init; }
    public LocaleString Description { get; init; } = new();
    public IReadOnlyList<string> FieldTypeIds { get; init; } = [];
}

public sealed class LiveLinkType
{
    public required string Id { get; init; }
    public required string SourceEntityTypeId { get; init; }
    public required string TargetEntityTypeId { get; init; }
    public string? LinkEntityTypeId { get; init; }
    public int Index { get; init; }
    public LocaleString SourceName { get; init; } = new();
    public LocaleString TargetName { get; init; } = new();
}

public sealed class LiveRole
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<LivePermission> Permissions { get; init; } = [];
}

public sealed class LivePermission
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
}

public sealed class LiveCompletenessDefinition
{
    public required int Id { get; init; }
    public required LocaleString Name { get; init; }
    public required string EntityTypeId { get; init; }
    public IReadOnlyList<LiveCompletenessGroup> Groups { get; init; } = [];
}

public sealed class LiveCompletenessGroup
{
    public required int Id { get; init; }
    public required LocaleString Name { get; init; }
    public int Weight { get; init; }
    public int SortOrder { get; init; }
    public int DefinitionId { get; init; }
    public IReadOnlyList<LiveCompletenessBusinessRule> Rules { get; init; } = [];
}

public sealed class LiveCompletenessBusinessRule
{
    public required int Id { get; init; }
    public required LocaleString Name { get; init; }
    public required string Type { get; init; }
    public int Weight { get; init; }
    public int SortOrder { get; init; }
    public IReadOnlyList<LiveCompletenessRuleSetting> Settings { get; init; } = [];
}

public sealed class LiveCompletenessRuleSetting
{
    public required int Id { get; init; }
    public required int BusinessRuleId { get; init; }
    public required string Type { get; init; }
    public required string Key { get; init; }
    public string Value { get; init; } = string.Empty;
}

public sealed class LiveRestrictedFieldPermission
{
    public required int Id { get; init; }
    public required int RoleId { get; init; }
    public required string RestrictionType { get; init; }
    public string? EntityTypeId { get; init; }
    public string? FieldTypeId { get; init; }
    public string? CategoryId { get; init; }
}
