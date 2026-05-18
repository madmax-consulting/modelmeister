using ModelMeister.Model.Primitives;

namespace ModelMeister.Model.Security;

/// <summary>Restricts a field for a given role with the supplied <see cref="RestrictionType"/>.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class RestrictedAttribute(Type role, RestrictionType restriction) : Attribute
{
    public Type Role { get; } = role;
    public RestrictionType Restriction { get; } = restriction;
}

/// <summary>Marks a field as read-only for a role.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class ReadOnlyForAttribute(Type role) : Attribute
{
    public Type Role { get; } = role;
}

/// <summary>Marks a field as hidden for a role.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class HiddenForAttribute(Type role) : Attribute
{
    public Type Role { get; } = role;
}

/// <summary>Marks a field as visible-only for a role (i.e. not editable).</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class VisibleForAttribute(Type role) : Attribute
{
    public Type Role { get; } = role;
}

/// <summary>Marks a field as editable-only for a role (no other access).</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class EditOnlyForAttribute(Type role) : Attribute
{
    public Type Role { get; } = role;
}

/// <summary>
/// Class-level restriction: applies <see cref="Restriction"/> to every field in
/// <see cref="Category"/> for the given <see cref="Role"/>, optionally scoped to a subset of
/// entity types via <see cref="EntityTypes"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class RestrictedCategoryAttribute(Type category, Type role, RestrictionType restriction) : Attribute
{
    public Type Category { get; } = category;
    public Type Role { get; } = role;
    public RestrictionType Restriction { get; } = restriction;
    public Type[] EntityTypes { get; init; } = [];
}
