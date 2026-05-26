using System.Globalization;
using System.Text;
using System.Text.Json;
using ModelMeister.Model.Primitives;

namespace ModelMeister.Scaffolder;

/// <summary>
/// Helpers shared by the per-concept emitters. Mostly responsible for rendering
/// inriver-side strings (locale dictionaries, raw text) as C# source fragments.
/// </summary>
internal static class EmitHelpers
{
    /// <summary>
    /// Renders a <see cref="JsonLocaleString"/> as a C# expression that constructs an equivalent
    /// <c>LocaleString</c>. Empty / null inputs render as <c>new LocaleString()</c>.
    /// </summary>
    /// <remarks>
    /// When every locale carries the same text, the multi-locale form collapses to a single-arg
    /// constructor. This is semantically identical on apply — <c>LocaleStringMapper.ToInriver</c>
    /// falls back to the default per locale, and <c>ModelDiffer</c> compares locale-by-locale — so
    /// it cannot produce a cosmetic diff.
    /// </remarks>
    public static string LocaleString(JsonLocaleString? ls)
    {
        if (ls is null || ls.IsEmpty()) return "new LocaleString()";

        var entries = ls.StringMap!
            .Where(kvp => !string.IsNullOrEmpty(kvp.Value))
            .ToList();
        if (entries.Count == 0) return "new LocaleString()";

        var distinctTexts = entries
            .Select(e => e.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (distinctTexts.Count == 1) return $"new LocaleString({Quote(distinctTexts[0])})";

        // First entry seeds the ctor; the rest fluent-chain via .With(lang, text).
        var head = $"new LocaleString({Quote(entries[0].Value)})";
        var tail = entries.Skip(1).Select(e => $".With({Quote(e.Key)}, {Quote(e.Value)})");
        return head + string.Concat(tail);
    }

    /// <summary>
    /// True when <paramref name="ls"/> reduces to a single distinct non-empty value that matches
    /// any of <paramref name="defaultedNames"/>.
    /// </summary>
    /// <remarks>
    /// The loader stamps a fallback <c>Name = new LocaleString(prop.Name)</c> in
    /// <c>ModelLoader</c>, and inriver fields without an explicit label echo the field id — so
    /// emitting <c>Name = new LocaleString(...)</c> for either is noise. <see cref="ModelDiffer"/>'s
    /// per-locale equality keeps both representations equivalent on apply.
    /// </remarks>
    public static bool IsRedundantNameOf(JsonLocaleString? ls, params string[] defaultedNames)
    {
        if (ls?.StringMap is null) return true;

        var distinct = ls.StringMap
            .Where(kvp => !string.IsNullOrEmpty(kvp.Value))
            .Select(kvp => kvp.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return distinct.Count switch
        {
            0 => true,
            1 => defaultedNames.Any(n => !string.IsNullOrEmpty(n) && string.Equals(distinct[0], n, StringComparison.Ordinal)),
            _ => false,
        };
    }

    /// <summary>
    /// Renders <paramref name="s"/> as a C# double-quoted string literal with the usual escapes.
    /// </summary>
    public static string Quote(string? s)
    {
        if (s is null) return "null";
        var escaped = string.Concat(s.Select(c => c switch
        {
            '"' or '\\' => "\\" + c,
            '\n' => "\\n",
            '\r' => "\\r",
            '\t' => "\\t",
            _ => c.ToString(),
        }));
        return $"\"{escaped}\"";
    }

    /// <summary>
    /// Maps an inriver field datatype to the C# type used in the <c>Field&lt;TData&gt;</c> slot.
    /// A non-empty <paramref name="cvlId"/> overrides everything to <c>CvlKey</c>.
    /// </summary>
    public static string Datatype(string raw, string? cvlId) =>
        !string.IsNullOrEmpty(cvlId) ? "CvlKey" : raw switch
        {
            "String" => "string",
            "LocaleString" => "LocaleString",
            "Integer" => "int",
            "Double" => "double",
            "Boolean" => "bool",
            "DateTime" => "DateTime",
            "Xml" => "System.Xml.Linq.XElement",
            "File" => "FileRef",
            _ => "string",
        };

    /// <summary>Maps an inriver CVL datatype string to the matching <c>CvlDataType</c> enum member.</summary>
    public static string CvlDatatype(string raw) => raw switch
    {
        "String" => "CvlDataType.String",
        "LocaleString" => "CvlDataType.LocaleString",
        "Integer" => "CvlDataType.Integer",
        "Double" => "CvlDataType.Double",
        "DateTime" => "CvlDataType.DateTime",
        _ => "CvlDataType.String",
    };

    /// <summary>Joins constructor body lines with the standard 8-space indent and newlines.</summary>
    public static string IndentBody(IEnumerable<string> lines) =>
        string.Join("\n", lines.Select(a => "        " + a));
}

/// <summary>
/// Emits a single <c>Category</c> subclass per inriver category. The class name is the sanitized
/// id; if sanitization rewrote the id (e.g. <c>My-Specs</c> → <c>MySpecs</c>) the original id is
/// preserved by assigning <c>CategoryId</c> in the constructor — that keeps the diff/apply
/// round-trip stable.
/// </summary>
public static class CategoryEmitter
{
    public static string Emit(JsonCategory c, string ns)
    {
        var sane = ProjectScaffolder.Sanitize(c.Id);
        var ctorLines = new List<string>();
        if (!sane.Equals(c.Id, StringComparison.Ordinal))
            ctorLines.Add($"CategoryId = {EmitHelpers.Quote(c.Id)};");
        if (c.Name is not null && !c.Name.IsEmpty() && !EmitHelpers.IsRedundantNameOf(c.Name, sane, c.Id, NameHumanizer.Humanize(sane)))
            ctorLines.Add($"Name = {EmitHelpers.LocaleString(c.Name)};");

        var ctor = ctorLines.Count == 0
            ? string.Empty
            : $"    public {sane}()\n    {{\n{EmitHelpers.IndentBody(ctorLines)}\n    }}\n";

        // Only pull in the Primitives using when an emitted line actually references LocaleString —
        // keeps generated files clean of unused usings under TreatWarningsAsErrors.
        var needsPrimitives = ctorLines.Any(l => l.Contains("LocaleString", StringComparison.Ordinal));
        var usings = needsPrimitives
            ? "using ModelMeister.Model;\nusing ModelMeister.Model.Primitives;\n"
            : "using ModelMeister.Model;\n";

        return $$"""
            {{usings}}
            namespace {{ns}}.Categories;

            public sealed class {{sane}} : Category
            {
            {{ctor}}    public override int Index => {{c.Index}};
            }

            """;
    }
}

/// <summary>
/// Emits one <c>Cvl</c> subclass per inriver CVL, optionally with its values inline.
/// </summary>
public static class CvlEmitter
{
    public static string Emit(JsonCvl cvl, List<JsonCvlValue> values, string ns, bool emitValues = true)
    {
        var sane = ProjectScaffolder.Sanitize(cvl.Id);
        var className = sane + "Cvl";
        var isLocaleString = string.Equals(cvl.DataType, "LocaleString", StringComparison.Ordinal);

        var valuesSrc = RenderValuesBlock(values, isLocaleString, emitValues);

        // Preserve the source id when sanitization rewrote it (round-trip stability).
        var ctor = sane.Equals(cvl.Id, StringComparison.Ordinal)
            ? string.Empty
            : $"    public {className}() {{ CvlId = {EmitHelpers.Quote(cvl.Id)}; }}\n";

        var parentLine = string.IsNullOrEmpty(cvl.ParentId)
            ? string.Empty
            : $"    public override Type? ParentCvl => typeof({ProjectScaffolder.Sanitize(cvl.ParentId!)}Cvl);\n";

        var customLine = cvl.CustomValueList
            ? "    public override bool CustomValueList => true;\n"
            : string.Empty;

        return $$"""
            using ModelMeister.Model;
            using ModelMeister.Model.Primitives;

            namespace {{ns}}.Cvls;

            public sealed class {{className}} : Cvl
            {
            {{ctor}}    public override CvlDataType DataType => {{EmitHelpers.CvlDatatype(cvl.DataType)}};
            {{parentLine}}{{customLine}}{{valuesSrc}}}

            """;
    }

    private static string RenderValuesBlock(List<JsonCvlValue> values, bool isLocaleString, bool emitValues)
    {
        // Caller opted into runtime-defined values (e.g. CvlFromFile) — inherit Cvl.GetValues().
        if (!emitValues) return string.Empty;

        if (values.Count == 0)
            return "    protected override IEnumerable<CvlValue> Values => Array.Empty<CvlValue>();\n";

        var entries = values
            .OrderBy(v => v.Index)
            .Select(v => "        new CvlValue(" + string.Join(", ", BuildCtorArgs(v, isLocaleString)) + "),");

        return "    protected override IEnumerable<CvlValue> Values => new[]\n    {\n"
             + string.Join("\n", entries)
             + "\n    };\n";
    }

    private static IEnumerable<string> BuildCtorArgs(JsonCvlValue v, bool isLocaleString)
    {
        yield return EmitHelpers.Quote(v.Key);
        yield return RenderCvlValueExpression(v.Value, isLocaleString);
        if (v.ParentKey is not null) yield return $"Parent: {EmitHelpers.Quote(v.ParentKey)}";
        if (v.Index != 0) yield return $"Index: {v.Index}";
        if (v.Deactivated) yield return "Deactivated: true";
    }

    private static string RenderCvlValueExpression(JsonElement el, bool isLocaleString)
    {
        // For non-LocaleString CVLs (String/Integer/Double/DateTime) CvlValue.Value still types as
        // LocaleString but only the default text is meaningful. Emit a bare string literal and
        // lean on LocaleString's implicit string operator so the source reads naturally.
        return isLocaleString
            ? RenderJsonLocaleString(el)
            : EmitHelpers.Quote(ExtractDefaultText(el));
    }

    private static string ExtractDefaultText(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString() ?? string.Empty,
        JsonValueKind.Object when el.TryGetProperty("StringMap", out var map) && map.ValueKind == JsonValueKind.Object
            => map.EnumerateObject()
                .Select(p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null)
                .FirstOrDefault(s => !string.IsNullOrEmpty(s)) ?? string.Empty,
        _ => string.Empty,
    };

    private static string RenderJsonLocaleString(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.String)
            return $"new LocaleString({EmitHelpers.Quote(el.GetString() ?? string.Empty)})";

        if (!el.TryGetProperty("StringMap", out var map) || map.ValueKind != JsonValueKind.Object)
            return "new LocaleString()";

        var entries = map.EnumerateObject()
            .Where(p => p.Value.ValueKind == JsonValueKind.String)
            .Select(p => (lang: p.Name, text: p.Value.GetString() ?? string.Empty))
            .ToList();

        return entries.Count switch
        {
            0 => "new LocaleString()",
            1 => $"new LocaleString({EmitHelpers.Quote(entries[0].text)})",
            _ => $"new LocaleString({EmitHelpers.Quote(entries[0].text)})"
                 + string.Concat(entries.Skip(1).Select(e => $".With({EmitHelpers.Quote(e.lang)}, {EmitHelpers.Quote(e.text)})")),
        };
    }
}

/// <summary>
/// Emits one <c>EntityType</c> subclass per inriver entity, plus optional abstract base classes
/// detected by <see cref="BaseClassDetector"/>.
/// </summary>
public static class EntityTypeEmitter
{
    /// <summary>Expression-parse warnings collected during the most recent <see cref="Emit"/> call.</summary>
    public static readonly List<string> LastEmissionWarnings = new();

    public static string EmitBase(DetectedBaseClass bc, string ns)
    {
        var members = string.Join("\n", bc.Members.Select(m =>
            $"    public Field<{EmitHelpers.Datatype(m.DataType, null)}> {m.PropertyName} {{ get; init; }} = new();"));

        return $$"""
            using ModelMeister.Model;
            using ModelMeister.Model.Primitives;

            namespace {{ns}}.EntityTypes;

            public abstract class {{bc.ClassName}} : EntityType
            {
            {{members}}
            }

            """;
    }

    public static string Emit(
        JsonEntityType e,
        string ns,
        DetectedBaseClass? baseClass,
        Dictionary<string, List<JsonCvlValue>> _,
        ISet<string>? entityTypeNames = null,
        ExpressionContext? exprContext = null,
        CompletenessAttributeIndex? completeness = null)
    {
        LastEmissionWarnings.Clear();

        var sane = ProjectScaffolder.Sanitize(e.Id);
        var ctor = BuildEntityCtor(e, sane);
        var baseName = baseClass?.ClassName ?? "EntityType";
        var inheritedMembers = baseClass?.Members
            .Select(m => m.PropertyName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>();
        var ctx = exprContext ?? ExpressionContext.Empty;

        // Group fields by category and emit a "// CategoryName" separator comment between groups.
        // Index order is preserved within each group; uncategorized fields lead. The category id
        // (not display name — display name lookup isn't in scope here) labels each group.
        var visibleFields = (e.FieldTypes ?? new())
            .Select(f => (Field: f, PropName: PropertyNameFor(f, e.Id)))
            .Where(x => !inheritedMembers.Contains(x.PropName))
            .ToList();

        var groups = visibleFields
            .GroupBy(x => x.Field.CategoryId ?? string.Empty)
            .OrderBy(g => string.IsNullOrEmpty(g.Key) ? 0 : 1) // uncategorized first
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Completeness attributes pull in extra usings — only when a field on this type carries one.
        var usedCompleteness = false;
        var usedLinkTypes = false;
        if (completeness is not null)
            foreach (var x in visibleFields)
            {
                var fa = completeness.For(e.Id, x.Field.Id);
                if (fa.Attributes.Count > 0) usedCompleteness = true;
                if (fa.UsesLinkType) usedLinkTypes = true;
            }

        var sb = new StringBuilder();
        var first = true;
        foreach (var group in groups)
        {
            if (!string.IsNullOrEmpty(group.Key))
            {
                if (!first) sb.AppendLine();
                sb.AppendLine("    //");
                sb.AppendLine($"    // {group.Key}");
                sb.AppendLine("    //");
            }
            foreach (var x in group)
                sb.Append(EmitField(e, x.Field, x.PropName, ns, entityTypeNames, ctx, completeness));
            first = false;
        }
        var fieldsBlock = sb.ToString();

        var usings = new List<string>
        {
            "using ModelMeister.Model;",
            "using ModelMeister.Model.Expressions;",
            "using ModelMeister.Model.Primitives;",
            $"using {ns}.Categories;",
            $"using {ns}.Cvls;",
        };
        if (usedCompleteness)
        {
            usings.Add("using ModelMeister.Model.Completeness;");
            usings.Add($"using {ns}.CompletenessGroups;");
        }
        if (usedLinkTypes)
            usings.Add($"using {ns}.LinkTypes;");
        var usingsBlock = string.Join("\n", usings);

        return $$"""
            {{usingsBlock}}

            namespace {{ns}}.EntityTypes;

            public sealed class {{sane}} : {{baseName}}
            {
            {{ctor}}{{fieldsBlock}}}

            """;
    }

    private static string BuildEntityCtor(JsonEntityType e, string sane)
    {
        var lines = new List<string>();
        if (!sane.Equals(e.Id, StringComparison.Ordinal))
            lines.Add($"EntityTypeId = {EmitHelpers.Quote(e.Id)};");
        // EntityTypeName defaults to new LocaleString(NameHumanizer.Humanize(EntityTypeId)) in the
        // EntityType base ctor — skip when it would only re-state the (humanized) default.
        if (e.Name is not null && !e.Name.IsEmpty() && !EmitHelpers.IsRedundantNameOf(e.Name, e.Id, sane, NameHumanizer.Humanize(sane)))
            lines.Add($"EntityTypeName = {EmitHelpers.LocaleString(e.Name)};");

        return lines.Count == 0
            ? string.Empty
            : $"    public {sane}()\n    {{\n{EmitHelpers.IndentBody(lines)}\n    }}\n";
    }

    private static string PropertyNameFor(JsonFieldType f, string entityId) =>
        f.Id.StartsWith(entityId, StringComparison.OrdinalIgnoreCase)
            ? f.Id.Substring(entityId.Length)
            : f.Id;

    private static string EmitField(
        JsonEntityType e,
        JsonFieldType f,
        string propName,
        string ns,
        ISet<string>? entityTypeNames,
        ExpressionContext ctx,
        CompletenessAttributeIndex? completeness = null)
    {
        var sanePropName = ProjectScaffolder.Sanitize(propName);
        if (sanePropName.Length == 0) sanePropName = "Field" + f.Id;

        var dt = EmitHelpers.Datatype(f.DataType, f.CvlId);
        var typeParams = BuildTypeParams(f, dt, ns, entityTypeNames);
        var inits = BuildFieldInits(e, f, sanePropName, ctx).ToList();
        var initText = inits.Count == 0 ? string.Empty : " { " + string.Join(", ", inits) + " }";

        // Flag attributes are emitted as a single comma-separated row so the eye scans one short
        // line per field — see [Mandatory, Unique, Index(1)] form. Order matches the property's
        // visual importance: data-shape flags first, behavior next, scalars last, display markers
        // dead last (they're meta-information, not data).
        var attrs = BuildFieldFlagAttributes(f).ToList();
        if (completeness is not null)
            attrs.AddRange(completeness.For(e.Id, f.Id).Attributes);
        if (f.IsDisplayName) attrs.Add("DisplayName");
        if (f.IsDisplayDescription) attrs.Add("DisplayDescription");
        var attrText = attrs.Count == 0 ? string.Empty : $"    [{string.Join(", ", attrs)}]\n";

        return $"{attrText}    public Field<{typeParams}> {sanePropName} {{ get; init; }} = new(){initText};\n";
    }

    /// <summary>
    /// Flag attributes — one per non-default boolean field option. Mirrors the set of flags the
    /// previous emitter wrote as <c>Mandatory = true</c> object-initializer entries, lifted into
    /// attribute form so each field reads as a single visual row. <c>Index</c> and
    /// <c>ExcludeFromDefaultView</c> stay unset to preserve the "leave inriver's value alone"
    /// read-through semantics (see CLAUDE.md). <c>TrackChanges</c> defaults to <c>true</c>, so it
    /// is emitted (as an object-initializer <c>TrackChanges = false</c>) only when the source has
    /// it off — see <see cref="BuildFieldInits"/>. <c>CategoryId</c> and fieldsets ride in the
    /// <see cref="Field{TData, TBinding}"/> type-parameter slots, not here.
    /// </summary>
    private static IEnumerable<string> BuildFieldFlagAttributes(JsonFieldType f)
    {
        if (f.Mandatory) yield return "Mandatory";
        if (f.Unique) yield return "Unique";
        if (f.ReadOnly) yield return "ReadOnlyField";
        if (f.Hidden) yield return "Hidden";
        if (f.Multivalue) yield return "MultiValue";
        if (f.ExpressionSupport) yield return "SupportsExpression";
    }

    private static string BuildTypeParams(JsonFieldType f, string dt, string ns, ISet<string>? entityTypeNames)
    {
        // Bindings (CVL / Category) ride in the type-parameter slots so the source reads as
        // `Field<DateTime, Icecat>` instead of `Field<DateTime> { Category = typeof(...) }`.
        var hasCvl = !string.IsNullOrEmpty(f.CvlId);
        var hasCategory = !string.IsNullOrEmpty(f.CategoryId);

        var cvlRef = hasCvl ? $"{ProjectScaffolder.Sanitize(f.CvlId!)}Cvl" : null;
        var categoryRef = hasCategory ? CategoryRef(f.CategoryId!, ns, entityTypeNames) : null;

        return (hasCvl, hasCategory) switch
        {
            (true, true) => $"{dt}, {cvlRef}, {categoryRef}",
            (true, false) => $"{dt}, {cvlRef}",
            (false, true) => $"{dt}, {categoryRef}",
            _ => dt,
        };
    }

    private static string CategoryRef(string categoryId, string ns, ISet<string>? entityTypeNames)
    {
        // When a category shares a sanitized name with an entity type (e.g. inriver has both an
        // "ETIM" entity type and an "ETIM" category), `using {ns}.Categories;` does not
        // disambiguate — the namespace-local entity type wins. Fully qualify in that case.
        var sane = ProjectScaffolder.Sanitize(categoryId);
        return entityTypeNames is not null && entityTypeNames.Contains(sane)
            ? $"global::{ns}.Categories.{sane}"
            : sane;
    }

    private static IEnumerable<string> BuildFieldInits(JsonEntityType e, JsonFieldType f, string sanePropName, ExpressionContext ctx)
    {
        // The base Field<T> ctor stamps Id = entityId + propertyName. Only emit Id when the source
        // id deviates from that convention (sanitization, irregular naming, etc.).
        if (!string.Equals(f.Id, e.Id + sanePropName, StringComparison.Ordinal))
            yield return $"Id = {EmitHelpers.Quote(f.Id)}";

        // Mandatory, Unique, ReadOnly, Hidden, MultiValue, SupportsExpression, Index, IsDisplayName,
        // IsDisplayDescription are emitted as attributes on the property — see EmitField /
        // BuildFieldFlagAttributes. The object initializer is reserved for complex values
        // (LocaleStrings, expressions, the explicit DefaultValue) that don't fit in attribute literals.

        if (f.Name is not null && !f.Name.IsEmpty() && !EmitHelpers.IsRedundantNameOf(f.Name, sanePropName, f.Id, NameHumanizer.Humanize(sanePropName)))
            yield return $"Name = {EmitHelpers.LocaleString(f.Name)}";

        if (f.Description is not null && !f.Description.IsEmpty() && !EmitHelpers.IsRedundantNameOf(f.Description, sanePropName, f.Id))
            yield return $"Description = {EmitHelpers.LocaleString(f.Description)}";

        // TrackChanges defaults to true (the code model is authoritative). Only pin it when the
        // source has it off, otherwise the default already covers it.
        if (!f.TrackChanges)
            yield return "TrackChanges = false";

        // Default value / default expression. inriver's JSON export stores expression text in the
        // top-level `DefaultValue` (leading `=`), not under Settings — older builds also put it in
        // Settings["DefaultValueExpression"], so check both for safety.
        var exprText = f.Settings is not null
            && f.Settings.TryGetValue("DefaultValueExpression", out var settingsExpr)
            && !string.IsNullOrWhiteSpace(settingsExpr)
                ? settingsExpr
                : !string.IsNullOrEmpty(f.DefaultValue) && f.DefaultValue.TrimStart().StartsWith("=")
                    ? f.DefaultValue
                    : null;

        if (exprText is not null)
        {
            var (cs, warns) = ExpressionParser.ParseTopLevel(exprText, ctx);
            foreach (var w in warns) LastEmissionWarnings.Add($"{f.Id}: {w}");
            yield return $"DefaultExpression = {cs}";
        }
        else if (!string.IsNullOrEmpty(f.DefaultValue))
        {
            yield return $"DefaultValue = {EmitDefaultValueLiteral(f.DataType, f.DefaultValue)}";
        }
    }

    private static string EmitDefaultValueLiteral(string dataType, string raw) => dataType switch
    {
        "Integer" => int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var i)
            ? i.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : EmitHelpers.Quote(raw),
        "Double" => double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)
            ? d.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
            : EmitHelpers.Quote(raw),
        "Boolean" => bool.TryParse(raw, out var b) ? (b ? "true" : "false") : EmitHelpers.Quote(raw),
        "DateTime" => $"System.DateTime.Parse({EmitHelpers.Quote(raw)}, System.Globalization.CultureInfo.InvariantCulture)",
        "CVL" => $"new CvlKey({EmitHelpers.Quote(raw)})",
        "LocaleString" => $"new LocaleString({EmitHelpers.Quote(raw)})",
        _ => EmitHelpers.Quote(raw),
    };
}

/// <summary>Emits a <c>Fieldset</c> subclass per inriver field set.</summary>
public static class FieldsetEmitter
{
    public static string Emit(JsonFieldSet f, string ns)
    {
        var sane = ProjectScaffolder.Sanitize(f.Id);
        var className = sane + "Fieldset";

        var ctorLines = new List<string>();
        if (!sane.Equals(f.Id, StringComparison.Ordinal))
            ctorLines.Add($"FieldsetId = {EmitHelpers.Quote(f.Id)};");
        ctorLines.Add($"Name = {EmitHelpers.LocaleString(f.Name)};");
        ctorLines.Add($"Description = {EmitHelpers.LocaleString(f.Description)};");

        var ctor = $"    public {className}()\n    {{\n{EmitHelpers.IndentBody(ctorLines)}\n    }}\n";
        var entityRef = ProjectScaffolder.Sanitize(f.EntityTypeId);

        return $$"""
            using ModelMeister.Model;
            using ModelMeister.Model.Primitives;
            using {{ns}}.EntityTypes;

            namespace {{ns}}.Fieldsets;

            public sealed class {{className}} : Fieldset
            {
            {{ctor}}    public override Type EntityType => typeof({{entityRef}});
            }

            """;
    }
}

/// <summary>
/// Emits all link types into a single file. Handles BCL identifier shadowing via using-alias
/// directives when an entity type's simple name collides with an implicit-using-imported type.
/// </summary>
public static class LinkTypeEmitter
{
    // Common BCL types brought in by Microsoft.NET.Sdk's implicit usings. An entity type with the
    // same simple name would otherwise produce a CS0104 ambiguity when written bare in a generic
    // argument list (e.g. `LinkType<Foo, Task>` against `System.Threading.Tasks.Task`). For any
    // colliding entity we emit a using-alias which wins over both the explicit namespace using
    // and the implicit using.
    private static readonly HashSet<string> BclShadowNames = new(StringComparer.Ordinal)
    {
        "Task", "File", "Path", "Environment", "Convert", "Console", "Action", "Encoding",
        "Type", "Math", "Tuple", "Exception", "Stream", "Random", "Object", "String",
        "Func", "Predicate", "EventArgs", "EventHandler", "Uri", "Version", "TimeSpan",
        "DateTime", "DateTimeOffset", "Guid", "Index", "Range", "Span", "Memory", "Buffer",
        "Array", "Comparer", "Activator", "Attribute",
    };

    public static string EmitAll(List<JsonLinkType> linkTypes, string ns)
    {
        // SourceName is the label inriver shows on the source for the link toward the target,
        // which defaults to the target's type name — so SourceName is redundant when it matches
        // the target. TargetName is the mirror case.
        bool SourceNameRedundant(JsonLinkType l) =>
            l.SourceName is null || l.SourceName.IsEmpty()
            || EmitHelpers.IsRedundantNameOf(l.SourceName, ProjectScaffolder.Sanitize(l.TargetEntityTypeId), l.TargetEntityTypeId);

        bool TargetNameRedundant(JsonLinkType l) =>
            l.TargetName is null || l.TargetName.IsEmpty()
            || EmitHelpers.IsRedundantNameOf(l.TargetName, ProjectScaffolder.Sanitize(l.SourceEntityTypeId), l.SourceEntityTypeId);

        var hasLocaleString = linkTypes.Any(l => !SourceNameRedundant(l) || !TargetNameRedundant(l));

        var collidingEntityNames = linkTypes
            .SelectMany(l => new[] { ProjectScaffolder.Sanitize(l.SourceEntityTypeId), ProjectScaffolder.Sanitize(l.TargetEntityTypeId) })
            .Where(BclShadowNames.Contains)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        var usings = new List<string> { "using ModelMeister.Model;" };
        if (hasLocaleString) usings.Add("using ModelMeister.Model.Primitives;");
        usings.Add($"using {ns}.EntityTypes;");
        usings.AddRange(collidingEntityNames.Select(n => $"using {n} = {ns}.EntityTypes.{n};"));

        var body = string.Join("\n", linkTypes.Select(l => EmitOne(l, SourceNameRedundant(l), TargetNameRedundant(l))));

        return string.Join("\n", usings) + $"\n\nnamespace {ns}.LinkTypes;\n\n" + body;
    }

    private static string EmitOne(JsonLinkType l, bool sourceRedundant, bool targetRedundant)
    {
        var sane = ProjectScaffolder.Sanitize(l.Id);
        var source = ProjectScaffolder.Sanitize(l.SourceEntityTypeId);
        var target = ProjectScaffolder.Sanitize(l.TargetEntityTypeId);

        var ctorLines = new List<string>();
        if (!sane.Equals(l.Id, StringComparison.Ordinal))
            ctorLines.Add($"LinkTypeId = {EmitHelpers.Quote(l.Id)};");
        if (!sourceRedundant)
            ctorLines.Add($"SourceName = {EmitHelpers.LocaleString(l.SourceName)};");
        if (!targetRedundant)
            ctorLines.Add($"TargetName = {EmitHelpers.LocaleString(l.TargetName)};");

        var indexLine = l.Index == 0 ? string.Empty : $"    public override int Index => {l.Index};\n";

        // Empty class body collapses to the brace-pair form for compactness.
        if (ctorLines.Count == 0 && indexLine.Length == 0)
            return $"public sealed class {sane} : LinkType<{source}, {target}> {{ }}\n";

        var ctor = ctorLines.Count == 0
            ? string.Empty
            : $"    public {sane}()\n    {{\n{EmitHelpers.IndentBody(ctorLines)}\n    }}\n";

        return $"public sealed class {sane} : LinkType<{source}, {target}>\n{{\n{ctor}{indexLine}}}\n";
    }
}

/// <summary>Emits <c>Role</c> subclasses plus, when needed, a <c>CustomPermissions.cs</c> file.</summary>
public static class RoleEmitter
{
    public static string Emit(JsonRole r, string ns, IDictionary<string, string> standardPermissionsByLowerName)
    {
        var sane = ProjectScaffolder.Sanitize(r.Name);
        var className = sane + "Role";

        var permTypes = (r.Permissions ?? new())
            .Select(p => p.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => standardPermissionsByLowerName.TryGetValue(n, out var canonical)
                ? $"typeof(StandardPermissions.{canonical})"
                : $"typeof({ProjectScaffolder.Sanitize(n)})")
            .ToList();

        var permArr = permTypes.Count == 0
            ? "Array.Empty<Type>()"
            : "new[] { " + string.Join(", ", permTypes) + " }";

        return $$"""
            using ModelMeister.Model.Security;

            namespace {{ns}}.Roles;

            public sealed class {{className}} : Role
            {
                public {{className}}()
                {
                    Name = {{EmitHelpers.Quote(r.Name)}};
                    Description = {{EmitHelpers.Quote(r.Description ?? string.Empty)}};
                }
                public override IReadOnlyList<Type> Permissions => {{permArr}};
            }

            """;
    }

    public static string EmitCustomPermissions(IEnumerable<string> names, string ns)
    {
        var classes = names
            .Select(n => (Original: n, Sane: ProjectScaffolder.Sanitize(n)))
            .Where(x => x.Sane.Length > 0)
            .Select(x =>
            {
                // Preserve the original permission name if sanitization changed it (rare for
                // inriver perms, which are usually valid C# identifiers — but the loader compares
                // by Name, not type).
                var ctor = x.Sane.Equals(x.Original, StringComparison.Ordinal)
                    ? string.Empty
                    : $"    public {x.Sane}() {{ Name = {EmitHelpers.Quote(x.Original)}; Description = {EmitHelpers.Quote(x.Original)}; }}\n";
                return $"public sealed class {x.Sane} : Permission\n{{\n{ctor}}}\n";
            });

        return $"using ModelMeister.Model.Security;\n\nnamespace {ns}.Roles;\n\n"
             + string.Join("\n", classes)
             + "\n";
    }
}

/// <summary>Emits a static <c>Languages</c> class listing the configured languages in order.</summary>
public static class CompletenessGroupEmitter
{
    public static string Emit(CompletenessAttributeIndex.GroupDecl g, string ns)
    {
        var ctorLines = new List<string>();
        if (g.Name is not null && !g.Name.IsEmpty()
            && !EmitHelpers.IsRedundantNameOf(g.Name, g.ClassName, NameHumanizer.Humanize(g.ClassName)))
            ctorLines.Add($"Name = {EmitHelpers.LocaleString(g.Name)};");

        var usings = "using ModelMeister.Model.Completeness;\n";
        if (ctorLines.Count > 0) usings += "using ModelMeister.Model.Primitives;\n";

        var ctor = ctorLines.Count == 0
            ? string.Empty
            : $"    public {g.ClassName}()\n    {{\n{EmitHelpers.IndentBody(ctorLines)}\n    }}\n";
        var weightLine = g.Weight == 0 ? string.Empty : $"    public override int Weight => {g.Weight};\n";
        var sortLine = g.SortOrder == 0 ? string.Empty : $"    public override int SortOrder => {g.SortOrder};\n";
        var body = ctor + weightLine + sortLine;

        return $$"""
            {{usings}}
            namespace {{ns}}.CompletenessGroups;

            public sealed class {{g.ClassName}} : CompletenessGroup
            {
            {{body}}}

            """;
    }
}

public static class LanguagesEmitter
{
    public static string Emit(List<JsonLanguage> langs, string ns)
    {
        // First language in the list is the master/default — flag it for the Language ctor.
        var entries = langs.Select((l, i) =>
        {
            var defaultArg = i == 0 ? ", IsDefault: true" : string.Empty;
            return $"        new({EmitHelpers.Quote(l.Name)}{defaultArg}),";
        });

        return $$"""
            using ModelMeister.Model;

            namespace {{ns}};

            public static class Languages
            {
                public static readonly Language[] All =
                {
            {{string.Join("\n", entries)}}
                };
            }

            """;
    }
}

/// <summary>
/// Emits the scaffolded project's <c>.csproj</c>. The Model assembly is referenced via a
/// <c>HintPath</c> into a sibling <c>lib\</c> directory which the scaffolder populates — that way
/// the project builds standalone regardless of how it was scaffolded.
/// </summary>
public static class CsprojEmitter
{
    public static string Emit(string ns) => $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net9.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <RootNamespace>{ns}</RootNamespace>
          </PropertyGroup>
          <ItemGroup>
            <Reference Include="ModelMeister.Model">
              <HintPath>lib\ModelMeister.Model.dll</HintPath>
            </Reference>
          </ItemGroup>
          <ItemGroup>
            <PackageReference Include="ClosedXML" Version="0.104.1" />
          </ItemGroup>
        </Project>

        """;
}
