using System.Text;
using IriverFieldType = inRiver.Remoting.Objects.FieldType;
using IriverUnit = inRiver.Remoting.Objects.Unit;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Model;
using ModelMeister.Model.Loading;
using ModelMeister.Model.Primitives;

namespace ModelMeister.Inriver.Mapping;

/// <summary>Bi-directional mapping between inriver field types and the code-side / snapshot representations.</summary>
public static class FieldTypeMapper
{
    /// <summary>
    /// Legacy Settings key some tooling used for a field's default expression. inriver itself stores
    /// the default — literal <em>or</em> expression — in the single <c>FieldType.DefaultValue</c>
    /// string (an expression is a <c>=</c>-prefixed value). We read this key only as a fallback.
    /// </summary>
    public const string DefaultExpressionSettingKey = "DefaultValueExpression";

    /// <summary>
    /// The code-side default collapsed to the single string inriver stores in <c>DefaultValue</c>:
    /// the rendered <c>=</c>-prefixed expression when one is set, else the literal default, else
    /// <c>null</c> (unset — read-through "leave inriver's value alone").
    /// </summary>
    public static string? CodeDefaultValue(Field ff)
        => ff.RawDefaultExpression is { } expr ? expr.RenderTopLevel()
         : ff.DefaultValue?.ToString();

    /// <summary>
    /// The live default as the single inriver string. Prefers <c>DefaultValue</c> (where inriver
    /// actually stores both literals and expressions); falls back to the legacy Settings key.
    /// </summary>
    public static string? LiveDefaultValue(LiveFieldType lf)
        => !string.IsNullOrEmpty(lf.DefaultValue) ? lf.DefaultValue
         : lf.Settings.TryGetValue(DefaultExpressionSettingKey, out var s) && !string.IsNullOrWhiteSpace(s) ? s
         : null;

    /// <summary>
    /// Compares two inriver default strings. Expressions (either side <c>=</c>-prefixed) are compared
    /// after collapsing whitespace outside single-quoted literals, so the renderer's <c>", "</c>
    /// spacing matches inriver's <c>","</c> canonical form instead of producing a phantom diff.
    /// </summary>
    public static bool DefaultValuesEqual(string? code, string? live)
    {
        var c = code ?? string.Empty;
        var l = live ?? string.Empty;
        if (LooksLikeExpression(c) || LooksLikeExpression(l))
            return NormalizeExpression(c) == NormalizeExpression(l);
        return string.Equals(c, l, StringComparison.Ordinal);
    }

    private static bool LooksLikeExpression(string s) => s.TrimStart().StartsWith('=');

    /// <summary>Drops insignificant whitespace outside single-quoted string literals.</summary>
    private static string NormalizeExpression(string expr)
    {
        var sb = new StringBuilder(expr.Length);
        var inString = false;
        foreach (var ch in expr)
        {
            if (ch == '\'') { inString = !inString; sb.Append(ch); }
            else if (!inString && char.IsWhiteSpace(ch)) { /* drop */ }
            else sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>Inriver DTO -> snapshot DTO. Empty id strings round-trip as null.</summary>
    public static LiveFieldType ToLive(IriverFieldType f)
    {
        var units = (f.Units ?? [])
            .Select(u => new LiveFieldUnit(u.Id, LocaleStringMapper.ToTp(u.Name)))
            .ToList();

        return new LiveFieldType
        {
            Id = f.Id,
            EntityTypeId = f.EntityTypeId,
            Name = LocaleStringMapper.ToTp(f.Name),
            Description = LocaleStringMapper.ToTp(f.Description),
            DataType = DatatypeMapper.FromInriver(f.DataType),
            Mandatory = f.Mandatory,
            Unique = f.Unique,
            ReadOnly = f.ReadOnly,
            Hidden = f.Hidden,
            MultiValue = f.Multivalue,
            TrackChanges = f.TrackChanges,
            IsDisplayName = f.IsDisplayName,
            IsDisplayDescription = f.IsDisplayDescription,
            ExcludeFromDefaultView = f.ExcludeFromDefaultView,
            ExpressionSupport = f.ExpressionSupport,
            Index = f.Index,
            CategoryId = string.IsNullOrEmpty(f.CategoryId) ? null : f.CategoryId,
            CvlId = string.IsNullOrEmpty(f.CVLId) ? null : f.CVLId,
            DefaultValue = f.DefaultValue,
            Settings = f.Settings is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(f.Settings, StringComparer.Ordinal),
            Units = units,
        };
    }

    /// <summary>
    /// Map a code-defined field to an inriver <see cref="IriverFieldType"/> DTO. Pass <paramref name="live"/>
    /// when emitting an Update so that values the code model left ambiguous (null nullable bools, units)
    /// are preserved from the live side rather than blanked.
    /// </summary>
    /// <remarks>
    /// <para><b>Read-through invariant.</b> For nullable code-side properties (ExcludeFromDefaultView,
    /// Index, DefaultValue, Category, CvlId), an unset code value means
    /// "leave inriver's value alone", NOT "set to default". (TrackChanges is the exception — it
    /// defaults to <c>true</c>; the code model is authoritative. See ModelLoader.) This must agree with
    /// <c>ModelDiffer.FieldDiffers</c> — if the two disagree, diff -> apply -> diff oscillates.</para>
    /// <para><b>Category id source.</b> The category id sent to inriver is looked up by CLR type
    /// via <paramref name="categoryIdByClrType"/>. A sanitized class name does not generally
    /// equal the inriver id (e.g. "My-Specs" -> class <c>MySpecs</c>); falling back to
    /// <c>Type.Name</c> would silently round-trip wrong.</para>
    /// </remarks>
    public static IriverFieldType ToInriver(
        LoadedField lf,
        LoadedEntityType owner,
        IReadOnlyDictionary<Type, string>? cvlIdByClrType = null,
        LiveFieldType? live = null,
        IReadOnlyDictionary<Type, string>? categoryIdByClrType = null)
    {
        var ff = lf.Field;

        // inriver stores the default expression in DefaultValue (see DefaultValue assignment below),
        // not in Settings, so don't carry a stale legacy key across.
        var settings = new Dictionary<string, string>(ff.Settings, StringComparer.Ordinal);
        settings.Remove(DefaultExpressionSettingKey);

        // Field.Cvl / Field.Category are read off the BASE Field property; derived ctors stamp them.
        var cvlId = ResolveCvlId(ff.Cvl, cvlIdByClrType);

        // TrackChanges is on by default — the code model is authoritative (ModelLoader stamps
        // true when unset). The `?? true` here only covers fields constructed directly (e.g. tests)
        // that bypass the loader.
        var trackChanges = ff.TrackChanges ?? true;
        // Read-through for nullable code-side bools. Falls back to inriver's value on Update,
        // then to a sensible derived default on initial Add (where `live` is null).
        var excludeFromDefaultView = ff.ExcludeFromDefaultView
            ?? live?.ExcludeFromDefaultView
            ?? false;

        // Units are entirely inriver-managed; the code DSL doesn't model them yet, so we mirror
        // whatever the live side reports rather than emit an empty list (which would clear them).
        var units = live?.Units is { Count: > 0 } liveUnits
            ? liveUnits.Select(u => new IriverUnit
            {
                Id = u.Id,
                Name = LocaleStringMapper.ToInriver(u.Name),
            }).ToList()
            : [];

        return new IriverFieldType
        {
            Id = lf.Id,
            EntityTypeId = lf.EntityTypeId,
            Name = LocaleStringMapper.ToInriver(lf.Name),
            Description = LocaleStringMapper.ToInriver(ff.Description ?? new LocaleString()),
            DataType = DatatypeMapper.ToInriver(lf.DataType),
            Mandatory = ff.Mandatory,
            Unique = ff.Unique,
            ReadOnly = ff.ReadOnly,
            Hidden = ff.Hidden,
            Multivalue = ff.MultiValue,
            TrackChanges = trackChanges,
            IsDisplayName = ff.IsDisplayName,
            IsDisplayDescription = ff.IsDisplayDescription,
            ExcludeFromDefaultView = excludeFromDefaultView,
            ExpressionSupport = ff.SupportsExpression,
            // Read-through for nullable Index / Category / DefaultValue — see the remarks block.
            Index = ff.Index ?? live?.Index ?? 0,
            CategoryId = ResolveCategoryId(ff.Category, categoryIdByClrType) ?? live?.CategoryId ?? string.Empty,
            CVLId = cvlId,
            // Literal default or rendered =expression; read-through to live when the code leaves it unset.
            DefaultValue = CodeDefaultValue(ff) ?? live?.DefaultValue,
            Settings = settings,
            Units = units,
        };
    }

    /// <summary>Resolve a code-side CVL CLR type to its inriver CvlId via the model-wide lookup.</summary>
    private static string? ResolveCvlId(Type? cvlClrType, IReadOnlyDictionary<Type, string>? cvlIdByClrType)
    {
        if (cvlClrType is null) return null;
        if (cvlIdByClrType is not null && cvlIdByClrType.TryGetValue(cvlClrType, out var id)) return id;
        return cvlClrType.Name;
    }

    /// <summary>
    /// Map a <c>Field.Category</c> CLR type to its inriver CategoryId. Prefers the model-wide
    /// lookup (built from <see cref="LoadedCategory.CategoryId"/>) so a sanitized class name doesn't
    /// silently overwrite the real id. Falls back to instantiating the type so pure code paths
    /// (tests, direct mapper use) keep working without the lookup dictionary.
    /// </summary>
    private static string? ResolveCategoryId(Type? categoryClrType, IReadOnlyDictionary<Type, string>? categoryIdByClrType)
    {
        if (categoryClrType is null) return null;
        if (categoryIdByClrType is not null && categoryIdByClrType.TryGetValue(categoryClrType, out var id)) return id;

        try
        {
            var instance = (ModelMeister.Model.Category)Activator.CreateInstance(categoryClrType)!;
            return instance.CategoryId;
        }
        catch
        {
            return categoryClrType.Name;
        }
    }
}
