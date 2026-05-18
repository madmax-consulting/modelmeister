using ModelMeister.Model.Primitives;

namespace ModelMeister.Model;

/// <summary>
/// Base class for an inriver Link Type — a directed relationship between two entity types.
/// Prefer the generic <see cref="LinkType{TSource, TTarget}"/> when the source/target are known
/// at compile time; the non-generic base is retained for advanced scenarios.
/// </summary>
public abstract class LinkType
{
    /// <summary>Optional explicit ID; defaults to the CLR type name when omitted.</summary>
    public string? LinkTypeId { get; init; }

    /// <summary>The CLR type of the source entity.</summary>
    public abstract Type Source { get; }

    /// <summary>The CLR type of the target entity.</summary>
    public abstract Type Target { get; }

    /// <summary>Optional link-entity-type that carries data on the link itself.</summary>
    public virtual Type? LinkEntityType => null;

    /// <summary>Sort order in the inriver UI.</summary>
    public virtual int Index => 0;

    /// <summary>Localised label for the source end of the link.</summary>
    public LocaleString SourceName { get; init; } = new();

    /// <summary>Localised label for the target end of the link.</summary>
    public LocaleString TargetName { get; init; } = new();

    /// <summary>Free-form settings forwarded to inriver verbatim.</summary>
    public Dictionary<string, string> Settings { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>Strongly-typed convenience over <see cref="LinkType"/>.</summary>
public abstract class LinkType<TSource, TTarget> : LinkType
    where TSource : EntityType
    where TTarget : EntityType
{
    public override Type Source => typeof(TSource);
    public override Type Target => typeof(TTarget);
}
