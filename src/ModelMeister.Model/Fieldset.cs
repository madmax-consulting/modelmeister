using ModelMeister.Model.Primitives;

namespace ModelMeister.Model;

/// <summary>
/// Base class for an inriver Fieldset — a named view onto a subset of an entity type's fields.
/// The CLR type name with the optional <c>Fieldset</c> suffix stripped becomes the inriver
/// <see cref="FieldsetId"/>.
/// </summary>
public abstract class Fieldset
{
    private const string Suffix = "Fieldset";

    protected Fieldset()
    {
        var name = GetType().Name;
        if (name.EndsWith(Suffix, StringComparison.Ordinal))
            name = name[..^Suffix.Length];
        FieldsetId = name;
        Name = new LocaleString(FieldsetId);
    }

    /// <summary>The inriver fieldset ID.</summary>
    public string FieldsetId { get; init; }

    /// <summary>The fieldset's localised display name.</summary>
    public LocaleString Name { get; init; }

    /// <summary>The fieldset's localised description.</summary>
    public LocaleString Description { get; init; } = new();

    /// <summary>The owning entity type's CLR type.</summary>
    public abstract Type EntityType { get; }

    /// <summary>Sort order of the fieldset within the entity type.</summary>
    public virtual int Index => 0;
}
