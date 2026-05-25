using ModelMeister.Model.Primitives;

namespace ModelMeister.Model;

/// <summary>
/// Base class for an inriver Specification Template — a curated set of categories and entity
/// types presented as a "specification" view. The CLR type name becomes the <see cref="TemplateId"/>.
/// </summary>
public abstract class SpecificationTemplate
{
    protected SpecificationTemplate()
    {
        TemplateId = GetType().Name;
        Name = new LocaleString(NameHumanizer.Humanize(TemplateId));
    }

    /// <summary>The inriver specification-template ID.</summary>
    public string TemplateId { get; init; }

    /// <summary>The template's localised display name.</summary>
    public LocaleString Name { get; init; }

    /// <summary>The template's localised description.</summary>
    public LocaleString Description { get; init; } = new();

    /// <summary>Categories included in the template.</summary>
    public virtual IReadOnlyList<Type> Categories => [];

    /// <summary>Entity types the template applies to.</summary>
    public virtual IReadOnlyList<Type> EntityTypes => [];
}
