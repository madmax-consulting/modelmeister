using ModelMeister.Model.Primitives;

namespace ModelMeister.Model.Loading;

/// <summary>
/// The reflection-derived, in-memory snapshot of a model assembly. Produced by
/// <see cref="ModelLoader.LoadFromAssembly"/> and consumed by the validator, differ and tooling.
/// </summary>
public sealed class LoadedModel
{
    public IReadOnlyList<LoadedEntityType> EntityTypes { get; init; } = [];
    public IReadOnlyList<LoadedCvl> Cvls { get; init; } = [];
    public IReadOnlyList<LoadedCategory> Categories { get; init; } = [];
    public IReadOnlyList<LoadedFieldset> Fieldsets { get; init; } = [];
    public IReadOnlyList<LoadedLinkType> LinkTypes { get; init; } = [];
    public IReadOnlyList<LoadedRole> Roles { get; init; } = [];
    public IReadOnlyList<LoadedPermission> Permissions { get; init; } = [];
    public IReadOnlyList<LoadedCompletenessGroup> CompletenessGroups { get; init; } = [];
    public IReadOnlyList<LoadedSpecificationTemplate> SpecificationTemplates { get; init; } = [];
    public IReadOnlyList<Language> Languages { get; init; } = [];
}

public sealed class LoadedEntityType
{
    public required Type ClrType { get; init; }
    public required string EntityTypeId { get; init; }
    public required LocaleString Name { get; init; }
    public LocaleString Description { get; init; } = new();
    public bool IsLinkEntityType { get; init; }
    public string? Icon { get; init; }
    public Dictionary<string, string> Settings { get; init; } = new();
    public List<LoadedField> Fields { get; init; } = [];
    public bool MarkedForDeletion { get; init; }
}

public sealed class LoadedField
{
    public required Field Field { get; init; }
    public required string Id { get; init; }
    public required string EntityTypeId { get; init; }
    public required string PropertyName { get; init; }
    public required LocaleString Name { get; init; }
    public required Datatype DataType { get; init; }
    public IReadOnlyList<Attribute> Attributes { get; init; } = [];
    public bool MarkedForDeletion { get; init; }

    /// <summary>
    /// Names of <see cref="Field"/> properties that were specified both via attribute
    /// (e.g. <c>[Mandatory]</c>) AND via the object initializer (<c>= new() { Mandatory = true }</c>).
    /// Surfaced as validator code MM012. The attribute always wins at runtime — but having both is
    /// redundant and confusing, so the model author should pick one form.
    /// </summary>
    public IReadOnlyList<string> DuplicateAttributeFlags { get; init; } = [];

    /// <summary>Source-location of the C# declaration that produced this field, when available.</summary>
    public string? SourceLocation => Field.SourceLocation;
}

public sealed class LoadedCvl
{
    public required Type ClrType { get; init; }
    public required string CvlId { get; init; }
    public required CvlDataType DataType { get; init; }
    public Type? ParentCvlClrType { get; init; }
    public string? ParentCvlId { get; init; }
    public bool CustomValueList { get; init; }
    public Type? EntityTypeClrType { get; init; }
    public string? EntityTypeId { get; init; }
    public IReadOnlyList<CvlValue> Values { get; init; } = [];
}

public sealed class LoadedCategory
{
    public required Type ClrType { get; init; }
    public required string CategoryId { get; init; }
    public required LocaleString Name { get; init; }
    public int Index { get; init; }
    public bool OrderByName { get; init; }
    public bool IsReserved { get; init; }
}

public sealed class LoadedFieldset
{
    public required Type ClrType { get; init; }
    public required string FieldsetId { get; init; }
    public required LocaleString Name { get; init; }
    public LocaleString Description { get; init; } = new();
    public required string EntityTypeId { get; init; }
    public int Index { get; init; }
}

public sealed class LoadedLinkType
{
    public required Type ClrType { get; init; }
    public required string LinkTypeId { get; init; }
    public required string SourceEntityTypeId { get; init; }
    public required string TargetEntityTypeId { get; init; }
    public string? LinkEntityTypeId { get; init; }
    public int Index { get; init; }
    public LocaleString SourceName { get; init; } = new();
    public LocaleString TargetName { get; init; } = new();
    public Dictionary<string, string> Settings { get; init; } = new();
}

public sealed class LoadedRole
{
    public required Type ClrType { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<Type> PermissionClrTypes { get; init; } = [];
    public IReadOnlyList<string> PermissionNames { get; init; } = [];
}

public sealed class LoadedPermission
{
    public required Type ClrType { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
}

public sealed class LoadedCompletenessGroup
{
    public required Type ClrType { get; init; }
    public required LocaleString Name { get; init; }
    public int Weight { get; init; }
    public int SortOrder { get; init; }
}

public sealed class LoadedSpecificationTemplate
{
    public required Type ClrType { get; init; }
    public required string TemplateId { get; init; }
    public required LocaleString Name { get; init; }
    public LocaleString Description { get; init; } = new();
    public IReadOnlyList<Type> CategoryClrTypes { get; init; } = [];
    public IReadOnlyList<Type> EntityTypeClrTypes { get; init; } = [];
}
