namespace ModelMeister.Scaffolder;

/// <summary>
/// Symbol lookup used by <see cref="ExpressionParser"/> to turn raw inriver ids embedded in
/// expression text (field ids, link-type ids, CVL ids) into compile-time-checked references via
/// <c>nameof</c>. When a lookup misses, the parser falls back to a bare string literal so the
/// generated code still compiles.
/// </summary>
public sealed class ExpressionContext
{
    /// <summary>inriver field id (case-insensitive) -> (entity-class, property-name) on the scaffolded code.</summary>
    public IReadOnlyDictionary<string, FieldRef> Fields { get; }

    /// <summary>inriver link-type id -> scaffolded class name (no "LinkType" suffix on this project).</summary>
    public IReadOnlyDictionary<string, string> LinkTypes { get; }

    /// <summary>inriver CVL id -> scaffolded CVL class name (with the "Cvl" suffix already applied).</summary>
    public IReadOnlyDictionary<string, string> Cvls { get; }

    public ExpressionContext(
        IReadOnlyDictionary<string, FieldRef> fields,
        IReadOnlyDictionary<string, string> linkTypes,
        IReadOnlyDictionary<string, string> cvls)
    {
        Fields = fields;
        LinkTypes = linkTypes;
        Cvls = cvls;
    }

    /// <summary>Empty context — <see cref="ExpressionParser"/> will fall back to string literals for every id arg.</summary>
    public static ExpressionContext Empty { get; } = new(
        new Dictionary<string, FieldRef>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    /// <summary>Build a context from the parsed JSON model.</summary>
    /// <remarks>
    /// The class / property names follow exactly the scaffolder's conventions: entity class is
    /// <see cref="ProjectScaffolder.Sanitize"/>(EntityId); property name strips a leading entity-id
    /// prefix (same rule as <c>EntityTypeEmitter.PropertyNameFor</c>); CVL class adds the "Cvl"
    /// suffix; link type class is bare sanitized id. Keep this in sync with the emitters or
    /// generated <c>nameof(...)</c> references will not resolve.
    /// </remarks>
    public static ExpressionContext Build(InriverModelJson model)
    {
        var fields = new Dictionary<string, FieldRef>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in model.EntityTypes)
        {
            var entityClass = ProjectScaffolder.Sanitize(e.Id);
            foreach (var f in e.FieldTypes ?? [])
            {
                var propName = f.Id.StartsWith(e.Id, StringComparison.OrdinalIgnoreCase)
                    ? f.Id.Substring(e.Id.Length)
                    : f.Id;
                var saneProp = ProjectScaffolder.Sanitize(propName);
                if (saneProp.Length == 0) continue;
                fields[f.Id] = new FieldRef(entityClass, saneProp);
            }
        }

        var linkTypes = model.LinkTypes.ToDictionary(
            l => l.Id,
            l => ProjectScaffolder.Sanitize(l.Id),
            StringComparer.OrdinalIgnoreCase);

        var cvls = model.Cvls.ToDictionary(
            c => c.Id,
            c => ProjectScaffolder.Sanitize(c.Id) + "Cvl",
            StringComparer.OrdinalIgnoreCase);

        return new ExpressionContext(fields, linkTypes, cvls);
    }
}

/// <summary>A field's scaffolded location: which entity class owns it, and what property holds it.</summary>
public sealed record FieldRef(string EntityClass, string PropertyName);
