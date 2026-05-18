using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using ModelMeister.Model.Primitives;

namespace ModelMeister.Model;

/// <summary>
/// Static helpers that take some of the boilerplate out of authoring model code. Every helper here
/// is purely sugar — anything you can express with the shorthand you can also express longhand.
/// </summary>
public static class Mm
{
    /// <summary>Shorthand for <c>new LocaleString(default).With(lang, text)</c> pairs.</summary>
    /// <example><code>Name = Mm.Loc(("en", "Brand"), ("sv", "Varumärke"));</code></example>
    public static LocaleString Loc(params (string Lang, string Text)[] entries)
    {
        if (entries.Length == 0) return new LocaleString();
        var head = new LocaleString(entries[0].Text);
        for (var i = 1; i < entries.Length; i++) head = head.With(entries[i].Lang, entries[i].Text);
        return head;
    }

    /// <summary>One-letter <see cref="LocaleString"/> ctor for the common case of a single language string.</summary>
    public static LocaleString L(string text) => new(text);

    /// <summary>
    /// Resolves a field property expression to the inriver field id at runtime. Use it from
    /// completeness or security helpers when you want a compile-checked reference instead of a
    /// string literal. <typeparamref name="TEntity"/> must be the concrete entity type — the id is
    /// computed as <c>{EntityTypeId}{PropertyName}</c> exactly like the loader does.
    /// </summary>
    /// <example>
    /// <code>
    /// var id = Mm.Field&lt;Product&gt;(p => p.Name); // -> "ProductName"
    /// </code>
    /// </example>
    public static string Field<TEntity>(Expression<Func<TEntity, Model.Field>> selector)
        where TEntity : EntityType, new()
    {
        if (selector.Body is not MemberExpression { Member: PropertyInfo prop })
            throw new ArgumentException("Selector must be a simple property access, e.g. p => p.Name", nameof(selector));
        var entityId = new TEntity().EntityTypeId;
        return entityId + prop.Name;
    }

    /// <summary>
    /// Resolves a chain of field-property + (optional) entity-property to a fully-qualified field
    /// id. Useful for inheritance-aware references when the same property lives on a base class.
    /// </summary>
    public static string FieldOn<TEntity>(string propertyName) where TEntity : EntityType, new()
        => new TEntity().EntityTypeId + propertyName;

    /// <summary>
    /// Resolves the inriver link-type id for <typeparamref name="TLinkType"/>. Reads
    /// <see cref="LinkType.LinkTypeId"/> from a fresh instance, falling back to the CLR type name
    /// (matching <c>LinkType</c>'s "default to type name when not set" rule).
    /// </summary>
    public static string LinkTypeId<TLinkType>() where TLinkType : LinkType, new()
        => new TLinkType().LinkTypeId ?? typeof(TLinkType).Name;

    /// <summary>Resolves the inriver CVL id for <typeparamref name="TCvl"/>.</summary>
    public static string CvlId<TCvl>() where TCvl : Cvl, new() => new TCvl().CvlId;

    /// <summary>Resolves the inriver category id for <typeparamref name="TCategory"/>.</summary>
    public static string CategoryId<TCategory>() where TCategory : Category, new()
        => new TCategory().CategoryId;

    /// <summary>Builds a list of <see cref="Type"/>s — keeps fieldset / category lists tidy.</summary>
    public static IReadOnlyList<Type> Types(params Type[] ts) => ts;
}

/// <summary>
/// Static-only shorthand for declaring the entity-type slot in <see cref="Field{TData}"/>.
/// </summary>
public static class FieldEx
{
    /// <summary>Marks a field as required ("Mandatory = true").</summary>
    public static T Required<T>(this T f) where T : Field
    {
        SetInit(f, nameof(Field.Mandatory), true);
        return f;
    }

    /// <summary>Marks a field as unique-within-the-entity.</summary>
    public static T UniqueValue<T>(this T f) where T : Field
    {
        SetInit(f, nameof(Field.Unique), true);
        return f;
    }

    /// <summary>Marks a field as multi-value.</summary>
    public static T Multi<T>(this T f) where T : Field
    {
        SetInit(f, nameof(Field.MultiValue), true);
        return f;
    }

    /// <summary>Sets the field's index (sort order) within the entity.</summary>
    public static T At<T>(this T f, int index) where T : Field
    {
        SetInit(f, nameof(Field.Index), index);
        return f;
    }

    /// <summary>Binds the field to one or more fieldsets without manually building a list.</summary>
    public static T In<T>(this T f, params Type[] fieldsets) where T : Field
    {
        SetInit(f, nameof(Field.Fieldsets), fieldsets);
        return f;
    }

    static void SetInit(object target, string name, object? value)
    {
        var prop = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
                   ?? throw new InvalidOperationException($"Property {name} not found on {target.GetType()}");
        prop.SetValue(target, value);
    }
}
