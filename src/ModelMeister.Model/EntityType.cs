using ModelMeister.Model.Primitives;

namespace ModelMeister.Model;

/// <summary>
/// Base class for every inriver entity type. Subclasses are discovered by reflection (see
/// <see cref="Loading.ModelLoader"/>). The CLR type name becomes the inriver
/// <see cref="EntityTypeId"/> unless an init-only override is supplied.
/// </summary>
public abstract class EntityType
{
    protected EntityType()
    {
        EntityTypeId = GetType().Name;
        EntityTypeName = new LocaleString(NameHumanizer.Humanize(EntityTypeId));
    }

    /// <summary>The inriver entity-type ID. Defaults to the CLR type name.</summary>
    public string EntityTypeId { get; init; }

    /// <summary>The entity-type's display label in inriver. Distinct from any field named Name on the entity.</summary>
    public LocaleString EntityTypeName { get; init; }

    /// <summary>The entity-type's localised description.</summary>
    public LocaleString EntityTypeDescription { get; init; } = new();

    /// <summary>True if instances participate as link-entity types (carry data on a link rather than a node).</summary>
    public bool IsLinkEntityType { get; init; }

    /// <summary>Optional icon identifier shown by the inriver UI.</summary>
    public string? Icon { get; init; }

    /// <summary>Free-form per-entity settings forwarded to inriver verbatim.</summary>
    public Dictionary<string, string> Settings { get; init; } = new(StringComparer.Ordinal);
}
