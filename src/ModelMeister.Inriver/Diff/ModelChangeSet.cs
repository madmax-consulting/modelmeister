using ModelMeister.Model.Loading;

namespace ModelMeister.Inriver.Diff;

/// <summary>Result of comparing a code-defined model to a live snapshot.</summary>
public sealed class ModelChangeSet
{
    /// <summary>The ordered set of changes produced by the differ. The applier re-orders this list itself.</summary>
    public IReadOnlyList<ModelChange> Changes { get; init; } = [];

    /// <summary>Non-fatal observations (e.g. a datatype change suppressed by policy).</summary>
    public IReadOnlyList<DiffWarning> Warnings { get; init; } = [];

    /// <summary>True iff <see cref="Changes"/> is empty.</summary>
    public bool IsEmpty => Changes.Count == 0;

    /// <summary>Project the change list to a specific <see cref="ModelChange"/> subtype.</summary>
    public IEnumerable<T> Of<T>() where T : ModelChange => Changes.OfType<T>();
}

/// <summary>A non-fatal diagnostic surfaced by the differ.</summary>
public sealed record DiffWarning(string Code, string Message);

/// <summary>Base record for the typed changes that make up a <see cref="ModelChangeSet"/>.</summary>
public abstract record ModelChange
{
    /// <summary>Human-readable summary used by reporting and dry-run logging.</summary>
    public abstract string Describe();
}

// ---- Languages ----
public sealed record AddLanguage(string IsoCode) : ModelChange
{
    public override string Describe() => $"+ Language {IsoCode}";
}

// ---- Categories ----
public sealed record AddCategory(LoadedCategory Category) : ModelChange
{
    public override string Describe() => $"+ Category {Category.CategoryId}";
}
public sealed record UpdateCategory(LoadedCategory Category) : ModelChange
{
    public override string Describe() => $"~ Category {Category.CategoryId}";
}
public sealed record DeleteCategory(string Id) : ModelChange
{
    public override string Describe() => $"- Category {Id}";
}

// ---- CVLs ----
public sealed record AddCvl(LoadedCvl Cvl) : ModelChange
{
    public override string Describe() => $"+ CVL {Cvl.CvlId}";
}
public sealed record UpdateCvl(LoadedCvl Cvl) : ModelChange
{
    public override string Describe() => $"~ CVL {Cvl.CvlId}";
}
public sealed record DeleteCvl(string Id) : ModelChange
{
    public override string Describe() => $"- CVL {Id}";
}

// ---- CVL values ----
public sealed record AddCvlValue(string CvlId, Model.CvlValue Value) : ModelChange
{
    public override string Describe() => $"+ CVLValue {CvlId}/{Value.Key}";
}
public sealed record UpdateCvlValue(string CvlId, int LiveId, Model.CvlValue Value) : ModelChange
{
    public override string Describe() => $"~ CVLValue {CvlId}/{Value.Key}";
}
public sealed record DeactivateCvlValue(string CvlId, int LiveId, string Key) : ModelChange
{
    public override string Describe() => $"- CVLValue {CvlId}/{Key} (deactivate)";
}

// ---- Entity types ----
public sealed record AddEntityType(LoadedEntityType EntityType) : ModelChange
{
    public override string Describe() => $"+ EntityType {EntityType.EntityTypeId}";
}
public sealed record UpdateEntityType(LoadedEntityType EntityType) : ModelChange
{
    public override string Describe() => $"~ EntityType {EntityType.EntityTypeId}";
}
public sealed record DeleteEntityType(string Id) : ModelChange
{
    public override string Describe() => $"- EntityType {Id}";
}

// ---- Field types ----
public sealed record AddFieldType(LoadedField Field, LoadedEntityType Owner) : ModelChange
{
    public override string Describe() => $"+ FieldType {Field.Id}";
}
public sealed record UpdateFieldType(LoadedField Field, LoadedEntityType Owner) : ModelChange
{
    public override string Describe() => $"~ FieldType {Field.Id}";
}
public sealed record DeleteFieldType(string Id) : ModelChange
{
    public override string Describe() => $"- FieldType {Id}";
}
public sealed record ChangeFieldDatatype(LoadedField Field, LoadedEntityType Owner, Model.Primitives.Datatype FromType, Model.Primitives.Datatype ToType) : ModelChange
{
    public override string Describe() => $"! FieldType {Field.Id} datatype {FromType} -> {ToType} (DANGEROUS)";
}

// ---- Fieldsets ----
public sealed record AddFieldset(LoadedFieldset Fieldset) : ModelChange
{
    public override string Describe() => $"+ FieldSet {Fieldset.FieldsetId}";
}
public sealed record UpdateFieldset(LoadedFieldset Fieldset) : ModelChange
{
    public override string Describe() => $"~ FieldSet {Fieldset.FieldsetId}";
}
public sealed record DeleteFieldset(string Id) : ModelChange
{
    public override string Describe() => $"- FieldSet {Id}";
}
public sealed record AddFieldToFieldset(string FieldsetId, string FieldTypeId) : ModelChange
{
    public override string Describe() => $"+ {FieldTypeId} -> FieldSet {FieldsetId}";
}
public sealed record RemoveFieldFromFieldset(string FieldsetId, string FieldTypeId) : ModelChange
{
    public override string Describe() => $"- {FieldTypeId} from FieldSet {FieldsetId}";
}

// ---- Link types ----
public sealed record AddLinkType(LoadedLinkType LinkType) : ModelChange
{
    public override string Describe() => $"+ LinkType {LinkType.LinkTypeId}";
}
public sealed record UpdateLinkType(LoadedLinkType LinkType) : ModelChange
{
    public override string Describe() => $"~ LinkType {LinkType.LinkTypeId}";
}
public sealed record DeleteLinkType(string Id) : ModelChange
{
    public override string Describe() => $"- LinkType {Id}";
}

// ---- Roles ----
public sealed record AddRole(LoadedRole Role) : ModelChange
{
    public override string Describe() => $"+ Role {Role.Name}";
}
public sealed record UpdateRole(LoadedRole Role) : ModelChange
{
    public override string Describe() => $"~ Role {Role.Name}";
}
public sealed record DeleteRole(int LiveId, string Name) : ModelChange
{
    public override string Describe() => $"- Role {Name}";
}
public sealed record AddPermissionToRole(int RoleId, int PermissionId, string PermissionName, string RoleName) : ModelChange
{
    public override string Describe() => $"+ {PermissionName} -> Role {RoleName}";
}
public sealed record RemovePermissionFromRole(int RoleId, int PermissionId, string PermissionName, string RoleName) : ModelChange
{
    public override string Describe() => $"- {PermissionName} from Role {RoleName}";
}

// ---- Restricted field permissions ----
public sealed record AddRestrictedFieldPermission(Snapshot.LiveRestrictedFieldPermission Desired) : ModelChange
{
    public override string Describe() => $"+ Restriction {Desired.RestrictionType} on {Desired.FieldTypeId ?? Desired.CategoryId} for role #{Desired.RoleId}";
}
public sealed record RemoveRestrictedFieldPermission(int LiveId) : ModelChange
{
    public override string Describe() => $"- Restriction #{LiveId}";
}

// ---- Completeness ----
public sealed record AddCompletenessGroup(LoadedCompletenessGroup Group) : ModelChange
{
    public override string Describe() => $"+ CompletenessGroup {Group.Name.DefaultValue}";
}
public sealed record UpdateCompletenessGroup(LoadedCompletenessGroup Group) : ModelChange
{
    public override string Describe() => $"~ CompletenessGroup {Group.Name.DefaultValue}";
}
