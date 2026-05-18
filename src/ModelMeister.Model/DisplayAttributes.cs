namespace ModelMeister.Model;

/// <summary>
/// Marks a <see cref="Field"/> property as the entity type's display name. At most one per entity
/// (enforced by validator MM010). Prefer this attribute over the <c>IsDisplayName = true</c>
/// initialiser — it keeps the field declaration focused on data and pushes the "this is the
/// display name" decision to the property where readers expect it.
/// </summary>
/// <example>
/// <code>
/// public sealed class Product : EntityType
/// {
///     [DisplayName]
///     public Field&lt;LocaleString&gt; Name { get; init; } = new() { Mandatory = true };
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class DisplayNameAttribute : Attribute { }

/// <summary>
/// Marks a <see cref="Field"/> property as the entity type's display description. At most one per
/// entity (enforced by validator MM011). Pairs with <see cref="DisplayNameAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class DisplayDescriptionAttribute : Attribute { }
