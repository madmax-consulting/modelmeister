namespace ModelMeister.Model;

// Attributes in this file each have a 1:1 mapping to a `Field` property. The loader stamps the
// corresponding value onto the field instance after the property's `= new() { ... }` initializer
// has run. They exist so author-facing field declarations can stay scannable: the boolean and
// scalar flags lift out of object initializers into a compact attribute row above each field.
//
// Specifying the same property via BOTH an attribute and an object initializer is flagged by
// validator MM012 — pick one form per property. Attributes always win at runtime because the
// loader applies them last.
//
// Property bindings that can't reasonably ride in an attribute (LocaleString names/descriptions,
// strongly-typed default expressions, Settings dictionaries, role lists) stay in the object
// initializer.

/// <summary>Sets <see cref="Field.Mandatory"/>. inriver rejects an entity save when a mandatory field is empty.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class MandatoryAttribute : Attribute;

/// <summary>Sets <see cref="Field.Unique"/>. inriver enforces uniqueness across all entities of the type.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class UniqueAttribute : Attribute;

/// <summary>
/// Sets <see cref="Field.ReadOnly"/>. The field is visible in the UI but cannot be edited.
/// Suffixed with <c>Field</c> to disambiguate from <see cref="System.ComponentModel.ReadOnlyAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ReadOnlyFieldAttribute : Attribute;

/// <summary>Sets <see cref="Field.Hidden"/>. The field is excluded from the UI but kept in the model.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class HiddenAttribute : Attribute;

/// <summary>Sets <see cref="Field.MultiValue"/>. The field holds a comma-separated list on the wire.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class MultiValueAttribute : Attribute;

/// <summary>Sets <see cref="Field.PerMarket"/>. The field fans out into per-market sibling fields at load time.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class PerMarketAttribute : Attribute;

/// <summary>Sets <see cref="Field.SupportsExpression"/>. The field accepts an inriver Expression-Engine expression as its default.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class SupportsExpressionAttribute : Attribute;

/// <summary>Sets <see cref="Field.ShowInEntityOverview"/>. The field appears in the entity overview list view.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ShowInEntityOverviewAttribute : Attribute;

/// <summary>Sets <see cref="Field.IgnoreFieldInEpiserverExport"/>. The field is skipped by the Episerver export pipeline.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class IgnoreFieldInEpiserverExportAttribute : Attribute;

/// <summary>
/// Sets <see cref="Field.TrackChanges"/> to <c>true</c>. Largely redundant now that TrackChanges
/// defaults to <c>true</c> (the loader stamps it when unset) — kept for explicitness and
/// back-compat. To turn tracking <c>off</c>, use the object initializer
/// (<c>= new() { TrackChanges = false }</c>).
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class TrackChangesAttribute : Attribute;

/// <summary>
/// Sets <see cref="Field.ExcludeFromDefaultView"/> to <c>true</c>. See <see cref="TrackChangesAttribute"/>
/// for the rationale on why this only sets the <c>true</c> case.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ExcludeFromDefaultViewAttribute : Attribute;

/// <summary>Sets <see cref="Field.Index"/>. Controls field display order within an entity type.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class IndexAttribute(int value) : Attribute
{
    public int Value { get; } = value;
}

/// <summary>Sets <see cref="Field.NumberOfRows"/>. UI hint: number of rows the editor displays.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class NumberOfRowsAttribute(int value) : Attribute
{
    public int Value { get; } = value;
}

/// <summary>Sets <see cref="Field.RegExp"/>. Regular expression validating the field's input.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class RegExpAttribute(string pattern) : Attribute
{
    public string Pattern { get; } = pattern;
}

/// <summary>
/// Sets <see cref="Field.Category"/>. The category type must derive from <see cref="Category"/>.
/// Suffixed with <c>Field</c> to disambiguate from <see cref="System.ComponentModel.CategoryAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class FieldCategoryAttribute(Type category) : Attribute
{
    public Type Category { get; } = category;
}

/// <summary>
/// Sets <see cref="Field.Fieldsets"/>. Stack the attribute to bind the field to multiple fieldsets:
/// <code>[Fieldset(typeof(Chemistry)), Fieldset(typeof(Logistics))]</code>
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
public sealed class FieldsetAttribute(Type fieldset) : Attribute
{
    public Type Fieldset { get; } = fieldset;
}
