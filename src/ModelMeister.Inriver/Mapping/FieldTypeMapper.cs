using IriverFieldType = inRiver.Remoting.Objects.FieldType;
using IriverUnit = inRiver.Remoting.Objects.Unit;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Model.Loading;
using ModelMeister.Model.Primitives;

namespace ModelMeister.Inriver.Mapping;

/// <summary>Bi-directional mapping between inriver field types and the code-side / snapshot representations.</summary>
public static class FieldTypeMapper
{
    /// <summary>Inriver Settings key under which a field's default expression text is stored.</summary>
    public const string DefaultExpressionSettingKey = "DefaultValueExpression";

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

        var settings = new Dictionary<string, string>(ff.Settings, StringComparer.Ordinal);
        if (ff.RawDefaultExpression is not null)
        {
            settings[DefaultExpressionSettingKey] = ff.RawDefaultExpression.RenderTopLevel();
        }

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
            DefaultValue = ff.DefaultValue?.ToString() ?? live?.DefaultValue,
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
