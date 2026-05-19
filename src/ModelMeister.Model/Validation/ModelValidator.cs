using ModelMeister.Model.Completeness;
using ModelMeister.Model.Expressions;
using ModelMeister.Model.Loading;
using ModelMeister.Model.Primitives;

namespace ModelMeister.Model.Validation;

/// <summary>Severity of a <see cref="ValidationIssue"/>.</summary>
public enum Severity { Error, Warning }

/// <summary>A single issue produced by <see cref="ModelValidator.Validate"/>.</summary>
public sealed record ValidationIssue(Severity Severity, string Code, string Message, string? Source = null)
{
    public override string ToString() =>
        string.IsNullOrEmpty(Source) ? $"{Code}: {Message}" : $"{Code}: {Message} (at {Source})";
}

/// <summary>Accumulator for issues raised during validation.</summary>
public sealed class ValidationResult
{
    public List<ValidationIssue> Issues { get; } = [];

    /// <summary>True when at least one <see cref="Severity.Error"/> issue has been added.</summary>
    public bool HasErrors => Issues.Any(i => i.Severity == Severity.Error);

    public void Error(string code, string message, string? source = null) =>
        Issues.Add(new ValidationIssue(Severity.Error, code, message, source));

    public void Warn(string code, string message, string? source = null) =>
        Issues.Add(new ValidationIssue(Severity.Warning, code, message, source));

    internal void ErrorOnField(string code, string message, LoadedField field) =>
        Error(code, message, field.SourceLocation);
}

/// <summary>
/// Runs a fixed battery of structural checks over a <see cref="LoadedModel"/>. See
/// <c>docs/validation-codes.md</c> for the authoritative list of MMxxx codes, triggers and fixes.
/// </summary>
public static class ModelValidator
{
    private static readonly HashSet<string> ReservedSystemFieldIdsImpl = new(StringComparer.Ordinal)
    {
        "Created", "Modified", "CreatedBy", "ModifiedBy", "Locked", "LockedBy",
    };

    /// <summary>
    /// Property names a model author cannot use for a Field — they collide with inriver's built-in
    /// system fields. Exposed so consumers (CLI, IDE tooling) can enumerate the list.
    /// </summary>
    public static IReadOnlyCollection<string> ReservedSystemFieldIds => ReservedSystemFieldIdsImpl;

    private static readonly HashSet<Datatype> ExpressionSupportedTypes =
    [
        Datatype.Integer, Datatype.Double, Datatype.Boolean, Datatype.String,
        Datatype.Cvl, Datatype.LocaleString, Datatype.DateTime,
    ];

    /// <summary>Runs every check against <paramref name="model"/>, returning a combined result.</summary>
    public static ValidationResult Validate(LoadedModel model)
    {
        var r = new ValidationResult();

        CheckUniqueIds(r, model);
        CheckDuplicateFlagSpecifications(r, model);
        CheckDisplayNamesUnique(r, model);
        CheckCvlReferences(r, model);
        CheckCvlDataTypeCompatibility(r, model);
        CheckLinkReferences(r, model);
        CheckFieldsetReferences(r, model);
        CheckCompletenessWeights(r, model);
        CheckLanguages(r, model);
        CheckSpecificationTemplateLimits(r, model);
        CheckReservedIds(r, model);
        CheckPerMarketResolver(r, model);
        CheckExpressions(r, model);

        return r;
    }

    /// <summary>Returns the (entity, field) pairs of every loaded field for use in foreach pipelines.</summary>
    private static IEnumerable<(LoadedEntityType Entity, LoadedField Field)> AllFields(LoadedModel m) =>
        m.EntityTypes.SelectMany(e => e.Fields.Select(f => (e, f)));

    private static void CheckUniqueIds(ValidationResult r, LoadedModel m)
    {
        Duplicates(r, "MM001", "EntityType", m.EntityTypes.Select(e => e.EntityTypeId));
        Duplicates(r, "MM002", "CVL", m.Cvls.Select(c => c.CvlId));
        Duplicates(r, "MM003", "Category", m.Categories.Select(c => c.CategoryId));
        Duplicates(r, "MM004", "Fieldset", m.Fieldsets.Select(f => f.FieldsetId));
        Duplicates(r, "MM005", "LinkType", m.LinkTypes.Select(l => l.LinkTypeId));
        Duplicates(r, "MM006", "Role", m.Roles.Select(ro => ro.Name));

        foreach (var et in m.EntityTypes)
        {
            var duplicates = et.Fields
                .GroupBy(f => f.Id, StringComparer.Ordinal)
                .Where(g => g.Count() > 1);

            foreach (var d in duplicates)
                r.Error("MM007", $"Duplicate field ID '{d.Key}' on entity type '{et.EntityTypeId}'.");
        }
    }

    private static void Duplicates(ValidationResult r, string code, string what, IEnumerable<string> ids)
    {
        var dups = ids.GroupBy(x => x, StringComparer.Ordinal).Where(g => g.Count() > 1);
        foreach (var d in dups)
            r.Error(code, $"Duplicate {what} ID '{d.Key}'.");
    }

    /// <summary>
    /// MM012: a field flag was specified by BOTH an attribute (e.g. <c>[Mandatory]</c>) AND the
    /// object initializer (<c>= new() { Mandatory = true }</c>). The attribute wins at runtime so
    /// this isn't a correctness bug, but it's redundant and confusing — pick one form.
    /// </summary>
    private static void CheckDuplicateFlagSpecifications(ValidationResult r, LoadedModel m)
    {
        foreach (var (_, f) in AllFields(m))
        {
            foreach (var prop in f.DuplicateAttributeFlags)
                r.ErrorOnField("MM012",
                    $"Field '{f.Id}' specifies '{prop}' via both an attribute and the object initializer — pick one form.", f);
        }
    }

    private static void CheckDisplayNamesUnique(ValidationResult r, LoadedModel m)
    {
        foreach (var et in m.EntityTypes)
        {
            var displayName = et.Fields.Count(f => f.Field.IsDisplayName);
            var displayDesc = et.Fields.Count(f => f.Field.IsDisplayDescription);
            if (displayName > 1)
                r.Error("MM010", $"Entity type '{et.EntityTypeId}' has {displayName} fields marked IsDisplayName.");
            if (displayDesc > 1)
                r.Error("MM011", $"Entity type '{et.EntityTypeId}' has {displayDesc} fields marked IsDisplayDescription.");
        }
    }

    private static void CheckCvlReferences(ValidationResult r, LoadedModel m)
    {
        var cvlByClr = m.Cvls.ToDictionary(c => c.ClrType);

        foreach (var (_, f) in AllFields(m))
        {
            if (f.Field.Cvl is { } cvlType && !cvlByClr.ContainsKey(cvlType))
                r.ErrorOnField("MM020", $"Field '{f.Id}' references unknown CVL type '{cvlType.FullName}'.", f);
        }
    }

    /// <summary>
    /// MM024: <c>Field&lt;TData, TCvl&gt;</c> must have a TData whose CLR data type maps to the CVL's
    /// <see cref="CvlDataType"/>. Detects e.g. <c>Field&lt;double, ColourCvl&gt;</c> where ColourCvl is
    /// <c>CvlDataType.String</c> — apply would silently coerce values, this surfaces the mismatch.
    /// </summary>
    private static void CheckCvlDataTypeCompatibility(ValidationResult r, LoadedModel m)
    {
        var cvlByClr = m.Cvls.ToDictionary(c => c.ClrType);

        foreach (var (_, f) in AllFields(m))
        {
            if (f.Field.Cvl is not { } cvlClrType) continue;
            if (!cvlByClr.TryGetValue(cvlClrType, out var cvl)) continue;

            var fieldClrType = f.Field.GetType();
            if (!fieldClrType.IsGenericType) continue;

            var tData = fieldClrType.GetGenericArguments()[0];
            if (tData == typeof(CvlKey)) continue;

            if (!IsCompatibleWithCvlDataType(tData, cvl.DataType))
            {
                r.ErrorOnField("MM024",
                    $"Field '{f.Id}' is Field<{tData.Name}, {cvlClrType.Name}> but CVL '{cvl.CvlId}' has DataType={cvl.DataType}; " +
                    $"use Field<CvlKey, {cvlClrType.Name}> or pick a TData whose Datatype matches.", f);
            }
        }
    }

    private static bool IsCompatibleWithCvlDataType(Type tData, CvlDataType cvlDataType) =>
        cvlDataType switch
        {
            CvlDataType.String => tData == typeof(string) || tData == typeof(LocaleString),
            CvlDataType.LocaleString => tData == typeof(LocaleString) || tData == typeof(string),
            CvlDataType.Integer => tData == typeof(int) || tData == typeof(long),
            CvlDataType.Double => tData == typeof(double) || tData == typeof(decimal) || tData == typeof(float),
            CvlDataType.DateTime => tData == typeof(DateTime) || tData == typeof(DateTimeOffset),
            _ => true,
        };

    /// <summary>
    /// MM075: any field using <c>PerMarket = true</c> requires a markets resolver — either an
    /// <see cref="Markets.IMarketResolver"/> implementation OR a concrete <see cref="Markets.MarketsCvl"/>
    /// subclass — otherwise the model loader has nothing to fan out against.
    /// </summary>
    private static void CheckPerMarketResolver(ValidationResult r, LoadedModel m)
    {
        var perMarketFields = AllFields(m).Where(t => t.Field.Field.PerMarket).ToList();
        if (perMarketFields.Count == 0) return;

        var hasMarketsCvl = m.Cvls.Any(c => typeof(Markets.MarketsCvl).IsAssignableFrom(c.ClrType));
        if (hasMarketsCvl) return;

        if (HasCustomMarketResolver()) return;

        foreach (var (et, f) in perMarketFields)
        {
            r.ErrorOnField("MM075",
                $"Field '{f.Id}' on '{et.EntityTypeId}' uses PerMarket=true but no MarketsCvl or IMarketResolver " +
                "is registered in the model assembly.", f);
        }
    }

    private static bool HasCustomMarketResolver() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(SafeGetTypes)
            .Any(t => !t.IsAbstract && !t.IsInterface
                && typeof(Markets.IMarketResolver).IsAssignableFrom(t)
                && t != typeof(Markets.CvlMarketResolver));

    private static IEnumerable<Type> SafeGetTypes(System.Reflection.Assembly a)
    {
        try { return a.GetTypes(); }
        catch { return []; }
    }

    private static void CheckLinkReferences(ValidationResult r, LoadedModel m)
    {
        var entityIds = m.EntityTypes.Select(e => e.EntityTypeId).ToHashSet(StringComparer.Ordinal);

        foreach (var l in m.LinkTypes)
        {
            if (!entityIds.Contains(l.SourceEntityTypeId))
                r.Error("MM030", $"LinkType '{l.LinkTypeId}' Source '{l.SourceEntityTypeId}' is not a registered entity type.");
            if (!entityIds.Contains(l.TargetEntityTypeId))
                r.Error("MM031", $"LinkType '{l.LinkTypeId}' Target '{l.TargetEntityTypeId}' is not a registered entity type.");
            if (l.LinkEntityTypeId is { } le && !entityIds.Contains(le))
                r.Error("MM032", $"LinkType '{l.LinkTypeId}' LinkEntityType '{le}' is not a registered entity type.");
        }
    }

    private static void CheckFieldsetReferences(ValidationResult r, LoadedModel m)
    {
        var fsByClr = m.Fieldsets.ToDictionary(f => f.ClrType);

        foreach (var (et, f) in AllFields(m))
        {
            foreach (var fsClr in f.Field.Fieldsets)
            {
                if (!fsByClr.TryGetValue(fsClr, out var fs))
                {
                    r.ErrorOnField("MM040", $"Field '{f.Id}' references unknown Fieldset type '{fsClr.FullName}'.", f);
                    continue;
                }
                if (!string.Equals(fs.EntityTypeId, et.EntityTypeId, StringComparison.Ordinal))
                {
                    r.ErrorOnField("MM041",
                        $"Field '{f.Id}' on entity '{et.EntityTypeId}' references Fieldset '{fs.FieldsetId}' which belongs to '{fs.EntityTypeId}'.",
                        f);
                }
            }
        }
    }

    private static void CheckCompletenessWeights(ValidationResult r, LoadedModel m)
    {
        var groupByClr = m.CompletenessGroups.ToDictionary(g => g.ClrType);

        // Collect every (entity, group) -> list of (fieldId, weight) contributors via a flat LINQ pipeline.
        var contributionsByKey = AllFields(m)
            .SelectMany(t => t.Field.Attributes.OfType<CompletenessRuleAttribute>()
                .Select(rule => (Entity: t.Entity.EntityTypeId, Field: t.Field, Rule: rule)))
            .GroupBy(x => (x.Entity, x.Rule.Group))
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var ((entity, group), contributions) in contributionsByKey)
        {
            if (!groupByClr.ContainsKey(group))
            {
                foreach (var c in contributions)
                    r.ErrorOnField("MM050",
                        $"Field '{c.Field.Id}' references unknown CompletenessGroup '{group.FullName}'.", c.Field);
                continue;
            }

            var sum = contributions.Sum(c => c.Rule.Weight);
            if (sum == 100) continue;

            var detail = " Contributions: " +
                         string.Join(", ", contributions.Select(c => $"{c.Field.Id}={c.Rule.Weight}"))
                         + ".";
            r.Error("MM051",
                $"Completeness weights on entity '{entity}' for group '{group.Name}' sum to {sum}, must be 100.{detail}");
        }
    }

    private static void CheckLanguages(ValidationResult r, LoadedModel m)
    {
        if (m.Languages.Count == 0)
        {
            r.Warn("MM060", "No languages declared in the model.");
            return;
        }
        if (!m.Languages.Any(l => l.IsDefault))
            r.Error("MM061", "No language is marked IsDefault.");

        var dups = m.Languages
            .GroupBy(l => l.IsoCode, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        foreach (var d in dups)
            r.Error("MM062", $"Duplicate language ISO code '{d.Key}'.");
    }

    private static void CheckSpecificationTemplateLimits(ValidationResult r, LoadedModel m)
    {
        var entityByClr = m.EntityTypes.ToDictionary(e => e.ClrType);
        var cvlByClr = m.Cvls.ToDictionary(c => c.ClrType);

        foreach (var spec in m.SpecificationTemplates)
        foreach (var entClr in spec.EntityTypeClrTypes)
        {
            if (!entityByClr.TryGetValue(entClr, out var ent)) continue;

            foreach (var f in ent.Fields)
            {
                if (f.Attributes.OfType<CompletenessRuleAttribute>().Any())
                    r.Error("MM070",
                        $"SpecificationTemplate '{spec.TemplateId}' includes field '{f.Id}' which has completeness rules — not supported by inriver.");

                if (f.Field.Cvl is { } fieldCvl
                    && cvlByClr.TryGetValue(fieldCvl, out var cvl)
                    && cvl.ParentCvlClrType is not null)
                {
                    r.Error("MM071",
                        $"SpecificationTemplate '{spec.TemplateId}' includes field '{f.Id}' bound to parent-child CVL '{cvl.CvlId}' — not supported by inriver.");
                }
            }
        }
    }

    private static void CheckReservedIds(ValidationResult r, LoadedModel m)
    {
        foreach (var (_, f) in AllFields(m))
        {
            if (ReservedSystemFieldIdsImpl.Contains(f.PropertyName))
                r.ErrorOnField("MM080", $"Field '{f.Id}' uses a reserved system property name '{f.PropertyName}'.", f);
        }
    }

    private static void CheckExpressions(ValidationResult r, LoadedModel m)
    {
        var fieldIds = AllFields(m).Select(t => t.Field.Id).ToHashSet(StringComparer.Ordinal);
        var linkIds = m.LinkTypes.Select(l => l.LinkTypeId).ToHashSet(StringComparer.Ordinal);
        var cvlValueIndex = m.Cvls
            .SelectMany(c => c.Values.Select(v => (c.CvlId, v.Key)))
            .ToHashSet();

        // Build expression-dependency edges (field A -> field B means A's expression reads B) for cycle detection.
        var edges = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var (_, f) in AllFields(m))
        {
            if (f.Field.RawDefaultExpression is not { } expr) continue;

            if (!ExpressionSupportedTypes.Contains(f.DataType))
            {
                r.ErrorOnField("MM090",
                    $"Field '{f.Id}' has DefaultExpression but DataType '{f.DataType}' is not supported by inriver Expression Engine.",
                    f);
                continue;
            }

            var refs = ExpressionRefCollector.Collect(expr);
            edges[f.Id] = new HashSet<string>(refs.FieldIds, StringComparer.Ordinal);

            foreach (var fid in refs.FieldIds.Where(fid => !fieldIds.Contains(fid)))
                r.ErrorOnField("MM091", $"Field '{f.Id}' DefaultExpression references unknown field '{fid}'.", f);

            foreach (var lid in refs.LinkTypeIds.Where(lid => !linkIds.Contains(lid)))
                r.ErrorOnField("MM092", $"Field '{f.Id}' DefaultExpression references unknown link type '{lid}'.", f);

            foreach (var (cvlId, key) in refs.CvlValues.Where(cv => !cvlValueIndex.Contains(cv)))
                r.ErrorOnField("MM093", $"Field '{f.Id}' DefaultExpression references unknown CVL value '{cvlId}.{key}'.", f);
        }

        var visited = new Dictionary<string, NodeState>(StringComparer.Ordinal);
        foreach (var node in edges.Keys)
            DetectCycles(node, edges, visited, r);
    }

    private enum NodeState { Unvisited, InStack, Done }

    private static void DetectCycles(
        string node,
        Dictionary<string, HashSet<string>> edges,
        Dictionary<string, NodeState> visited,
        ValidationResult r)
    {
        visited.TryGetValue(node, out var state);
        if (state == NodeState.Done) return;
        if (state == NodeState.InStack)
        {
            r.Error("MM094", $"Cyclical DefaultExpression dependency involving field '{node}'.");
            return;
        }

        visited[node] = NodeState.InStack;
        if (edges.TryGetValue(node, out var deps))
        {
            foreach (var d in deps)
                DetectCycles(d, edges, visited, r);
        }
        visited[node] = NodeState.Done;
    }
}
