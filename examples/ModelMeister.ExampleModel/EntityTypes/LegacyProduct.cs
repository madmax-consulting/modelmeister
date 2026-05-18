using ModelMeister.ExampleModel.Categories;
using ModelMeister.ExampleModel.Fieldsets;
using ModelMeister.ExampleModel.Roles;
using ModelMeister.Model;
using ModelMeister.Model.Lifecycle;
using ModelMeister.Model.Primitives;
using ModelMeister.Model.Security;

namespace ModelMeister.ExampleModel.EntityTypes;

/// <summary>
/// Exercises lifecycle + restriction attributes:
/// <list type="bullet">
///   <item><see cref="RestrictedCategoryAttribute"/> (class-level)</item>
///   <item><see cref="DeletedAttribute"/> on both the class and on a property</item>
///   <item><see cref="IgnoreMigrationAttribute"/> on a property</item>
///   <item><see cref="ReadOnlyForAttribute"/>, <see cref="VisibleForAttribute"/>,
///         <see cref="EditOnlyForAttribute"/>, <see cref="HiddenForAttribute"/>,
///         <see cref="RestrictedAttribute"/></item>
/// </list>
/// Also exercises entity-level <see cref="EntityType.Icon"/> and <see cref="EntityType.Settings"/>,
/// and the init-time role lists on <see cref="Field"/> (<see cref="Field.ReadOnlyFor"/> etc.) as an
/// alternative to per-field attributes.
/// </summary>
[Deleted]
[RestrictedCategory(typeof(LegalCategory), typeof(Reader), RestrictionType.Readonly)]
public sealed class LegacyProduct : TranslatableEntity
{
    public LegacyProduct()
    {
        Icon = "legacy.svg";
        Settings["Sunset"] = "2025-12-31";
        EntityTypeDescription = new LocaleString("Discontinued — kept for diff/apply Delete coverage");
    }

    /// <summary>Property-level [Deleted]: applier drops the field with --allow-deletes, else warns.</summary>
    [Deleted]
    public Field<string> ObsoleteId { get; init; } = new();

    /// <summary>[IgnoreMigration]: applier skips the destructive datatype-change leg.</summary>
    [IgnoreMigration]
    public Field<string> MigratingShape { get; init; } = new();

    [ReadOnlyFor(typeof(Reader))]
    [VisibleFor(typeof(Editor))]
    public Field<string> InternalNote { get; init; } = new();

    [EditOnlyFor(typeof(Translator))]
    public Field<LocaleString> TranslatorOnlyCopy { get; init; } = new();

    [Restricted(typeof(Admin), RestrictionType.Hidden)]
    public Field<double> InternalScore { get; init; } = new();

    /// <summary>
    /// Init-time role lists — equivalent to per-attribute restriction, but specified inline.
    /// Useful when the same restriction pattern repeats across many fields.
    /// </summary>
    public Field<string> AuditTrail { get; init; } = new()
    {
        ReadOnlyFor = new[] { typeof(Reader), typeof(Translator) },
        HiddenFor = new[] { typeof(LegalReviewer) },
        EditableFor = new[] { typeof(Editor) },
        VisibleFor = new[] { typeof(Admin) },
    };

    public Field<bool> WasPublished { get; init; } = new() { DefaultValue = true };
}
