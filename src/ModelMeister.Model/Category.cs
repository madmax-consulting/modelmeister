using ModelMeister.Model.Primitives;

namespace ModelMeister.Model;

/// <summary>
/// Base class for an inriver Category — a grouping bucket for fields within an entity type.
/// The CLR type name with any trailing <c>CategoryType</c> or <c>Category</c> suffix stripped
/// becomes the inriver <see cref="CategoryId"/>.
/// </summary>
public abstract class Category : IFieldBinding
{
    private static readonly string[] Suffixes = ["CategoryType", "Category"];

    protected Category()
    {
        var name = GetType().Name;
        foreach (var suffix in Suffixes)
        {
            if (name.EndsWith(suffix, StringComparison.Ordinal))
            {
                name = name[..^suffix.Length];
                break;
            }
        }
        CategoryId = name;
        Name = new LocaleString(CategoryId);
    }

    /// <summary>The inriver category ID.</summary>
    public string CategoryId { get; init; }

    /// <summary>The category's localised display name.</summary>
    public LocaleString Name { get; init; }

    /// <summary>Sort order of the category within the entity type.</summary>
    public virtual int Index => 0;

    /// <summary>When true, fields inside this category are presented in alphabetical order in the UI.</summary>
    public virtual bool OrderByName => false;
}
