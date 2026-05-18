using System.Runtime.CompilerServices;
using ModelMeister.Model.Expressions;
using ModelMeister.Model.Primitives;

namespace ModelMeister.Model;

/// <summary>
/// Untyped base. Authors should use <see cref="Field{TData}"/>. The loader stamps Id/Name on each
/// instance at registration time by reflecting over the owning entity type — no stack-walking.
/// </summary>
public abstract record Field
{
    /// <summary>
    /// Captures the file path of the user code that constructed this field, so validation errors
    /// can point at the declaration site. Auto-populated via <see cref="CallerFilePathAttribute"/>.
    /// </summary>
    public string? SourceFile { get; init; }

    /// <summary>
    /// Captures the line number of the user code that constructed this field. Auto-populated via
    /// <see cref="CallerLineNumberAttribute"/>.
    /// </summary>
    public int SourceLine { get; init; }

    /// <summary>
    /// Human-readable source-location string: <c>{file}:{line}</c>, or <c>null</c> when caller info isn't available.
    /// </summary>
    public string? SourceLocation =>
        string.IsNullOrEmpty(SourceFile) ? null : $"{SourceFile}:{SourceLine}";

    /// <summary>The inriver field ID. Stamped by <see cref="Loading.ModelLoader"/> as <c>{EntityTypeId}{PropertyName}</c>.</summary>
    public string? Id { get; init; }

    /// <summary>The owning entity-type ID. Stamped by the loader.</summary>
    public string? EntityTypeId { get; init; }

    /// <summary>The CLR property name on the owning entity type. Stamped by the loader.</summary>
    public string? PropertyName { get; init; }

    /// <summary>The field's localised display name.</summary>
    public LocaleString? Name { get; init; }

    /// <summary>The field's localised description.</summary>
    public LocaleString? Description { get; init; }

    /// <summary>The inriver data type derived from the <c>TData</c> generic argument.</summary>
    public abstract Datatype DataType { get; }

    /// <summary>CVL CLR type bound to this field, if any. Populated either explicitly or via the typed <c>Field&lt;TData, TBinding&gt;</c> overloads.</summary>
    public Type? Cvl { get; init; }

    /// <summary>Default value to seed in inriver when creating the field.</summary>
    public object? DefaultValue { get; init; }

    public bool MultiValue { get; init; }
    public bool Unique { get; init; }
    public bool Mandatory { get; init; }
    public bool ReadOnly { get; init; }
    public bool Hidden { get; init; }
    public bool IsDisplayName { get; init; }
    public bool IsDisplayDescription { get; init; }
    public bool? ExcludeFromDefaultView { get; init; }
    public bool? TrackChanges { get; init; }
    public int? Index { get; init; }

    /// <summary>Optional regular expression validating the field's input.</summary>
    public string? RegExp { get; init; }

    /// <summary>UI hint: number of rows the editor should display. Default 1.</summary>
    public int NumberOfRows { get; init; } = 1;

    public bool ShowInEntityOverview { get; init; }
    public bool IgnoreFieldInEpiserverExport { get; init; }

    /// <summary>Category CLR type bound to this field, if any.</summary>
    public Type? Category { get; init; }

    /// <summary>Fieldset CLR types this field belongs to.</summary>
    public IReadOnlyList<Type> Fieldsets { get; init; } = [];

    /// <summary>
    /// Convenience setter for declaring a single fieldset. Setting this property <b>replaces</b>
    /// <see cref="Fieldsets"/> with a one-element list, so it must not be combined with
    /// <see cref="Fieldsets"/> on the same field — whichever runs last wins (initialiser order).
    /// Use <see cref="Fieldsets"/> directly when binding to more than one fieldset.
    /// </summary>
    public Type? Fieldset
    {
        init => Fieldsets = value is null ? [] : [value];
    }

    /// <summary>True if the field fans out into per-market sibling fields at load time.</summary>
    public bool PerMarket { get; init; }

    /// <summary>Free-form per-field settings forwarded to inriver verbatim.</summary>
    public Dictionary<string, string> Settings { get; init; } = new(StringComparer.Ordinal);

    public IReadOnlyList<Type> ReadOnlyFor { get; init; } = [];
    public IReadOnlyList<Type> HiddenFor { get; init; } = [];
    public IReadOnlyList<Type> EditableFor { get; init; } = [];
    public IReadOnlyList<Type> VisibleFor { get; init; } = [];

    /// <summary>True when the field accepts an inriver Expression-Engine expression as its default value.</summary>
    public bool SupportsExpression { get; init; }

    /// <summary>The default expression in its raw, untyped form (for validator/walker consumption).</summary>
    public abstract Expr? RawDefaultExpression { get; }
}

/// <remarks>
/// <para>
/// CLR types that have no direct inriver counterpart are coerced to the closest supported
/// <see cref="Datatype"/>:
/// </para>
/// <list type="bullet">
///   <item><description><c>decimal</c>, <c>float</c> -> <see cref="Datatype.Double"/></description></item>
///   <item><description><c>long</c> -> <see cref="Datatype.Integer"/></description></item>
///   <item><description><c>DateTimeOffset</c> -> <see cref="Datatype.DateTime"/></description></item>
/// </list>
/// <para>
/// Prefer <see cref="double"/> over <see cref="decimal"/> for monetary fields if exact decimal
/// semantics matter to your code — inriver always stores Doubles, so a <c>Field&lt;decimal&gt;</c>
/// is functionally identical to <c>Field&lt;double&gt;</c> on the wire.
/// </para>
/// </remarks>
public record Field<TData> : Field
{
    /// <summary>
    /// Constructs a field. <paramref name="sourceFile"/> and <paramref name="sourceLine"/> are
    /// captured automatically — DO NOT pass them explicitly; they exist so validation errors can
    /// point at the user's declaration site.
    /// </summary>
    public Field([CallerFilePath] string? sourceFile = null, [CallerLineNumber] int sourceLine = 0)
    {
        SourceFile = sourceFile;
        SourceLine = sourceLine;
    }

    public override Datatype DataType => DatatypeOf<TData>.Value;

    /// <summary>Strongly-typed default expression. See <see cref="Expressions.Ex"/> for the function catalogue.</summary>
    public Expr<TData>? DefaultExpression { get; init; }

    public override Expr? RawDefaultExpression => DefaultExpression;
}

/// <summary>
/// Field with a single binding slot — TBinding is either a <see cref="Cvl"/> or a
/// <see cref="Category"/>. The ctor stamps the matching base property (<see cref="Field.Cvl"/>
/// or <see cref="Field.Category"/>) so downstream code reading through the <see cref="Field"/>
/// base reference sees the binding without going through CLR generic-arg introspection.
/// </summary>
public record Field<TData, TBinding> : Field<TData> where TBinding : IFieldBinding
{
    public Field([CallerFilePath] string? sourceFile = null, [CallerLineNumber] int sourceLine = 0)
        : base(sourceFile, sourceLine)
    {
        var binding = typeof(TBinding);
        if (typeof(Model.Cvl).IsAssignableFrom(binding))
            Cvl = binding;
        else if (typeof(Model.Category).IsAssignableFrom(binding))
            Category = binding;
        else
            throw new InvalidOperationException(
                $"Field binding {binding.FullName} must derive from Cvl or Category.");
    }
}

/// <summary>
/// Field that binds both a <see cref="Cvl"/> and a <see cref="Category"/> via type parameters.
/// Use when a field is CVL-keyed AND assigned to a non-default category — the two-arg
/// <see cref="Field{TData, TBinding}"/> overload can only express one of those at a time.
/// </summary>
public record Field<TData, TCvl, TCategory> : Field<TData>
    where TCvl : Model.Cvl
    where TCategory : Model.Category
{
    public Field([CallerFilePath] string? sourceFile = null, [CallerLineNumber] int sourceLine = 0)
        : base(sourceFile, sourceLine)
    {
        Cvl = typeof(TCvl);
        Category = typeof(TCategory);
    }
}

/// <summary>
/// Resolves the inriver <see cref="Datatype"/> for a CLR type once, then caches it as a static
/// readonly per closed generic. The lookup is built as a flat dictionary for readability.
/// </summary>
internal static class DatatypeOf<T>
{
    private static readonly Dictionary<Type, Datatype> Map = new()
    {
        [typeof(string)] = Datatype.String,
        [typeof(LocaleString)] = Datatype.LocaleString,
        [typeof(int)] = Datatype.Integer,
        [typeof(long)] = Datatype.Integer,
        [typeof(double)] = Datatype.Double,
        [typeof(decimal)] = Datatype.Double,
        [typeof(float)] = Datatype.Double,
        [typeof(bool)] = Datatype.Boolean,
        [typeof(DateTime)] = Datatype.DateTime,
        [typeof(DateTimeOffset)] = Datatype.DateTime,
        [typeof(System.Xml.Linq.XElement)] = Datatype.Xml,
        [typeof(System.Xml.XmlDocument)] = Datatype.Xml,
        [typeof(CvlKey)] = Datatype.Cvl,
        [typeof(FileRef)] = Datatype.File,
    };

    public static readonly Datatype Value =
        Map.TryGetValue(typeof(T), out var dt)
            ? dt
            : throw new NotSupportedException($"Type {typeof(T).Name} cannot be mapped to an inriver Datatype.");
}

/// <summary>Phantom type for CVL-keyed fields (the value held is a CVL key).</summary>
public readonly record struct CvlKey(string Value);

/// <summary>Phantom type for file fields.</summary>
public readonly record struct FileRef(string Value);
